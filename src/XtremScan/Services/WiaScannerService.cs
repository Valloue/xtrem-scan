using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Extensions.Logging;
using XtremScan.Scanning;

namespace XtremScan.Services;

/// <summary>
/// Lecture et acquisition WIA 2.0. Les dpi viennent du pilote, jamais d'une liste figée.
/// </summary>
public sealed class WiaScannerService : IScannerService, IDisposable
{
    private const int DeviceTypeScanner = 1;
    private const int DipDevName = 7;
    private const int DpsOpticalX = 3090;
    private const int DpsOpticalY = 3091;
    private const int DpsHandlingCapabilities = 3086;
    private const int DpsHandlingSelect = 3088;
    private const int IpaDataType = 4103;
    private const int IpaDepth = 4104;
    private const int IpaFormat = 4106;
    private const int IpsXRes = 6147;
    private const int IpsYRes = 6148;
    private const int IpsXPos = 6149;
    private const int IpsYPos = 6150;
    private const int IpsXExtent = 6151;
    private const int IpsYExtent = 6152;

    private const int FlatbedFlag = 0x0002;
    private const int FeederFlag = 0x0001;
    private const int DuplexFlag = 0x0004;

    private const int RangeSubType = 1;
    private const int ListSubType = 2;

    private static readonly (string Id, string Label, string Extension)[] KnownFormats =
    [
        ("{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}", "PNG", ".png"),
        ("{B96B3CB1-0728-11D3-9D7B-0000F81EF32E}", "TIFF", ".tif"),
        ("{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}", "BMP", ".bmp"),
        ("{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}", "JPEG", ".jpg"),
        ("{B96B3CB0-0728-11D3-9D7B-0000F81EF32E}", "GIF", ".gif"),
    ];

    private readonly ComStaThread _sta = new("XtremScan.WIA");
    private readonly ILogger<WiaScannerService> _logger;

    public WiaScannerService(ILogger<WiaScannerService> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<ScannerDevice>> ListDevicesAsync()
        => _sta.InvokeAsync(ListDevices);

    public Task<IReadOnlyList<ScanSourceCapabilities>> ReadCapabilitiesAsync(string deviceId)
        => _sta.InvokeAsync(() => ReadCapabilities(deviceId));

    public Task<ScanResult> ScanAsync(ScanRequest request)
        => _sta.InvokeAsync(() => Scan(request));

    public void Dispose() => _sta.Dispose();

    private IReadOnlyList<ScannerDevice> ListDevices()
    {
        dynamic manager = CreateManager();
        dynamic infos = manager.DeviceInfos;
        var opened = new List<object>();
        try
        {
            var devices = new List<ScannerDevice>();
            var count = (int)infos.Count;
            for (var index = 1; index <= count; index++)
            {
                dynamic info = infos[index];
                opened.Add((object)info);
                if ((int)info.Type != DeviceTypeScanner)
                {
                    continue;
                }

                var id = (string)info.DeviceID;
                var name = ReadString((object)info.Properties, DipDevName) ?? id;
                devices.Add(new ScannerDevice { Id = id, Name = name });
            }

            _logger.LogInformation("Scanners WIA trouvés : {Count}", devices.Count);
            return devices;
        }
        finally
        {
            foreach (var info in opened)
            {
                ReleaseCom(info);
            }

            ReleaseCom((object)infos);
            ReleaseCom((object)manager);
        }
    }

    private IReadOnlyList<ScanSourceCapabilities> ReadCapabilities(string deviceId)
    {
        dynamic device = Connect(deviceId);
        dynamic? item = null;
        try
        {
        object deviceProperties = device.Properties;
        var handling = ReadInt(deviceProperties, DpsHandlingCapabilities) ?? 0;
        var opticalX = ReadInt(deviceProperties, DpsOpticalX);
        var opticalY = ReadInt(deviceProperties, DpsOpticalY);
        var sources = new List<(ScanSourceKind Kind, int Flag, string Label)>();
        if (handling == 0 || (handling & FlatbedFlag) != 0)
        {
            sources.Add((ScanSourceKind.Flatbed, FlatbedFlag, "Vitre"));
        }

        if ((handling & FeederFlag) != 0)
        {
            sources.Add((ScanSourceKind.Feeder, FeederFlag, "Chargeur"));
        }

        if ((handling & DuplexFlag) != 0 && (handling & FeederFlag) != 0)
        {
            sources.Add((ScanSourceKind.Duplex, DuplexFlag | FeederFlag, "Recto verso"));
        }

        var result = new List<ScanSourceCapabilities>();
        foreach (var source in sources)
        {
            TrySetInt(deviceProperties, DpsHandlingSelect, source.Flag);
            item = FirstItem(device);
            object itemProperties = item.Properties;
            var horizontal = ReadAxis(itemProperties, IpsXRes);
            var vertical = ReadAxis(itemProperties, IpsYRes);
            if (horizontal is null || vertical is null)
            {
                _logger.LogWarning("Source {Source} sans résolution lisible sur {Device}", source.Label, deviceId);
                continue;
            }

            result.Add(new ScanSourceCapabilities
            {
                Kind = source.Kind,
                Label = source.Label,
                Horizontal = horizontal,
                Vertical = vertical,
                OpticalX = opticalX,
                OpticalY = opticalY,
                ColorModes = ReadNamed(itemProperties, IpaDataType, ColorLabel),
                BitDepths = ReadNamed(itemProperties, IpaDepth, static depth => depth + " bits"),
                Formats = ReadFormats(itemProperties),
            });
            _logger.LogInformation(
                "Source {Source} : {MinX}-{MaxX} dpi horizontal, {MinY}-{MaxY} dpi vertical",
                source.Label,
                horizontal.Minimum,
                horizontal.Maximum,
                vertical.Minimum,
                vertical.Maximum);
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException("Ce pilote ne publie aucune résolution de scan.");
        }

        return result;
        }
        finally
        {
            ReleaseCom((object?)item);
            ReleaseCom((object?)device);
        }
    }

    private ScanResult Scan(ScanRequest request)
    {
        dynamic device = Connect(request.DeviceId);
        dynamic? item = null;
        try
        {
        var flag = request.Source switch
        {
            ScanSourceKind.Feeder => FeederFlag,
            ScanSourceKind.Duplex => DuplexFlag | FeederFlag,
            _ => FlatbedFlag,
        };
        object deviceProperties = device.Properties;
        TrySetInt(deviceProperties, DpsHandlingSelect, flag);
        item = FirstItem(device);
        object properties = item.Properties;

        SetRequired(properties, IpsXRes, request.DpiX);
        SetRequired(properties, IpsYRes, request.DpiY);
        if (request.ColorMode is int color)
        {
            TrySetInt(properties, IpaDataType, color);
        }

        if (request.BitDepth is int depth)
        {
            TrySetInt(properties, IpaDepth, depth);
        }

        // Pleine vitre / pleine page à la résolution demandée. L'étendue est en pixels et dépend du dpi.
        TrySetInt(properties, IpsXPos, ReadAxis(properties, IpsXPos)?.Minimum ?? 0);
        TrySetInt(properties, IpsYPos, ReadAxis(properties, IpsYPos)?.Minimum ?? 0);
        TrySetToMaximum(properties, IpsXExtent);
        TrySetToMaximum(properties, IpsYExtent);

        ScanFormat format = ResolveFormat(properties, request.FormatId);
        Directory.CreateDirectory(request.OutputDirectory);
        var path = Path.Combine(request.OutputDirectory, ScanFileNames.Build(request.Preview, format.Extension));

        _logger.LogInformation(
            "Acquisition {Mode} {DpiX}x{DpiY} vers {Extension}",
            request.Preview ? "aperçu" : "scan",
            request.DpiX,
            request.DpiY,
            format.Extension);

        request.Progress?.Report(12);
        dynamic image;
        try
        {
            image = string.IsNullOrEmpty(format.Id)
                ? item.Transfer()
                : item.Transfer(format.Id);
        }
        catch (COMException exception)
        {
            _logger.LogError(exception, "Transfert WIA refusé");
            throw new InvalidOperationException("Le pilote a refusé l'acquisition à cette résolution.", exception);
        }

        image.SaveFile(path);
        request.Progress?.Report(100);
        var fileDpi = ImageHeaderDpi.TryRead(path);
        _logger.LogInformation("Fichier écrit, dpi d'en-tête : {Dpi}", fileDpi);
        return new ScanResult
        {
            FilePath = path,
            RequestedDpiX = request.DpiX,
            RequestedDpiY = request.DpiY,
            FileDpi = fileDpi,
        };
        }
        finally
        {
            ReleaseCom((object?)item);
            ReleaseCom((object?)device);
        }
    }

    private static void ReleaseCom(object? com)
    {
        if (com is null)
        {
            return;
        }

        try
        {
            while (Marshal.ReleaseComObject(com) > 0)
            {
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidComObjectException)
        {
        }
    }

    private static dynamic CreateManager()
    {
        var type = Type.GetTypeFromProgID("WIA.DeviceManager")
            ?? throw new InvalidOperationException("Windows Image Acquisition n'est pas disponible sur ce PC.");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Impossible de démarrer WIA.");
    }

    private static dynamic Connect(string deviceId)
    {
        dynamic manager = CreateManager();
        dynamic infos = manager.DeviceInfos;
        var opened = new List<object>();
        try
        {
            var count = (int)infos.Count;
            for (var index = 1; index <= count; index++)
            {
                dynamic info = infos[index];
                opened.Add((object)info);
                if (!string.Equals((string)info.DeviceID, deviceId, StringComparison.Ordinal))
                {
                    continue;
                }

                return info.Connect();
            }

            throw new InvalidOperationException("Ce scanner n'est plus visible. Actualise la liste.");
        }
        finally
        {
            foreach (var info in opened)
            {
                ReleaseCom(info);
            }

            ReleaseCom((object)infos);
            ReleaseCom((object)manager);
        }
    }

    private static dynamic FirstItem(dynamic device)
    {
        dynamic items = device.Items;
        if ((int)items.Count < 1)
        {
            throw new InvalidOperationException("Ce périphérique n'a pas de source de scan.");
        }

        return items[1];
    }

    private static ResolutionAxis? ReadAxis(object propertyBag, int propertyId)
    {
        var property = FindProperty(propertyBag, propertyId);
        if (property is null)
        {
            return null;
        }

        var subType = (int)property.SubType;
        if (subType == ListSubType)
        {
            List<int> values = ReadVector((object)property.SubTypeValues);
            if (values.Count == 0)
            {
                return null;
            }

            values.Sort();
            return new ResolutionAxis
            {
                Minimum = values[0],
                Maximum = values[^1],
                Step = 1,
                Values = values,
            };
        }

        if (subType == RangeSubType)
        {
            var minimum = Convert.ToInt32(property.SubTypeMin, CultureInfo.InvariantCulture);
            var maximum = Convert.ToInt32(property.SubTypeMax, CultureInfo.InvariantCulture);
            var step = Convert.ToInt32(property.SubTypeStep, CultureInfo.InvariantCulture);
            if (step < 1)
            {
                step = 1;
            }

            return new ResolutionAxis
            {
                Minimum = minimum,
                Maximum = maximum,
                Step = step,
                Values = ResolutionMath.Expand(minimum, maximum, step),
            };
        }

        var current = Convert.ToInt32(property.Value, CultureInfo.InvariantCulture);
        return new ResolutionAxis
        {
            Minimum = current,
            Maximum = current,
            Step = 1,
            Values = [current],
        };
    }

    private static List<int> ReadVector(object vector)
    {
        var values = new List<int>();
        try
        {
            if (vector is System.Collections.IEnumerable enumerable and not string)
            {
                foreach (var item in enumerable)
                {
                    values.Add(Convert.ToInt32(item, CultureInfo.InvariantCulture));
                }
            }
        }
        catch (Exception exception) when (exception is RuntimeBinderException or InvalidCastException or COMException)
        {
            values.Clear();
        }

        if (values.Count > 0)
        {
            return values;
        }

        var count = Convert.ToInt32(vector.GetType().InvokeMember(
            "Count",
            System.Reflection.BindingFlags.GetProperty,
            null,
            vector,
            null), CultureInfo.InvariantCulture);
        for (var index = 1; index <= count; index++)
        {
            var raw = vector.GetType().InvokeMember(
                "Item",
                System.Reflection.BindingFlags.GetProperty,
                null,
                vector,
                [index]);
            values.Add(Convert.ToInt32(raw, CultureInfo.InvariantCulture));
        }

        return values;
    }

    private static IReadOnlyList<NamedValue> ReadNamed(object propertyBag, int propertyId, Func<int, string> label)
    {
        var axis = ReadAxis(propertyBag, propertyId);
        if (axis is null)
        {
            return [];
        }

        IReadOnlyList<int> values = axis.Values
            ?? ResolutionMath.Expand(axis.Minimum, axis.Maximum, axis.Step)
            ?? [axis.Minimum, axis.Maximum];
        return values.Distinct().Select(value => new NamedValue { Value = value, Label = label(value) }).ToList();
    }

    private static IReadOnlyList<ScanFormat> ReadFormats(object propertyBag)
    {
        var property = FindProperty(propertyBag, IpaFormat);
        var found = new List<ScanFormat>();
        if (property is not null && (int)property.SubType == ListSubType)
        {
            dynamic vector = property.SubTypeValues;
            var count = (int)vector.Count;
            for (var index = 1; index <= count; index++)
            {
                var raw = Convert.ToString(vector[index], CultureInfo.InvariantCulture);
                var format = MatchFormat(raw);
                if (format is not null && found.All(item => item.Id != format.Id))
                {
                    found.Add(format);
                }
            }
        }

        if (found.Count == 0)
        {
            found.Add(KnownFormats.Select(ToFormat).First(item => item.Label == "BMP"));
        }

        return found
            .OrderBy(item => item.Label == "PNG" ? 0 : item.Label == "TIFF" ? 1 : 2)
            .ToList();
    }

    private static ScanFormat ResolveFormat(object properties, string? requestedId)
    {
        var formats = ReadFormats(properties);
        var match = formats.FirstOrDefault(item => string.Equals(item.Id, requestedId, StringComparison.OrdinalIgnoreCase));
        return match ?? formats[0];
    }

    private static ScanFormat? MatchFormat(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var guid))
        {
            return null;
        }

        var canonical = guid.ToString("B").ToUpperInvariant();
        foreach (var known in KnownFormats)
        {
            if (string.Equals(known.Id, canonical, StringComparison.OrdinalIgnoreCase))
            {
                return ToFormat(known);
            }
        }

        return new ScanFormat { Id = canonical, Label = "Image", Extension = ".bin" };
    }

    private static ScanFormat ToFormat((string Id, string Label, string Extension) known)
        => new() { Id = known.Id, Label = known.Label, Extension = known.Extension };

    private static string ColorLabel(int value) => value switch
    {
        0 => "Noir et blanc",
        1 => "Tramé",
        2 => "Niveaux de gris",
        3 => "Couleur",
        4 => "Couleur (seuil)",
        5 => "Couleur (tramé)",
        6 => "RGB brut",
        7 => "BGR brut",
        8 => "Gris brut",
        _ => "Mode " + value,
    };

    private static void SetRequired(object propertyBag, int propertyId, int value)
    {
        var property = FindProperty(propertyBag, propertyId)
            ?? throw new InvalidOperationException("Le pilote n'expose pas la résolution demandée.");
        property.Value = value;
    }

    private static void TrySetInt(object propertyBag, int propertyId, int value)
    {
        var property = FindProperty(propertyBag, propertyId);
        if (property is null)
        {
            return;
        }

        try
        {
            property.Value = value;
        }
        catch (COMException)
        {
            // Propriété facultative : la vitre reste utilisable sans elle.
        }
    }

    private static void TrySetToMaximum(object propertyBag, int propertyId)
    {
        var axis = ReadAxis(propertyBag, propertyId);
        if (axis is null)
        {
            return;
        }

        TrySetInt(propertyBag, propertyId, axis.Maximum);
    }

    private static int? ReadInt(object propertyBag, int propertyId)
    {
        var property = FindProperty(propertyBag, propertyId);
        if (property is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(property.Value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is COMException or FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static string? ReadString(object propertyBag, int propertyId)
    {
        var property = FindProperty(propertyBag, propertyId);
        if (property is null)
        {
            return null;
        }

        var text = Convert.ToString(property.Value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static dynamic? FindProperty(object propertyBag, int propertyId)
    {
        dynamic properties = propertyBag;
        var count = (int)properties.Count;
        for (var index = 1; index <= count; index++)
        {
            dynamic property = properties[index];
            if ((int)property.PropertyID == propertyId)
            {
                return property;
            }
        }

        return null;
    }

}
