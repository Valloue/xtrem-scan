using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using Windows.Foundation;
using XtremScan.Scanning;
using XtremScan.Services;
using XtremScan.ViewModels;
using Path = System.IO.Path;

namespace XtremScan.Pages;

public sealed partial class ScanPage : Page
{
    private enum DragMode
    {
        None,
        Draw,
        Move,
        Rotate,
        Resize,
    }

    private bool _updatingResolution;
    private bool _zoomReady;
    private DocumentPage? _hooked;
    private DragMode _mode;
    private double _displayScale = 1;
    private bool _wheelDrag;
    private double _wheelStartX;
    private double _wheelStartAngle;
    private bool _panView;
    private double _panX;
    private double _panY;
    private double _panOffsetX;
    private double _panOffsetY;
    private int _previewGeneration;
    private byte[]? _decodedPixels;
    private int _decodedWidth;
    private int _decodedHeight;
    private string? _decodedPath;
    private double _viewX;
    private double _viewY;
    private double _viewZoom = 1;

    private static readonly ILogger Log = AppLog.For(nameof(ScanPage));

    public ScanPage()
    {
        ViewModel = App.Services.GetRequiredService<ScanViewModel>();
        InitializeComponent();
        Log.Debug("ScanPage initialisée");
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ScanViewModel.SelectedDpi) or nameof(ScanViewModel.ResolutionChoices))
            {
                ShowResolution(ViewModel.SelectedDpi);
            }

            if (args.PropertyName is nameof(ScanViewModel.SelectedPage) or nameof(ScanViewModel.HasDocument))
            {
                Hook(ViewModel.SelectedPage);
            }

            if (args.PropertyName is nameof(ScanViewModel.RotationDegrees) or nameof(ScanViewModel.AngleText))
            {
                DrawWheel();
            }
        };
    }

    public ScanViewModel ViewModel { get; }

    private void ResolutionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingResolution || ResolutionList.SelectedItem is not int dpi)
        {
            return;
        }

        ViewModel.SelectedDpi = dpi;
    }

    private void ResolutionBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updatingResolution || ViewModel.SelectedSource is null || double.IsNaN(args.NewValue))
        {
            return;
        }

        ViewModel.SelectedDpi = (int)Math.Round(args.NewValue);
    }

    private void ShowResolution(int dpi)
    {
        _updatingResolution = true;
        if (ViewModel.ResolutionChoices.Contains(dpi))
        {
            ResolutionList.SelectedItem = dpi;
        }

        if (Math.Abs(ResolutionBox.Value - dpi) > 0.1)
        {
            ResolutionBox.Value = dpi;
        }

        _updatingResolution = false;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Log.Debug("ScanPage navigation — appareils {Count}", ViewModel.Devices.Count);
        if (ViewModel.Devices.Count == 0)
        {
            await ViewModel.InitializeAsync();
        }

        Hook(ViewModel.SelectedPage);
        _zoomReady = true;
    }

    private void Hook(DocumentPage? page)
    {
        if (_hooked is not null)
        {
            _hooked.PropertyChanged -= Page_PropertyChanged;
        }

        _hooked = page;
        if (page is not null)
        {
            page.PropertyChanged += Page_PropertyChanged;
        }

        _ = ShowPageAsync();
        _viewZoom = 1;
        LayoutView();
    }

    private void Page_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentPage.QuarterTurns) or nameof(DocumentPage.SkewDegrees))
        {
            PlaceCrop();
            DrawWheel();
            return;
        }

        if (e.PropertyName is nameof(DocumentPage.FrameX) or nameof(DocumentPage.FrameTurns) or nameof(DocumentPage.Zoom)
            or nameof(DocumentPage.ImageOffsetX))
        {
            PlaceCrop();
        }

        if (e.PropertyName == nameof(DocumentPage.WordCount))
        {
            PlaceCrop();
            return;
        }

        if (e.PropertyName is nameof(DocumentPage.CropX) or nameof(DocumentPage.CropY)
            or nameof(DocumentPage.CropWidth) or nameof(DocumentPage.CropHeight))
        {
            PlaceCrop();
        }
    }

    private void Stage_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        Canvas.SetLeft(RotateHandle, Math.Max(0, (Stage.ActualWidth - RotateHandle.Width) / 2));
        Canvas.SetTop(RotateHandle, 6);
        LayoutView();
    }

    /// <summary>Ctrl et la molette agrandissent toute la page. La molette seule fait défiler.</summary>
    private void PageView_Wheel(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.SelectedPage is null || !e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
        {
            return;
        }

        var delta = e.GetCurrentPoint(PageView).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        var pointer = e.GetCurrentPoint(PageView).Position;
        var next = Math.Clamp(_viewZoom * (delta > 0 ? 1.12 : 1 / 1.12), 0.5, 6);
        if (Math.Abs(next - _viewZoom) < 0.001)
        {
            e.Handled = true;
            return;
        }

        var oldWidth = Math.Max(1, PageBox.Width);
        var oldHeight = Math.Max(1, PageBox.Height);
        var contentX = PageView.HorizontalOffset + pointer.X;
        var contentY = PageView.VerticalOffset + pointer.Y;
        var boxLeft = Math.Max(0, (PageHost.Width - oldWidth) / 2);
        var boxTop = Math.Max(0, (PageHost.Height - oldHeight) / 2);
        var relX = Math.Clamp((contentX - boxLeft) / oldWidth, 0, 1);
        var relY = Math.Clamp((contentY - boxTop) / oldHeight, 0, 1);
        _viewZoom = next;
        LayoutView();
        var newLeft = Math.Max(0, (PageHost.Width - PageBox.Width) / 2);
        var newTop = Math.Max(0, (PageHost.Height - PageBox.Height) / 2);
        PageView.ChangeView(newLeft + (relX * PageBox.Width) - pointer.X, newTop + (relY * PageBox.Height) - pointer.Y, null, true);
        Log.Debug("Aperçu zoom {Zoom}", _viewZoom.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        e.Handled = true;
    }

    /// <summary>Tant que le clic molette est maintenu, toute la page suit la souris. Au relâchement, ça s'arrête.</summary>
    private void PageView_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.SelectedPage is null || !e.GetCurrentPoint(PageHost).Properties.IsMiddleButtonPressed)
        {
            return;
        }

        var point = e.GetCurrentPoint(PageView).Position;
        _panView = true;
        _panX = point.X;
        _panY = point.Y;
        _panOffsetX = PageView.HorizontalOffset;
        _panOffsetY = PageView.VerticalOffset;
        PageHost.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void PageView_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_panView)
        {
            return;
        }

        if (!e.GetCurrentPoint(PageHost).Properties.IsMiddleButtonPressed)
        {
            StopPan(e);
            return;
        }

        var point = e.GetCurrentPoint(PageView).Position;
        PageView.ChangeView(
            _panOffsetX - (point.X - _panX),
            _panOffsetY - (point.Y - _panY),
            null,
            true);
        e.Handled = true;
    }

    private void PageView_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_panView)
        {
            return;
        }

        StopPan(e);
    }

    private void StopPan(PointerRoutedEventArgs e)
    {
        _panView = false;
        PageHost.ReleasePointerCapture(e.Pointer);
        Log.Debug(
            "Aperçu déplacé, défilement {X},{Y}",
            PageView.HorizontalOffset.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
            PageView.VerticalOffset.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        e.Handled = true;
    }

    private void LayoutView()
    {
        var viewportWidth = Stage.ActualWidth;
        var viewportHeight = Stage.ActualHeight;
        if (viewportWidth < 1 || viewportHeight < 1)
        {
            return;
        }

        var contentWidth = viewportWidth * _viewZoom;
        var contentHeight = viewportHeight * _viewZoom;
        PageHost.Width = Math.Max(viewportWidth, contentWidth);
        PageHost.Height = Math.Max(viewportHeight, contentHeight);
        PageBox.Width = contentWidth;
        PageBox.Height = contentHeight;
    }

    private void Wheel_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        DrawWheel();
    }

    private void Wheel_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.SelectedPage is null || ViewModel.IsBusy)
        {
            return;
        }

        _wheelDrag = true;
        _wheelStartX = e.GetCurrentPoint(AngleWheel).Position.X;
        _wheelStartAngle = ViewModel.RotationDegrees;
        AngleWheel.CapturePointer(e.Pointer);
    }

    private void Wheel_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_wheelDrag)
        {
            return;
        }

        var delta = _wheelStartX - e.GetCurrentPoint(AngleWheel).Position.X;
        ViewModel.ApplyRotation(_wheelStartAngle + (delta / 8d));
    }

    private void Wheel_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_wheelDrag)
        {
            return;
        }

        _wheelDrag = false;
        AngleWheel.ReleasePointerCapture(e.Pointer);
    }

    private void DrawWheel()
    {
        AngleWheel.Children.Clear();
        var width = AngleWheel.ActualWidth;
        if (width < 40)
        {
            return;
        }

        var angle = ViewModel.SelectedPage?.SkewDegrees ?? ViewModel.RotationDegrees;
        const double pixelsPerDegree = 8d;
        var center = width / 2d;
        var span = (width / 2d / pixelsPerDegree) + 1d;
        var from = (int)Math.Floor(angle - span);
        var to = (int)Math.Ceiling(angle + span);
        var tick = new SolidColorBrush(Windows.UI.Color.FromArgb(230, 255, 255, 255));
        for (var degree = from; degree <= to; degree++)
        {
            var x = center + ((degree - angle) * pixelsPerDegree);
            if (x < 0 || x > width)
            {
                continue;
            }

            var major = degree % 10 == 0;
            var mid = degree % 5 == 0;
            var height = major ? 28d : mid ? 18d : 9d;
            AngleWheel.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 42 - height,
                Y2 = 42,
                Stroke = tick,
                StrokeThickness = major ? 1.6 : 1,
            });
        }

        AngleWheel.Children.Add(new Line
        {
            X1 = center,
            X2 = center,
            Y1 = 2,
            Y2 = 44,
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 117, 31)),
            StrokeThickness = 2,
        });
    }

    private async Task ShowPageAsync()
    {
        var page = ViewModel.SelectedPage;
        if (page is null || page.PixelWidth < 1 || page.PixelHeight < 1)
        {
            return;
        }

        Log.Debug("Affichage page {File} {W}x{H}", Path.GetFileName(page.JpegPath), page.PixelWidth, page.PixelHeight);
        Log.Debug(
            "État page {File} : quart {Quarter}, skew {Skew}°, zoom {Zoom}, quart sélection {FrameTurns}, cadre plein {Full}",
            Path.GetFileName(page.JpegPath),
            page.QuarterTurns,
            page.SkewDegrees.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            page.Zoom.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            page.FrameTurns,
            page.IsFullFrame);

        var generation = ++_previewGeneration;
        PlaceCrop();
        try
        {
            if (_decodedPath != page.JpegPath || _decodedPixels is null)
            {
                var decoded = await PagePreview.DecodeAsync(page.JpegPath, page.PixelWidth, page.PixelHeight);
                if (generation != _previewGeneration || !ReferenceEquals(ViewModel.SelectedPage, page))
                {
                    return;
                }

                _decodedPixels = decoded.Pixels;
                _decodedWidth = decoded.Width;
                _decodedHeight = decoded.Height;
                _decodedPath = page.JpegPath;
            }

            var pixels = _decodedPixels;
            var decodedWidth = _decodedWidth;
            var decodedHeight = _decodedHeight;
            if (generation != _previewGeneration || !ReferenceEquals(ViewModel.SelectedPage, page))
            {
                return;
            }

            var source = await PagePreview.ToImageAsync(pixels, decodedWidth, decodedHeight);
            if (generation != _previewGeneration || !ReferenceEquals(ViewModel.SelectedPage, page))
            {
                return;
            }

            PageImage.Source = source;
            ZoomImage.Source = source;
            PlaceCrop();
        }
        catch (Exception)
        {
            if (generation != _previewGeneration || !ReferenceEquals(ViewModel.SelectedPage, page))
            {
                return;
            }

            PageImage.Source = page.Image;
            ZoomImage.Source = page.Image;
            PlaceCrop();
        }
    }

    private void PlaceCrop()
    {
        var page = ViewModel.SelectedPage;
        if (page is null || page.PixelWidth < 1 || page.PixelHeight < 1)
        {
            return;
        }

        var landscape = (page.QuarterTurns & 1) == 1;
        var sheet = IsoA4.SheetPixels(landscape, PagePreview.MaxEdge);
        var image = IsoA4.ImageDisplay(page.PixelWidth, page.PixelHeight, page.DpiX, page.DpiY, PagePreview.MaxEdge);
        var turnedWidth = landscape ? page.PixelHeight : page.PixelWidth;
        _displayScale = sheet.Width / (double)Math.Max(1, turnedWidth);
        Sheet.Width = sheet.Width;
        Sheet.Height = sheet.Height;
        Sheet.Clip = new RectangleGeometry { Rect = new Rect(0, 0, sheet.Width, sheet.Height) };
        PlaceSpin(page, image.Width, image.Height, sheet.Width, sheet.Height);
        CropShape.Points = new PointCollection();
        CropShade.Data = null;
        HandleCanvas.Children.Clear();
        RotateHandle.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        DrawWords(page);
        ZoomHost.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void ZoomSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_zoomReady || Math.Abs(ViewModel.Zoom - e.NewValue) < 0.0001)
        {
            return;
        }

        ViewModel.Zoom = e.NewValue;
    }

    /// <summary>
    /// L'angle de la feuille est une transformation d'écran. Le bitmap n'est pas recalculé à chaque degré.
    /// </summary>
    private void PlaceSpin(DocumentPage page, double rawWidth, double rawHeight, double sheetWidth, double sheetHeight)
    {
        SpinHost.Width = rawWidth;
        SpinHost.Height = rawHeight;
        PageImage.Width = rawWidth;
        PageImage.Height = rawHeight;
        SpinHost.RenderTransformOrigin = new Point(0.5, 0.5);
        SpinHost.RenderTransform = PageSpin(page, sheetWidth, sheetHeight);
        var shift = ImageShift(page, sheetWidth, sheetHeight);
        SpinMove.RenderTransform = new TranslateTransform { X = shift.X, Y = shift.Y };
        ZoomSpin.Width = rawWidth;
        ZoomSpin.Height = rawHeight;
        ZoomImage.Width = rawWidth;
        ZoomImage.Height = rawHeight;
        ZoomSpin.RenderTransformOrigin = new Point(0.5, 0.5);
        ZoomSpin.RenderTransform = PageSpin(page, sheetWidth, sheetHeight);
        ZoomMove.RenderTransform = new TranslateTransform { X = shift.X, Y = shift.Y };
    }

    /// <summary>Déplacement du cadre depuis le point où le zoom a été réglé, en pixels d'écran.</summary>
    private static (double X, double Y) ImageShift(DocumentPage page, double width, double height)
    {
        return (page.ImageOffsetX * width, page.ImageOffsetY * height);
    }

    private static Transform PageSpin(DocumentPage page, double sheetWidth, double sheetHeight)
    {
        var angle = PageRotation.Combine(page.QuarterTurns, page.SkewDegrees);
        var scale = PageFrame.NormalizeZoom(page.Zoom);
        return new TransformGroup
        {
            Children =
            {
                new RotateTransform { Angle = angle },
                new ScaleTransform { ScaleX = scale, ScaleY = scale },
            },
        };
    }

    /// <summary>
    /// Le zoom et le quart de tour de la sélection sont une transformation d'écran.
    /// Le bitmap n'est pas recalculé à chaque cran du curseur.
    /// </summary>
    private void PlaceZoom(DocumentPage page, double displayWidth, double displayHeight, PageFrame frame)
    {
        if (!ReferenceEquals(ZoomImage.Source, PageImage.Source))
        {
            ZoomImage.Source = PageImage.Source;
        }

        if (displayWidth < 1 || displayHeight < 1 || !frame.ChangesPixels(page.FrameTurns, page.Zoom))
        {
            ZoomHost.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        ZoomHost.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        ZoomHost.Width = displayWidth;
        ZoomHost.Height = displayHeight;
        var rect = new Rect(frame.X * displayWidth, frame.Y * displayHeight, frame.Width * displayWidth, frame.Height * displayHeight);
        ZoomHost.Clip = new RectangleGeometry { Rect = rect };
        var scale = frame.ContentScale(displayWidth, displayHeight, page.FrameTurns, page.Zoom);
        var anchor = page.ContentAnchor;
        ZoomFrame.RenderTransformOrigin = new Point(anchor.X, anchor.Y);
        ZoomFrame.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform { ScaleX = scale, ScaleY = scale },
                new RotateTransform { Angle = (page.FrameTurns & 3) * 90d },
            },
        };
    }

    private void Stage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var page = ViewModel.SelectedPage;
        if (page is null || ViewModel.IsBusy)
        {
            return;
        }

        var point = e.GetCurrentPoint(Stage).Position;
        if (e.GetCurrentPoint(Stage).Properties.IsMiddleButtonPressed)
        {
            return;
        }

        Stage.CapturePointer(e.Pointer);
        _mode = DragMode.Move;
        _viewX = point.X;
        _viewY = point.Y;
    }

    private void Stage_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_mode == DragMode.None || ViewModel.SelectedPage is null)
        {
            return;
        }

        var view = e.GetCurrentPoint(Stage).Position;
        if (e.GetCurrentPoint(Stage).Properties.IsLeftButtonPressed == false)
        {
            return;
        }

        var page = ViewModel.SelectedPage;
        var origin = Sheet.TransformToVisual(Stage).TransformPoint(new Point(0, 0));
        var far = Sheet.TransformToVisual(Stage).TransformPoint(new Point(Sheet.Width, Sheet.Height));
        var spanX = far.X - origin.X;
        var spanY = far.Y - origin.Y;
        if (Math.Abs(spanX) < 1 || Math.Abs(spanY) < 1)
        {
            return;
        }

        page.MoveImage((view.X - _viewX) / spanX, (view.Y - _viewY) / spanY);
        _viewX = view.X;
        _viewY = view.Y;
    }

    private void Stage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _mode = DragMode.None;
        Stage.ReleasePointerCapture(e.Pointer);
    }

    private async void Recognize_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanRecognizeText)
        {
            return;
        }

        if (!PageOcr.LanguageReady())
        {
            ViewModel.Warning = "Le français Tesseract est introuvable. Le PDF sera enregistré sans texte.";
            return;
        }

        var message = new TextBlock
        {
            Text = "Lecture en cours…",
            TextWrapping = TextWrapping.Wrap,
        };
        var panel = new StackPanel { Spacing = 12, MinWidth = 260 };
        panel.Children.Add(new ProgressRing
        {
            IsActive = true,
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        panel.Children.Add(message);

        var finished = false;
        var dialog = new ContentDialog
        {
            Title = "Reconnaissance du texte",
            Content = panel,
            XamlRoot = XamlRoot,
        };
        dialog.Closing += (_, args) =>
        {
            if (!finished)
            {
                args.Cancel = true;
            }
        };

        var shown = dialog.ShowAsync();
        try
        {
            var progress = new Progress<string>(text => message.Text = text);
            await ViewModel.RecognizeDocumentAsync(progress);
        }
        finally
        {
            finished = true;
            dialog.Hide();
        }

        try
        {
            await shown;
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Fermeture de la fenêtre de reconnaissance");
        }
    }

    private void DrawWords(DocumentPage page)
    {
        WordCanvas.Children.Clear();
        WordCanvas.RenderTransform = null;
        var changes = page.Frame.ChangesPixels(page.FrameTurns, page.Zoom);
        if (changes || !page.IsFullFrame || page.Words.Count == 0)
        {
            Log.Debug(
                "Surlignages masqués : modifié {Changes}, plein {Full}, mots {Words}",
                changes,
                page.IsFullFrame,
                page.Words.Count);
            return;
        }

        Log.Debug("Surlignages : {Count} mot(s) dessinés", page.Words.Count);

        var sheet = IsoA4.SheetPixels((page.QuarterTurns & 1) == 1, PagePreview.MaxEdge);
        var image = IsoA4.ImageDisplay(page.PixelWidth, page.PixelHeight, page.DpiX, page.DpiY, PagePreview.MaxEdge);
        var visualWidth = (page.QuarterTurns & 1) == 1 ? image.Height : image.Width;
        var visualHeight = (page.QuarterTurns & 1) == 1 ? image.Width : image.Height;
        var originX = (sheet.Width - visualWidth) / 2d;
        var originY = (sheet.Height - visualHeight) / 2d;
        var shift = ImageShift(page, sheet.Width, sheet.Height);
        WordCanvas.RenderTransform = new TranslateTransform { X = shift.X, Y = shift.Y };
        foreach (var word in page.Words)
        {
            var mark = new Rectangle
            {
                Width = Math.Max(1, word.Width * visualWidth),
                Height = Math.Max(1, word.Height * visualHeight),
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 255, 214, 80)),
            };
            Canvas.SetLeft(mark, originX + (word.X * visualWidth));
            Canvas.SetTop(mark, originY + (word.Y * visualHeight));
            WordCanvas.Children.Add(mark);
        }
    }
}
