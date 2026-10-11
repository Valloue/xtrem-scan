using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace XtremScan.Services;

/// <summary>
/// Retrouve l'IPv4 locale d'un scanner WSD à partir du nom affiché par WIA.
/// </summary>
public sealed class ScannerAddressResolver
{
    private static readonly DevPropKey FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    private static readonly DevPropKey LocationKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 15);

    private static readonly DevPropKey IpAddressKey = new(new Guid("656A3BB3-ECC0-43FD-8477-4AE0404A96CD"), 12297);

    public string? TryResolve(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }

        var token = MacToken(deviceName);
        foreach (var instanceId in EnumerateEnumerator("SWD"))
        {
            var friendly = ReadString(instanceId, FriendlyNameKey);
            if (!Matches(deviceName, token, instanceId, friendly))
            {
                continue;
            }

            var address = FirstLan(ReadString(instanceId, IpAddressKey))
                ?? HostFromLocation(ReadString(instanceId, LocationKey));
            if (address is not null)
            {
                return address;
            }
        }

        return null;
    }

    private static bool Matches(string deviceName, string? token, string instanceId, string? friendly)
    {
        if (!string.IsNullOrWhiteSpace(friendly)
            && string.Equals(friendly.Trim(), deviceName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return token is not null
            && instanceId.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static string? MacToken(string deviceName)
    {
        var open = deviceName.LastIndexOf('[');
        var close = deviceName.LastIndexOf(']');
        if (open < 0 || close <= open + 1)
        {
            return null;
        }

        var token = deviceName[(open + 1)..close].Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (token.Length != 12)
        {
            return null;
        }

        foreach (var character in token)
        {
            if (!Uri.IsHexDigit(character))
            {
                return null;
            }
        }

        return token;
    }

    private static string? FirstLan(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        foreach (var part in raw.Split('\0', ' ', ';', ','))
        {
            if (IPAddress.TryParse(part.Trim(), out var address) && IsLan(address))
            {
                return address.ToString();
            }
        }

        return null;
    }

    private static string? HostFromLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location) || !Uri.TryCreate(location.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return FirstLan(uri.Host);
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

    private static IEnumerable<string> EnumerateEnumerator(string enumerator)
    {
        var length = 0;
        if (CM_Get_Device_ID_List_Size(out length, enumerator, 1) != 0 || length <= 1)
        {
            yield break;
        }

        var buffer = new char[length];
        if (CM_Get_Device_ID_List(enumerator, buffer, length, 1) != 0)
        {
            yield break;
        }

        var text = new string(buffer);
        foreach (var id in text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return id;
        }
    }

    private static string? ReadString(string instanceId, DevPropKey key)
    {
        if (CM_Locate_DevNode(out var node, instanceId, 0) != 0)
        {
            return null;
        }

        var size = 0;
        var type = 0u;
        var probe = CM_Get_DevNode_Property(node, ref key, out type, IntPtr.Zero, ref size, 0);
        if ((probe != 0 && probe != 26) || size <= 0 || size > 4096)
        {
            return null;
        }

        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            var read = CM_Get_DevNode_Property(node, ref key, out type, pointer, ref size, 0);
            if (read != 0)
            {
                return null;
            }

            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, size);
            var value = Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public DevPropKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List_Size(out int length, string? filter, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List(string? filter, char[] buffer, int bufferLength, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNode(out uint deviceInstance, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Property(
        uint deviceInstance,
        ref DevPropKey propertyKey,
        out uint propertyType,
        IntPtr propertyBuffer,
        ref int propertyBufferSize,
        uint flags);
}
