using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using XtremScan.Scanning;

namespace XtremScan.ViewModels;

public partial class DocumentPage : ObservableObject
{
    public required string JpegPath { get; init; }

    public required int PixelWidth { get; init; }

    public required int PixelHeight { get; init; }

    public required int DpiX { get; init; }

    public required int DpiY { get; init; }

    public required ImageSource Image { get; init; }

    [ObservableProperty]
    private ImageSource? thumbnail;

    [ObservableProperty]
    private int quarterTurns;

    [ObservableProperty]
    private int cropX;

    [ObservableProperty]
    private int cropY;

    [ObservableProperty]
    private int cropWidth;

    [ObservableProperty]
    private int cropHeight;

    [ObservableProperty]
    private double skewDegrees;

    [ObservableProperty]
    private double frameX;

    [ObservableProperty]
    private double frameY;

    [ObservableProperty]
    private double frameWidth = 1;

    [ObservableProperty]
    private double frameHeight = 1;

    [ObservableProperty]
    private double zoom = 1;

    [ObservableProperty]
    private int frameTurns;

    /// <summary>Point de la feuille autour duquel le zoom et le quart de tour restent fixes quand on déplace la sélection.</summary>
    public double AnchorX { get; set; } = double.NaN;

    public double AnchorY { get; set; } = double.NaN;

    /// <summary>Déplacement de l'image sur la page, en fraction de feuille. Peut sortir de la page.</summary>
    public double ImageOffsetX { get; set; }

    public double ImageOffsetY { get; set; }

    public void MoveImage(double dx, double dy)
    {
        ImageOffsetX = Math.Clamp(ImageOffsetX + dx, -2, 2);
        ImageOffsetY = Math.Clamp(ImageOffsetY + dy, -2, 2);
        OnPropertyChanged(nameof(ImageOffsetX));
    }

    public (double X, double Y) ContentAnchor
    {
        get
        {
            if (double.IsNaN(AnchorX) || double.IsNaN(AnchorY))
            {
                return Frame.Center;
            }

            return (AnchorX, AnchorY);
        }
    }

    public void PinContentAnchor()
    {
        var center = Frame.Center;
        AnchorX = center.X;
        AnchorY = center.Y;
    }

    [ObservableProperty]
    private int wordCount;

    public IReadOnlyList<OcrWordBox> Words { get; private set; } = [];

    public IReadOnlyList<PdfTextRun> Runs { get; private set; } = [];

    public bool IsFullCrop => CropX == 0 && CropY == 0 && CropWidth == PixelWidth && CropHeight == PixelHeight;

    public PageFrame Frame => PageFrame.Normalize(FrameX, FrameY, FrameWidth, FrameHeight);

    public bool IsFullFrame => new PageFrame(FrameX, FrameY, FrameWidth, FrameHeight).IsFull;

    public void SetFrame(double x, double y, double width, double height)
    {
        var frame = PageFrame.Normalize(x, y, width, height);
#pragma warning disable MVVMTK0034
        frameX = frame.X;
        frameY = frame.Y;
        frameWidth = frame.Width;
        frameHeight = frame.Height;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(FrameX));
    }

    public void SetOcr(OcrPage? page)
    {
        Words = page?.Boxes ?? [];
        Runs = page?.Runs ?? [];
        WordCount = Words.Count;
    }
}