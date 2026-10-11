using XtremScan.Scanning;

namespace XtremScan.Services;

public interface IScannerService
{
    Task<IReadOnlyList<ScannerDevice>> ListDevicesAsync();

    Task<IReadOnlyList<ScanSourceCapabilities>> ReadCapabilitiesAsync(string deviceId);

    Task<ScanResult> ScanAsync(ScanRequest request);
}
