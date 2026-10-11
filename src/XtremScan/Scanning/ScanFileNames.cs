using System.Globalization;

namespace XtremScan.Scanning;

public static class ScanFileNames
{
    public static string Build(bool preview, string extension)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture);
        var prefix = preview ? "apercu" : "scan";
        return prefix + "-" + stamp + extension;
    }
}
