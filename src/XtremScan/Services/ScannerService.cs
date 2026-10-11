using Microsoft.Extensions.Logging;
using XtremScan.Scanning;

namespace XtremScan.Services;

/// <summary>
/// WIA d'abord. Si le pilote plafonne et que le port Brother va plus haut, c'est ce port qui scanne.
/// </summary>
public sealed class ScannerService : IScannerService
{
    private readonly WiaScannerService _wia;
    private readonly BrotherScanClient _brother;
    private readonly ScannerAddressResolver _addresses;
    private readonly ILogger<ScannerService> _logger;
    private readonly Dictionary<string, string> _hosts = new(StringComparer.Ordinal);
    private IReadOnlyList<ScannerDevice> _devices = [];

    public ScannerService(
        WiaScannerService wia,
        BrotherScanClient brother,
        ScannerAddressResolver addresses,
        ILogger<ScannerService> logger)
    {
        _wia = wia;
        _brother = brother;
        _addresses = addresses;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScannerDevice>> ListDevicesAsync()
    {
        _devices = await _wia.ListDevicesAsync();
        return _devices;
    }

    public async Task<IReadOnlyList<ScanSourceCapabilities>> ReadCapabilitiesAsync(string deviceId)
    {
        var host = ResolveHost(deviceId);
        BrotherProfile? profile = null;
        if (host is not null)
        {
            try
            {
                profile = await _brother.ProbeAsync(host);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Le port Brother de {Host} n'a pas répondu, WIA est conservé", host);
            }
        }
        else
        {
            _logger.LogInformation("Pas d'adresse réseau pour {Device}", deviceId);
        }

        var wia = await _wia.ReadCapabilitiesAsync(deviceId);
        if (profile is null)
        {
            _hosts.Remove(deviceId);
            return WithPdf(wia);
        }

        var wiaX = wia.Count == 0 ? 0 : wia.Max(source => source.Horizontal.Maximum);
        var wiaY = wia.Count == 0 ? 0 : wia.Max(source => source.Vertical.Maximum);
        var higher = (profile.MaxX > wiaX || profile.MaxY > wiaY)
            && profile.MaxX >= wiaX
            && profile.MaxY >= wiaY;
        if (!higher)
        {
            _hosts.Remove(deviceId);
            return WithPdf(wia);
        }

        _hosts[deviceId] = profile.Host;
        _logger.LogInformation(
            "Scanner {Device} : WIA {WiaX}x{WiaY} dpi, port Brother {MaxX}x{MaxY} dpi",
            deviceId,
            wiaX,
            wiaY,
            profile.MaxX,
            profile.MaxY);
        return [profile.ToSource()];
    }

    public async Task<ScanResult> ScanAsync(ScanRequest request)
    {
        if (_hosts.TryGetValue(request.DeviceId, out var host))
        {
            return await _brother.ScanAsync(host, request);
        }

        if (!request.Preview && request.FormatId == ScanFormats.Pdf)
        {
            var scanned = await _wia.ScanAsync(new ScanRequest
            {
                DeviceId = request.DeviceId,
                Source = request.Source,
                DpiX = request.DpiX,
                DpiY = request.DpiY,
                ColorMode = request.ColorMode,
                BitDepth = request.BitDepth,
                FormatId = ScanFormats.WiaJpeg,
                OutputDirectory = request.OutputDirectory,
                Preview = false,
            });
            var pdfPath = Path.Combine(request.OutputDirectory, ScanFileNames.Build(false, ".pdf"));
            try
            {
                PdfJpegWriter.Write(scanned.FilePath, pdfPath, request.DpiX, request.DpiY);
            }
            catch
            {
                TryDelete(pdfPath);
                throw;
            }

            Directory.CreateDirectory(AppPaths.Preview);
            var display = Path.Combine(AppPaths.Preview, Path.GetFileName(scanned.FilePath));
            File.Move(scanned.FilePath, display, overwrite: true);
            return new ScanResult
            {
                FilePath = pdfPath,
                RequestedDpiX = scanned.RequestedDpiX,
                RequestedDpiY = scanned.RequestedDpiY,
                FileDpi = scanned.FileDpi,
                DisplayPath = display,
            };
        }

        return await _wia.ScanAsync(request);
    }

    private static IReadOnlyList<ScanSourceCapabilities> WithPdf(IReadOnlyList<ScanSourceCapabilities> sources)
    {
        return sources.Select(source =>
        {
            if (source.Formats.Any(format => format.Id == ScanFormats.Pdf))
            {
                return source;
            }

            var formats = source.Formats.ToList();
            formats.Add(ScanFormats.PdfFormat);
            return new ScanSourceCapabilities
            {
                Kind = source.Kind,
                Label = source.Label,
                Horizontal = source.Horizontal,
                Vertical = source.Vertical,
                OpticalX = source.OpticalX,
                OpticalY = source.OpticalY,
                GrayscaleHorizontal = source.GrayscaleHorizontal,
                GrayscaleVertical = source.GrayscaleVertical,
                ColorModes = source.ColorModes,
                BitDepths = source.BitDepths,
                Formats = formats,
            };
        }).ToList();
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

    private string? ResolveHost(string deviceId)
    {
        var name = _devices.FirstOrDefault(device => device.Id == deviceId)?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var host = _addresses.TryResolve(name);
        if (host is null)
        {
            _logger.LogInformation("Pas d'adresse réseau pour {Name}", name);
        }

        return host;
    }
}
