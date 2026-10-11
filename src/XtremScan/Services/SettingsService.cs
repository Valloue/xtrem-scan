using System.Text.Json;

namespace XtremScan.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly object _gate = new();
    private readonly string _path;
    private SettingsFile _file;

    public SettingsService()
    {
        AppPaths.EnsureDirectories();
        _path = AppPaths.SettingsFile;
        AppLog.For(nameof(SettingsService)).Information("Réglages chargés depuis {Path}", _path);
        _file = Load();
    }

    public string? LastFormatId
    {
        get
        {
            lock (_gate)
            {
                return _file.LastFormatId;
            }
        }
        set
        {
            lock (_gate)
            {
                _file.LastFormatId = value;
                Save();
            }
        }
    }

    public int? LastDpiX
    {
        get
        {
            lock (_gate)
            {
                return _file.LastDpiX;
            }
        }
        set
        {
            lock (_gate)
            {
                _file.LastDpiX = value;
                Save();
            }
        }
    }

    public int? LastDpiY
    {
        get
        {
            lock (_gate)
            {
                return _file.LastDpiY;
            }
        }
        set
        {
            lock (_gate)
            {
                _file.LastDpiY = value;
                Save();
            }
        }
    }

    public int? LastColorMode
    {
        get
        {
            lock (_gate)
            {
                return _file.LastColorMode;
            }
        }
        set
        {
            lock (_gate)
            {
                _file.LastColorMode = value;
                Save();
            }
        }
    }

    public string? LastDeviceId
    {
        get
        {
            lock (_gate)
            {
                return _file.LastDeviceId;
            }
        }
        set
        {
            lock (_gate)
            {
                _file.LastDeviceId = value;
                Save();
            }
        }
    }

    private SettingsFile Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new SettingsFile();
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<SettingsFile>(json) ?? new SettingsFile();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new SettingsFile();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_file, new JsonSerializerOptions { WriteIndented = true });
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _path, overwrite: true);
    }

    private sealed class SettingsFile
    {
        public string? LastDeviceId { get; set; }

        public string? LastFormatId { get; set; }

        public int? LastDpiX { get; set; }

        public int? LastDpiY { get; set; }

        public int? LastColorMode { get; set; }
    }
}
