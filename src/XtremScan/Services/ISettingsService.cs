namespace XtremScan.Services;

public interface ISettingsService
{
    string? LastDeviceId { get; set; }

    string? LastFormatId { get; set; }

    int? LastDpiX { get; set; }

    int? LastDpiY { get; set; }

    int? LastColorMode { get; set; }
}
