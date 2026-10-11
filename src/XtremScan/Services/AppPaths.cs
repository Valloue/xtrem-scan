namespace XtremScan.Services;

public static class AppPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XtremScan");

    public static string Logs => Path.Combine(Root, "logs");

    public static string Preview => Path.Combine(Root, "preview");

    public static string Document => Path.Combine(Root, "document");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Preview);
        Directory.CreateDirectory(Document);
    }
}
