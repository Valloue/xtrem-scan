using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;
using XtremScan.Scanning;
using XtremScan.Services;

namespace XtremScan.ViewModels;

public partial class ScanViewModel : ObservableObject
{
    private readonly IScannerService _scanner;
    private readonly ISettingsService _settings;
    private readonly ILogger<ScanViewModel> _logger;
    private bool _loadingDevices;
    private bool _applyingSource;
    private bool _suppressDpiSync;
    private bool _loadingDraft;
    private bool _publishingRotation;
    private bool _syncingZoom;
    private int _progressEpoch;
    private int _dpiX;
    private int _dpiY;

    public ScanViewModel(IScannerService scanner, ISettingsService settings, ILogger<ScanViewModel> logger)
    {
        _scanner = scanner;
        _settings = settings;
        _logger = logger;
        Pages.CollectionChanged += (_, _) => PersistDraft();
        LoadDraft();
    }

    public ObservableCollection<DocumentPage> Pages { get; } = [];

    public IReadOnlyList<ScannerDevice> Devices { get; private set; } = [];

    public IReadOnlyList<ScanSourceCapabilities> Sources { get; private set; } = [];

    public IReadOnlyList<int> ResolutionChoices { get; private set; } = [];

    public IReadOnlyList<NamedValue> ColorModes { get; private set; } = [];

    public IReadOnlyList<NamedValue> BitDepths { get; private set; } = [];

    public IReadOnlyList<ScanFormat> Formats { get; private set; } = [];

    [ObservableProperty]
    private ScannerDevice? selectedDevice;

    [ObservableProperty]
    private ScanSourceCapabilities? selectedSource;

    [ObservableProperty]
    private int selectedDpi;

    [ObservableProperty]
    private NamedValue? selectedColor;

    [ObservableProperty]
    private NamedValue? selectedDepth;

    [ObservableProperty]
    private ScanFormat? selectedFormat;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool useResolutionList;

    [ObservableProperty]
    private int minimumDpi;

    [ObservableProperty]
    private int maximumDpi = 1;

    [ObservableProperty]
    private int stepDpi = 1;

    [ObservableProperty]
    private string resolutionSummary = "";

    [ObservableProperty]
    private string opticalSummary = "";

    [ObservableProperty]
    private string status = "Choisis un scanner.";

    [ObservableProperty]
    private string? error;

    [ObservableProperty]
    private string? warning;

    [ObservableProperty]
    private ImageSource? preview;

    [ObservableProperty]
    private DocumentPage? selectedPage;

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private int scanPercent;

    public bool CanRecognizeText => !IsBusy && Pages.Count > 0;

    public bool HasDocument => Pages.Count > 0;

    public string ScanPercentText => ScanPercent + " %";

    public string ScanLabel => SelectedFormat?.Id == ScanFormats.Pdf ? "Ajouter une page" : "Scanner";

    [ObservableProperty]
    private string ocrSummary = "";

    [ObservableProperty]
    private double rotationDegrees;

    public async Task InitializeAsync()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            Error = null;
            var devices = await _scanner.ListDevicesAsync();
            Devices = devices;
            OnPropertyChanged(nameof(Devices));
            var preferred = devices.FirstOrDefault(device => device.Id == _settings.LastDeviceId) ?? devices.FirstOrDefault();
            _loadingDevices = true;
            SelectedDevice = preferred;
            _loadingDevices = false;
            Status = devices.Count == 0
                ? "Aucun scanner détecté. Branche l'imprimante, installe son pilote, puis actualise."
                : devices.Count + " scanner(s).";
            if (preferred is not null)
            {
                await LoadSourcesAsync(preferred.Id);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Liste des scanners impossible");
            Error = "Impossible de lister les scanners.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedDeviceChanged(ScannerDevice? value)
    {
        if (_loadingDevices || value is null)
        {
            if (value is null)
            {
                Sources = [];
                OnPropertyChanged(nameof(Sources));
                SelectedSource = null;
            }

            return;
        }

        _settings.LastDeviceId = value.Id;
        _ = LoadSourcesAsync(value.Id);
    }

    partial void OnSelectedSourceChanged(ScanSourceCapabilities? value)
    {
        if (value is null)
        {
            return;
        }

        ColorModes = value.ColorModes;
        BitDepths = value.BitDepths;
        Formats = value.Formats;
        OnPropertyChanged(nameof(ColorModes));
        OnPropertyChanged(nameof(BitDepths));
        OnPropertyChanged(nameof(Formats));

        _applyingSource = true;
        SelectedColor = ColorModes.FirstOrDefault(mode => mode.Value == _settings.LastColorMode)
            ?? ColorModes.FirstOrDefault(mode => mode.Value == 3)
            ?? ColorModes.FirstOrDefault();
        SelectedDepth = BitDepths.FirstOrDefault(mode => mode.Value == 24) ?? BitDepths.LastOrDefault();
        SelectedFormat = Formats.FirstOrDefault(format => format.Id == _settings.LastFormatId) ?? Formats.FirstOrDefault();
        if (_settings.LastDpiX is int savedX && savedX > 0)
        {
            _dpiX = savedX;
            _dpiY = _settings.LastDpiY ?? savedX;
        }

        _applyingSource = false;
        OnPropertyChanged(nameof(ScanLabel));
        BindResolution(value, useOpticalDefault: !(_settings.LastDpiX is int stored && stored > 0));
        Warning = value.Horizontal.Maximum <= 300 && value.Vertical.Maximum <= 300
            ? "Ce pilote ne déclare que " + value.Horizontal.Maximum + " dpi. Le pilote de classe Windows bride souvent les multifonctions réseau : installe le pilote du constructeur."
            : null;
    }

    partial void OnSelectedDpiChanged(int value)
    {
        if (_suppressDpiSync || SelectedSource is null)
        {
            return;
        }

        var snapped = ActiveHorizontal(SelectedSource).Snap(value);
        if (snapped != value)
        {
            SelectedDpi = snapped;
            return;
        }

        var vertical = ActiveVertical(SelectedSource).Snap(snapped);
        ApplyPair(SelectedSource, snapped, vertical);
        if (!_applyingSource)
        {
            _settings.LastDpiX = _dpiX;
            _settings.LastDpiY = _dpiY;
        }

        UpdateSummaries(SelectedSource);
        OnPropertyChanged(nameof(DpiBox));
    }

    public double DpiBox
    {
        get => SelectedDpi;
        set => SelectedDpi = (int)Math.Round(value);
    }

    [RelayCommand]
    private void UseMaximum()
    {
        if (SelectedSource is null)
        {
            return;
        }

        var horizontal = ActiveHorizontal(SelectedSource);
        var vertical = ActiveVertical(SelectedSource);
        _suppressDpiSync = true;
        SelectedDpi = horizontal.Maximum;
        _suppressDpiSync = false;
        ApplyPair(SelectedSource, horizontal.Maximum, vertical.Maximum);
        _settings.LastDpiX = _dpiX;
        _settings.LastDpiY = _dpiY;
        UpdateSummaries(SelectedSource);
        OnPropertyChanged(nameof(DpiBox));
    }

    [RelayCommand]
    private Task PreviewAsync() => AcquireAsync(preview: true);

    [RelayCommand]
    private Task ScanAsync() => AcquireAsync(preview: false);

    private async Task LoadSourcesAsync(string deviceId)
    {
        try
        {
            IsBusy = true;
            Error = null;
            var sources = await _scanner.ReadCapabilitiesAsync(deviceId);
            if (SelectedDevice?.Id != deviceId)
            {
                return;
            }

            Sources = sources;
            OnPropertyChanged(nameof(Sources));
            SelectedSource = sources.FirstOrDefault();
            Status = SelectedSource is null
                ? "Capacités lues."
                : SelectedSource.Label + " : " + SelectedSource.Horizontal.Minimum + " à "
                    + SelectedSource.Horizontal.Maximum + " dpi horizontal, "
                    + SelectedSource.Vertical.Minimum + " à " + SelectedSource.Vertical.Maximum + " dpi vertical.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Lecture des capacités impossible");
            Error = exception is InvalidOperationException ? exception.Message : "Impossible de lire les capacités de ce scanner.";
            Sources = [];
            OnPropertyChanged(nameof(Sources));
            SelectedSource = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AcquireAsync(bool preview)
    {
        if (IsBusy || SelectedDevice is null || SelectedSource is null)
        {
            return;
        }

        var buildingPdf = !preview && SelectedFormat?.Id == ScanFormats.Pdf;
        string directory;
        if (preview)
        {
            directory = AppPaths.Preview;
        }
        else if (buildingPdf)
        {
            directory = AppPaths.Document;
        }
        else
        {
            var picked = await PickFolderAsync();
            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            directory = picked;
        }

        var formatId = buildingPdf ? ScanFormats.Jpeg : SelectedFormat?.Id;
        try
        {
            IsBusy = true;
            IsScanning = true;
            ScanPercent = 0;
            Error = null;
            Status = preview ? "Aperçu en cours… 0 %" : "Scan en cours… 0 %";
            Directory.CreateDirectory(directory);
            var epoch = ++_progressEpoch;
            var progress = new Progress<int>(percent =>
            {
                if (epoch != _progressEpoch)
                {
                    return;
                }

                ScanPercent = percent;
                Status = (preview ? "Aperçu en cours… " : "Scan en cours… ") + percent + " %";
            });
            var result = await _scanner.ScanAsync(new ScanRequest
            {
                DeviceId = SelectedDevice.Id,
                Source = SelectedSource.Kind,
                DpiX = _dpiX,
                DpiY = _dpiY,
                ColorMode = SelectedColor?.Value,
                BitDepth = SelectedDepth?.Value,
                FormatId = formatId,
                OutputDirectory = directory,
                Preview = preview,
                Progress = progress,
            });

            _progressEpoch++;
            if (buildingPdf)
            {
                AddPage(result);
                Status = "Page " + Pages.Count + " ajoutée. Pose la suivante sur la vitre, puis Ajouter une page (Ctrl+Entrée).";
            }
            else
            {
                Preview = LoadPreview(result.DisplayPath ?? result.FilePath, 1600);
                Status = Describe(result, preview);
            }
        }
        catch (Exception exception)
        {
            _progressEpoch++;
            _logger.LogError(exception, "Acquisition impossible");
            Error = exception is InvalidOperationException
                ? exception.Message
                : "L'acquisition a échoué. Le détail est dans le journal.";
            Status = "Échec.";
        }
        finally
        {
            IsScanning = false;
            IsBusy = false;
        }
    }

    partial void OnScanPercentChanged(int value) => OnPropertyChanged(nameof(ScanPercentText));

    partial void OnIsBusyChanged(bool value)
    {
        SaveDocumentCommand.NotifyCanExecuteChanged();
        RemovePageCommand.NotifyCanExecuteChanged();
        ClearDocumentCommand.NotifyCanExecuteChanged();
        ResetCropCommand.NotifyCanExecuteChanged();
        StraightenCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRecognizeText));
        RotateLeftCommand.NotifyCanExecuteChanged();
        RotateRightCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFormatChanged(ScanFormat? value)
    {
        OnPropertyChanged(nameof(ScanLabel));
        if (!_applyingSource && value is not null)
        {
            _settings.LastFormatId = value.Id;
        }
    }

    partial void OnSelectedPageChanged(DocumentPage? value)
    {
        _logger.LogDebug(
            "Page sélectionnée : {File}, quart {Quarter}, skew {Skew}°",
            value is null ? "aucune" : Path.GetFileName(value.JpegPath),
            value?.QuarterTurns ?? 0,
            value?.SkewDegrees.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "0");
        if (value is not null)
        {
            Preview = value.Image;
        }

        RemovePageCommand.NotifyCanExecuteChanged();
        ResetCropCommand.NotifyCanExecuteChanged();
        StraightenCommand.NotifyCanExecuteChanged();
        RotateLeftCommand.NotifyCanExecuteChanged();
        RotateRightCommand.NotifyCanExecuteChanged();
        PublishRotation();
        _syncingZoom = true;
        Zoom = value is null || value.Zoom < 0.05 ? 1 : value.Zoom;
        _syncingZoom = false;
    }

    [RelayCommand(CanExecute = nameof(CanSaveDocument))]
    private async Task SaveDocumentAsync()
    {
        if (Pages.Count == 0)
        {
            return;
        }

        var folder = await PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, ScanFileNames.Build(false, ".pdf"));
        try
        {
            IsBusy = true;
            Error = null;
            var specs = new List<PdfPageSpec>(Pages.Count);
            for (var index = 0; index < Pages.Count; index++)
            {
                var page = Pages[index];
                specs.Add(ToSpec(page, page.Runs));
            }

            await Task.Run(() => PdfDocumentWriter.Write(path, specs));
            Status = "PDF enregistré. " + path;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Écriture du PDF impossible");
            Error = exception is InvalidOperationException ? exception.Message : "Impossible d'enregistrer le PDF.";
            Status = "Échec.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemovePage))]
    private void RemovePage()
    {
        if (SelectedPage is null)
        {
            return;
        }

        var page = SelectedPage;
        var index = Pages.IndexOf(page);
        Pages.Remove(page);
        TryDelete(page.JpegPath);
        SelectedPage = Pages.Count == 0 ? null : Pages[Math.Clamp(index, 0, Pages.Count - 1)];
        if (SelectedPage is null)
        {
            Preview = null;
        }

        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(ScanLabel));
        OnPropertyChanged(nameof(CanRecognizeText));
        SaveDocumentCommand.NotifyCanExecuteChanged();
        ClearDocumentCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSaveDocument))]
    private void ClearDocument()
    {
        foreach (var page in Pages)
        {
            TryDelete(page.JpegPath);
        }

        Pages.Clear();
        SelectedPage = null;
        Preview = null;
        OcrSummary = "";
        DraftStore.Delete();
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(ScanLabel));
        OnPropertyChanged(nameof(CanRecognizeText));
        SaveDocumentCommand.NotifyCanExecuteChanged();
        ClearDocumentCommand.NotifyCanExecuteChanged();
        Status = "Document vidé.";
    }

    public void ApplyCrop(int x, int y, int width, int height)
    {
        if (SelectedPage is null)
        {
            return;
        }

        SelectedPage.CropX = x;
        SelectedPage.CropY = y;
        SelectedPage.CropWidth = width;
        SelectedPage.CropHeight = height;
    }

    public void ApplyTurn(int quarterTurns)
    {
        if (SelectedPage is null)
        {
            return;
        }

        SelectedPage.QuarterTurns = quarterTurns & 3;
        PublishRotation();
    }

    public void ApplyRotation(double degrees)
    {
        if (SelectedPage is null)
        {
            return;
        }

        SelectedPage.SkewDegrees = degrees;
        PublishRotation();
        _logger.LogInformation(
            "Angle image {Degrees}°, format inchangé (quart {Quarter})",
            degrees.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            SelectedPage.QuarterTurns & 3);
    }

    public string AngleText => RotationDegrees.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " °";

    [ObservableProperty]
    private double zoom = 1;

    public string ZoomText => Math.Round(PageFrame.NormalizeZoom(Zoom) * 100d).ToString(System.Globalization.CultureInfo.CurrentCulture) + " %";

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomText));
        if (_syncingZoom || SelectedPage is null)
        {
            return;
        }

        SelectedPage.Zoom = PageFrame.NormalizeZoom(value);
        SelectedPage.PinContentAnchor();
    }

    public void ApplyFrame(double x, double y, double width, double height)
    {
        SelectedPage?.SetFrame(x, y, width, height);
    }

    public void ApplyFrameTurn(int turns)
    {
        if (SelectedPage is null)
        {
            return;
        }

        SelectedPage.FrameTurns = turns & 3;
        SelectedPage.PinContentAnchor();
    }

    partial void OnRotationDegreesChanged(double value)
    {
        OnPropertyChanged(nameof(AngleText));
        if (_publishingRotation || SelectedPage is null || double.IsNaN(value) || double.IsInfinity(value))
        {
            return;
        }

        ApplyRotation(value);
    }

    private void PublishRotation()
    {
        _publishingRotation = true;
        RotationDegrees = SelectedPage?.SkewDegrees ?? 0;
        _publishingRotation = false;
    }

    [RelayCommand(CanExecute = nameof(CanRemovePage))]
    private void RotateLeft() => RotateSelection(-1);

    [RelayCommand(CanExecute = nameof(CanRemovePage))]
    private void RotateRight() => RotateSelection(1);

    private void RotateSelection(int delta)
    {
        if (SelectedPage is null)
        {
            return;
        }

        SelectedPage.QuarterTurns = (SelectedPage.QuarterTurns + delta) & 3;
        SelectedPage.FrameTurns = 0;
        PublishRotation();
    }

    [RelayCommand(CanExecute = nameof(CanRemovePage))]
    private void ResetCrop()
    {
        if (SelectedPage is null)
        {
            return;
        }

        SelectedPage.SetFrame(0, 0, 1, 1);
        SelectedPage.FrameTurns = 0;
        SelectedPage.ImageOffsetX = 0;
        SelectedPage.ImageOffsetY = 0;
        SelectedPage.MoveImage(0, 0);
        _syncingZoom = true;
        Zoom = 1;
        _syncingZoom = false;
        SelectedPage.Zoom = 1;
        Status = "Image replacée sur la page.";
    }

    [RelayCommand(CanExecute = nameof(CanRemovePage))]
    private async Task StraightenAsync()
    {
        var page = SelectedPage;
        if (page is null)
        {
            return;
        }

        try
        {
            Status = "Recherche du sens du texte…";
            _logger.LogInformation(
                "Redresser démarré (quart {Quarter}, inclinaison {Skew}°) — {File}",
                page.QuarterTurns & 3,
                page.SkewDegrees.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                Path.GetFileName(page.JpegPath));
            var orientation = await PageDeskew.SuggestOrientationAsync(page.JpegPath);
            if (!ReferenceEquals(SelectedPage, page))
            {
                return;
            }

            var quarter = orientation.QuarterTurns & 3;
            var skew = Math.Abs(orientation.SkewDegrees) > PageSkew.MaxFineDegrees ? 0 : orientation.SkewDegrees;
            var landscape = (quarter & 1) == 1;
            if (quarter == (page.QuarterTurns & 3) && Math.Abs(skew - page.SkewDegrees) < PageSkew.PixelDegrees)
            {
                Status = "Le texte est déjà à l'endroit.";
                _logger.LogInformation("Redresser inchangé (quart {Quarter}, inclinaison {Skew}°)", quarter, skew);
                return;
            }

            page.QuarterTurns = quarter;
            page.FrameTurns = 0;
            page.SkewDegrees = skew;
            PublishRotation();
            _logger.LogInformation(
                "Redresser appliqué ({Format}, quart {Quarter}, inclinaison {Skew}°)",
                landscape ? "paysage" : "portrait",
                quarter,
                skew.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            Status = landscape
                ? "Texte à l'endroit, page en paysage."
                : "Texte à l'endroit, page en portrait.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Redressement impossible");
            Error = "Impossible de redresser cette page.";
        }
    }

    public async Task RecognizeDocumentAsync(IProgress<string> progress)
    {
        if (Pages.Count == 0)
        {
            return;
        }

        if (!PageOcr.LanguageReady())
        {
            _logger.LogWarning("Tesseract : français introuvable");
            Warning = "Le français Tesseract est introuvable. Le PDF sera enregistré sans texte.";
            return;
        }

        Warning = null;
        var words = 0;
        try
        {
            for (var index = 0; index < Pages.Count; index++)
            {
                var page = Pages[index];
                progress.Report("Page " + (index + 1) + " sur " + Pages.Count + "…");
                _logger.LogInformation(
                    "OCR demandé — page {Index}, {File}, quart {Quarter}, skew {Skew}°",
                    index + 1,
                    Path.GetFileName(page.JpegPath),
                    page.QuarterTurns,
                    page.SkewDegrees.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                var result = await PageOcr.RecognizeAsync(ToSpec(page, []));
                page.SetOcr(result);
                words += result.Boxes.Count;
                _logger.LogInformation("OCR page {Index} : {Count} mot(s)", index + 1, result.Boxes.Count);
            }

            OcrSummary = words + (words > 1 ? " mots reconnus." : " mot reconnu.");
            Status = OcrSummary;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "OCR indisponible");
            Warning = exception is InvalidOperationException
                ? exception.Message
                : "La reconnaissance a échoué. Le détail est dans le journal.";
            OcrSummary = "";
        }
    }

    private static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private bool CanSaveDocument() => !IsBusy && Pages.Count > 0;

    private bool CanRemovePage() => !IsBusy && SelectedPage is not null;

    private void AddPage(ScanResult result)
    {
        var path = result.DisplayPath ?? result.FilePath;
        var picture = JpegPicture.Read(path);
        var page = new DocumentPage
        {
            JpegPath = path,
            PixelWidth = picture.Width,
            PixelHeight = picture.Height,
            DpiX = picture.DpiX ?? (result.FileDpi ?? result.RequestedDpiX),
            DpiY = picture.DpiY ?? (result.FileDpi ?? result.RequestedDpiY),
            Image = LoadPreview(path, 1400),
            Thumbnail = LoadPreview(path, 160),
            CropWidth = picture.Width,
            CropHeight = picture.Height,
        };
        Watch(page);
        Pages.Add(page);
        SelectedPage = page;
        PersistDraft();
        _ = OrientNewPageAsync(page);
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(ScanLabel));
        SaveDocumentCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRecognizeText));
        ClearDocumentCommand.NotifyCanExecuteChanged();
        RemovePageCommand.NotifyCanExecuteChanged();
    }

    private async Task OrientNewPageAsync(DocumentPage page)
    {
        try
        {
            _logger.LogInformation("Orientation auto — {File}", Path.GetFileName(page.JpegPath));
            var orientation = await PageDeskew.SuggestOrientationAsync(page.JpegPath);
            if (!ReferenceEquals(SelectedPage, page))
            {
                return;
            }

            var quarter = orientation.QuarterTurns & 3;
            var skew = Math.Abs(orientation.SkewDegrees) > PageSkew.MaxFineDegrees ? 0 : orientation.SkewDegrees;
            if (quarter == (page.QuarterTurns & 3) && Math.Abs(skew - page.SkewDegrees) < PageSkew.PixelDegrees)
            {
                _logger.LogInformation("Orientation auto inchangée (quart {Quarter}, inclinaison {Skew}°)", quarter, skew);
                return;
            }

            page.QuarterTurns = quarter;
            page.SkewDegrees = skew;
            PublishRotation();
            var landscape = (quarter & 1) == 1;
            _logger.LogInformation(
                "Orientation auto appliquée — {Format}, quart {Quarter}, inclinaison {Skew}°",
                landscape ? "paysage" : "portrait",
                quarter,
                skew.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            Status = landscape
                ? "Page mise en paysage pour la lecture."
                : quarter == 0
                    ? "Page mise en portrait pour la lecture."
                    : "Page tournée de " + (quarter * 90) + "° pour la lecture.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Orientation automatique ignorée");
        }
    }

    private static PdfPageSpec ToSpec(DocumentPage page, IReadOnlyList<PdfTextRun> text)
    {
        return new PdfPageSpec
        {
            JpegPath = page.JpegPath,
            CropX = page.CropX,
            CropY = page.CropY,
            CropWidth = page.CropWidth,
            CropHeight = page.CropHeight,
            QuarterTurns = page.QuarterTurns,
            SkewDegrees = page.SkewDegrees,
            FrameX = page.FrameX,
            FrameY = page.FrameY,
            FrameWidth = page.FrameWidth,
            FrameHeight = page.FrameHeight,
            Zoom = page.Zoom,
            FrameTurns = page.FrameTurns,
            AnchorX = page.AnchorX,
            AnchorY = page.AnchorY,
            ImageOffsetX = page.ImageOffsetX,
            ImageOffsetY = page.ImageOffsetY,
            FallbackDpiX = page.DpiX,
            FallbackDpiY = page.DpiY,
            Text = text,
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    partial void OnSelectedColorChanged(NamedValue? value)
    {
        if (_applyingSource || value is null || SelectedSource is null)
        {
            return;
        }

        _settings.LastColorMode = value.Value;
        BindResolution(SelectedSource, useOpticalDefault: false);
    }

    private void BindResolution(ScanSourceCapabilities source, bool useOpticalDefault)
    {
        var horizontal = ActiveHorizontal(source);
        var vertical = ActiveVertical(source);
        ResolutionChoices = horizontal.Values ?? [];
        UseResolutionList = ResolutionChoices.Count > 0;
        MinimumDpi = horizontal.Minimum;
        MaximumDpi = Math.Max(1, horizontal.Maximum);
        StepDpi = Math.Max(1, horizontal.Step);
        OnPropertyChanged(nameof(ResolutionChoices));

        var dpiX = useOpticalDefault ? SuggestDpi(horizontal, source.OpticalX) : horizontal.Snap(_dpiX);
        var dpiY = useOpticalDefault ? SuggestDpi(vertical, source.OpticalY) : vertical.Snap(_dpiY);
        _suppressDpiSync = true;
        SelectedDpi = dpiX;
        _suppressDpiSync = false;
        ApplyPair(source, dpiX, dpiY);
        UpdateSummaries(source);
        OnPropertyChanged(nameof(DpiBox));
    }

    private ResolutionAxis ActiveHorizontal(ScanSourceCapabilities source)
    {
        return SelectedColor?.Value == 2 && source.GrayscaleHorizontal is { } axis
            ? axis
            : source.Horizontal;
    }

    private ResolutionAxis ActiveVertical(ScanSourceCapabilities source)
    {
        return SelectedColor?.Value == 2 && source.GrayscaleVertical is { } axis
            ? axis
            : source.Vertical;
    }

    private void ApplyPair(ScanSourceCapabilities source, int dpiX, int dpiY)
    {
        _dpiX = ActiveHorizontal(source).Snap(dpiX);
        _dpiY = ActiveVertical(source).Snap(dpiY);
    }

    private static int SuggestDpi(ResolutionAxis axis, int? optical)
    {
        if (optical is int value && value > 0)
        {
            return axis.Snap(value);
        }

        return axis.Maximum;
    }

    private void UpdateSummaries(ScanSourceCapabilities source)
    {
        ResolutionSummary = _dpiX + " × " + _dpiY + " dpi";
        OpticalSummary = source.OpticalX is int x && source.OpticalY is int y && x > 0 && y > 0
            ? "Optique : " + x + " × " + y + " dpi"
            : "";
    }

    private static ImageSource LoadPreview(string path, int decodeWidth)
    {
        var bitmap = new BitmapImage();
        // L'affichage est borné. Le fichier sur le disque n'est pas réduit.
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.UriSource = new Uri(path);
        return bitmap;
    }

    private static string Describe(ScanResult result, bool preview)
    {
        var header = result.FileDpi is int dpi
            ? "En-tête du fichier : " + dpi + " dpi."
            : "L'en-tête du fichier ne répète pas le dpi (demandé " + result.RequestedDpiX + " × " + result.RequestedDpiY + ").";
        var title = preview ? "Aperçu enregistré." : "Scan enregistré.";
        return title + " " + header + " " + result.FilePath;
    }

    private void LoadDraft()
    {
        _loadingDraft = true;
        foreach (var draft in DraftStore.Load())
        {
            var path = Path.Combine(AppPaths.Document, draft.File);
            var page = new DocumentPage
            {
                JpegPath = path,
                PixelWidth = Math.Max(1, draft.PixelWidth),
                PixelHeight = Math.Max(1, draft.PixelHeight),
                DpiX = draft.DpiX,
                DpiY = draft.DpiY,
                Image = LoadPreview(path, 1400),
                Thumbnail = LoadPreview(path, 160),
                CropX = draft.CropX,
                CropY = draft.CropY,
                CropWidth = draft.CropWidth > 0 ? draft.CropWidth : Math.Max(1, draft.PixelWidth),
                CropHeight = draft.CropHeight > 0 ? draft.CropHeight : Math.Max(1, draft.PixelHeight),
                QuarterTurns = draft.QuarterTurns,
                SkewDegrees = draft.SkewDegrees,
                FrameX = draft.FrameX,
                FrameY = draft.FrameY,
                FrameWidth = draft.FrameWidth > 0 ? draft.FrameWidth : 1,
                FrameHeight = draft.FrameHeight > 0 ? draft.FrameHeight : 1,
                Zoom = draft.Zoom < 0.05 ? 1 : draft.Zoom,
                FrameTurns = draft.FrameTurns,
                AnchorX = draft.AnchorX ?? double.NaN,
                AnchorY = draft.AnchorY ?? double.NaN,
                ImageOffsetX = draft.ImageOffsetX,
                ImageOffsetY = draft.ImageOffsetY,
            };
            if (double.IsNaN(page.AnchorX) || double.IsNaN(page.AnchorY))
            {
                page.PinContentAnchor();
            }
            Watch(page);
            Pages.Add(page);
        }

        SelectedPage = Pages.FirstOrDefault();
        if (Pages.Count > 0)
        {
            OnPropertyChanged(nameof(HasDocument));
            OnPropertyChanged(nameof(CanRecognizeText));
            OnPropertyChanged(nameof(ScanLabel));
            Status = Pages.Count + " page(s) reprises. Pose la suivante ou enregistre le PDF.";
        }

        _loadingDraft = false;
    }

    private void PersistDraft()
    {
        if (_loadingDraft)
        {
            return;
        }

        DraftStore.Save(Pages.Select(page => new DraftPage
        {
            File = Path.GetFileName(page.JpegPath),
            PixelWidth = page.PixelWidth,
            PixelHeight = page.PixelHeight,
            DpiX = page.DpiX,
            DpiY = page.DpiY,
            CropX = page.CropX,
            CropY = page.CropY,
            CropWidth = page.CropWidth,
            CropHeight = page.CropHeight,
            QuarterTurns = page.QuarterTurns,
            SkewDegrees = page.SkewDegrees,
            FrameX = page.FrameX,
            FrameY = page.FrameY,
            FrameWidth = page.FrameWidth,
            FrameHeight = page.FrameHeight,
            Zoom = page.Zoom,
            FrameTurns = page.FrameTurns,
            AnchorX = double.IsNaN(page.AnchorX) ? null : page.AnchorX,
            AnchorY = double.IsNaN(page.AnchorY) ? null : page.AnchorY,
            ImageOffsetX = page.ImageOffsetX,
            ImageOffsetY = page.ImageOffsetY,
        }).ToList());
    }

    private void Watch(DocumentPage page)
    {
        page.PropertyChanged += (_, args) =>
        {
            if (_loadingDraft)
            {
                return;
            }

            if (args.PropertyName is nameof(DocumentPage.CropX) or nameof(DocumentPage.CropY)
                or nameof(DocumentPage.CropWidth) or nameof(DocumentPage.CropHeight)
                or nameof(DocumentPage.QuarterTurns) or nameof(DocumentPage.SkewDegrees)
                or nameof(DocumentPage.FrameX) or nameof(DocumentPage.FrameTurns)
                or nameof(DocumentPage.Zoom) or nameof(DocumentPage.ImageOffsetX))
            {
                page.SetOcr(null);
                if (ReferenceEquals(page, SelectedPage))
                {
                    OcrSummary = "";
                }

                PersistDraft();
            }
        };
    }
}
