using System.Text.Json;
using XtremScan.Services;

namespace XtremScan.Services;

public sealed class DraftPage
{
    public string File { get; set; } = "";

    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    public int DpiX { get; set; }

    public int DpiY { get; set; }

    public int CropX { get; set; }

    public int CropY { get; set; }

    public int CropWidth { get; set; }

    public int CropHeight { get; set; }

    public int QuarterTurns { get; set; }

    public double SkewDegrees { get; set; }

    public double FrameX { get; set; }

    public double FrameY { get; set; }

    public double FrameWidth { get; set; }

    public double FrameHeight { get; set; }

    public double Zoom { get; set; }

    public int FrameTurns { get; set; }

    public double? AnchorX { get; set; }

    public double? AnchorY { get; set; }

    public double ImageOffsetX { get; set; }

    public double ImageOffsetY { get; set; }
}

/// <summary>
/// Brouillon PDF sur le disque, jusqu'à Nouveau document.
/// </summary>
public static class DraftStore
{
    private static string FilePath => Path.Combine(AppPaths.Document, "session.json");

    public static IReadOnlyList<DraftPage> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                AppLog.For(nameof(DraftStore)).Debug("Brouillon : aucun fichier de session");
                return [];
            }

            var pages = JsonSerializer.Deserialize<List<DraftPage>>(File.ReadAllText(FilePath)) ?? [];
            var kept = pages.Where(page => !string.IsNullOrWhiteSpace(page.File) && File.Exists(Path.Combine(AppPaths.Document, page.File))).ToList();
            AppLog.For(nameof(DraftStore)).Information("Brouillon chargé : {Count} page(s) sur {Total}", kept.Count, pages.Count);
            return kept;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.For(nameof(DraftStore)).Warning(exception, "Brouillon illisible");
            return [];
        }
    }

    public static void Save(IReadOnlyList<DraftPage> pages)
    {
        Directory.CreateDirectory(AppPaths.Document);
        var json = JsonSerializer.Serialize(pages, new JsonSerializerOptions { WriteIndented = true });
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, FilePath, overwrite: true);
        AppLog.For(nameof(DraftStore)).Debug("Brouillon enregistré : {Count} page(s)", pages.Count);
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
                AppLog.For(nameof(DraftStore)).Information("Brouillon supprimé");
            }
        }
        catch (IOException)
        {
        }
    }
}
