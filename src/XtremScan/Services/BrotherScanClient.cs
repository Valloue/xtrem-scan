using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using XtremScan.Scanning;

namespace XtremScan.Services;

/// <summary>
/// Acquisition JPEG via le port 54921. Le flux est écrit sur le disque, pas gardé en mémoire.
/// </summary>
public sealed class BrotherScanClient
{
    private const int MaxPageBytes = 768 * 1024 * 1024;

    private readonly ILogger<BrotherScanClient> _logger;

    public BrotherScanClient(ILogger<BrotherScanClient> logger)
    {
        _logger = logger;
    }

    public async Task<BrotherProfile> ProbeAsync(string host)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ProbeCoreAsync(host);
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("occupé", StringComparison.Ordinal) && attempt < 4)
            {
                _logger.LogInformation("Scanner Brother occupé, nouvel essai {Attempt}", attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt));
            }
        }
    }

    private async Task<BrotherProfile> ProbeCoreAsync(string host)
    {
        using var client = await ConnectAsync(host, CancellationToken.None);
        var stream = client.GetStream();
        await ReadBannerAsync(stream, CancellationToken.None);
        var floor = await LeaseOnStreamAsync(
            stream,
            BrotherScanProtocol.FloorProbeDpi,
            BrotherScanProtocol.FloorProbeDpi,
            "GRAY64",
            CancellationToken.None);
        var gray = await LeaseOnStreamAsync(
            stream,
            BrotherScanProtocol.CeilingProbeDpi,
            BrotherScanProtocol.CeilingProbeDpi,
            "GRAY64",
            CancellationToken.None);
        var color = await LeaseOnStreamAsync(
            stream,
            BrotherScanProtocol.CeilingProbeDpi,
            BrotherScanProtocol.CeilingProbeDpi,
            "CGRAY",
            CancellationToken.None);
        _logger.LogInformation(
            "Brother {Host} : gris {GrayX}x{GrayY} dpi, couleur {ColorX}x{ColorY} dpi",
            host,
            gray.DpiX,
            gray.DpiY,
            color.DpiX,
            color.DpiY);
        return new BrotherProfile
        {
            Host = host,
            MinX = floor.DpiX,
            MinY = floor.DpiY,
            MaxX = color.DpiX,
            MaxY = color.DpiY,
            GrayMaxX = gray.DpiX,
            GrayMaxY = gray.DpiY,
        };
    }

    public async Task<ScanResult> ScanAsync(string host, ScanRequest request)
    {
        var mode = BrotherScanProtocol.ColorMode(request.ColorMode);
        Directory.CreateDirectory(request.OutputDirectory);
        var path = Path.Combine(request.OutputDirectory, ScanFileNames.Build(request.Preview, ".jpg"));
        using var overall = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            using var client = await ConnectAsync(host, overall.Token);
            var stream = client.GetStream();
            await ReadBannerAsync(stream, overall.Token);
            request.Progress?.Report(8);
            var lease = await LeaseOnStreamAsync(stream, request.DpiX, request.DpiY, mode, overall.Token);
            _logger.LogInformation(
                "Acquisition Brother {Host} demandée {AskX}x{AskY}, exécutée {DpiX}x{DpiY}, zone {Width}x{Height} px",
                host,
                request.DpiX,
                request.DpiY,
                lease.DpiX,
                lease.DpiY,
                lease.WidthPx,
                lease.HeightPx);

            var command = BrotherScanProtocol.CreateCommand(
                "X",
                ("R", lease.DpiX + "," + lease.DpiY),
                ("M", mode),
                ("C", "JPEG"),
                ("J", "MID"),
                ("B", "50"),
                ("N", "50"),
                ("A", "0,0," + lease.WidthPx + "," + lease.HeightPx));
            await stream.WriteAsync(command, overall.Token);

            await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 65536, useAsync: true))
            {
                await ReadPageAsync(stream, file, lease.WidthPx, lease.HeightPx, request.Progress, overall.Token);
            }

            request.Progress?.Report(100);

            if (!IsJpeg(path))
            {
                throw new InvalidOperationException("Le scanner n'a pas renvoyé une image JPEG.");
            }

            var fileDpi = ImageHeaderDpi.TryRead(path);
            var exported = await ExportIfNeededAsync(path, request, fileDpi);
            _logger.LogInformation("Fichier Brother écrit, dpi d'en-tête : {Dpi}", fileDpi);
            return exported;
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            DeleteQuietly(path);
            _logger.LogError(exception, "Acquisition Brother interrompue");
            throw new InvalidOperationException("L'acquisition réseau a été interrompue.", exception);
        }
        catch
        {
            DeleteQuietly(path);
            throw;
        }
    }

    private static async Task<ScanResult> ExportIfNeededAsync(string jpegPath, ScanRequest request, int? fileDpi)
    {
        if (request.Preview || !ScanFormats.IsDerived(request.FormatId))
        {
            return new ScanResult
            {
                FilePath = jpegPath,
                RequestedDpiX = request.DpiX,
                RequestedDpiY = request.DpiY,
                FileDpi = fileDpi,
                DisplayPath = jpegPath,
            };
        }

        var format = ScanFormats.Brother.First(item => item.Id == request.FormatId);
        var exported = Path.Combine(request.OutputDirectory, ScanFileNames.Build(false, format.Extension));
        try
        {
            await ScanExporter.ExportAsync(jpegPath, exported, format.Id, request.DpiX, request.DpiY);
            Directory.CreateDirectory(AppPaths.Preview);
            var display = Path.Combine(AppPaths.Preview, Path.GetFileName(jpegPath));
            File.Move(jpegPath, display, overwrite: true);
            return new ScanResult
            {
                FilePath = exported,
                RequestedDpiX = request.DpiX,
                RequestedDpiY = request.DpiY,
                FileDpi = fileDpi,
                DisplayPath = display,
            };
        }
        catch
        {
            DeleteQuietly(exported);
            throw;
        }
    }

    private static async Task<BrotherLease> LeaseOnStreamAsync(
        NetworkStream stream,
        int dpiX,
        int dpiY,
        string mode,
        CancellationToken cancellationToken)
    {
        var command = BrotherScanProtocol.CreateCommand(
            "I",
            ("R", dpiX + "," + dpiY),
            ("M", mode));
        await stream.WriteAsync(command, cancellationToken);
        var buffer = new byte[512];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var read = await stream.ReadAsync(buffer, linked.Token);
        if (!BrotherScanProtocol.TryParseLease(buffer.AsSpan(0, read), out var lease) || lease is null)
        {
            throw new InvalidOperationException("Le scanner n'a pas indiqué de résolution utilisable.");
        }

        return lease;
    }

    private static async Task<TcpClient> ConnectAsync(string host, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(host, out var address) || !IsLan(address))
        {
            throw new InvalidOperationException("Adresse du scanner refusée.");
        }

        var client = new TcpClient();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await client.ConnectAsync(address, BrotherScanProtocol.Port, linked.Token);
            client.NoDelay = true;
            client.ReceiveTimeout = 120000;
            client.SendTimeout = 8000;
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task ReadBannerAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[128];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), linked.Token);
        var banner = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
        var hex = Convert.ToHexString(buffer, 0, Math.Min(read, 32));
        _logger.LogInformation("Bannière Brother ({Count} octets) : {Hex}", read, hex);
        if (banner.Contains("-NG 401", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Le scanner est occupé.");
        }

        if (!banner.Contains("+OK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Le scanner n'a pas répondu.");
        }
    }

    private static async Task ReadPageAsync(
        NetworkStream stream,
        Stream file,
        int width,
        int height,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var header = new byte[2];
        var rest = new byte[10];
        long total = 0;
        var shown = 0;
        while (true)
        {
            await stream.ReadExactlyAsync(header, cancellationToken);
            var type = header[0];
            if (type is 0x82 or 0x80)
            {
                return;
            }

            await stream.ReadExactlyAsync(rest, cancellationToken);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(rest.AsSpan(8));
            if (length == 0)
            {
                continue;
            }

            total += length;
            if (total > MaxPageBytes)
            {
                throw new InvalidOperationException("La page dépasse la taille admise.");
            }

            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload, cancellationToken);
            await file.WriteAsync(payload, cancellationToken);
            var percent = ScanThroughput.Percent(total, width, height);
            if (percent != shown)
            {
                shown = percent;
                progress?.Report(percent);
            }
        }
    }

    private static bool IsJpeg(string path)
    {
        Span<byte> signature = stackalloc byte[2];
        using var stream = File.OpenRead(path);
        return stream.Read(signature) == 2 && signature[0] == 0xFF && signature[1] == 0xD8;
    }

    private static bool IsLan(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (bytes[0] == 10)
        {
            return true;
        }

        if (bytes[0] == 192 && bytes[1] == 168)
        {
            return true;
        }

        return bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    private static void DeleteQuietly(string path)
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
}
