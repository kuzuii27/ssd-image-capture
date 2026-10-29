using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhotoCapture.Host.Services;

public sealed class AppSettings
{
    public int ApiPort { get; set; } = 5080;
    public int AdminPort { get; set; } = 5081;
    public long MaxUploadBytes { get; set; } = 15 * 1024 * 1024;
    /// <summary>Bearer token phones must send. Kept in the data folder so the admin page can show a setup QR code.</summary>
    public string Token { get; set; } = "";
    public DateTimeOffset TokenCreatedAt { get; set; }
    /// <summary>Optional, OFF by default: photos older than this many days (by capture date) are deleted automatically.</summary>
    public bool RetentionEnabled { get; set; } = false;
    public int RetentionDays { get; set; } = 30;
    /// <summary>PBKDF2 hash of the password required for remote (non-localhost) admin access. Empty = remote access stays blocked.</summary>
    public string AdminPasswordHash { get; set; } = "";
    public string AdminPasswordSalt { get; set; } = "";
}

/// <summary>Resolves the data folder and persists settings.json inside it.</summary>
public sealed class SettingsStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    readonly object gate = new();
    readonly byte[][] tokenBytes = [[]];

    public string DataDir { get; }
    public string PhotosDir => Path.Combine(DataDir, "photos");
    public string ThumbsDir => Path.Combine(DataDir, "thumbs");
    public string DbPath => Path.Combine(DataDir, "photos.db");
    public string SqlServerMdfPath => Path.Combine(DataDir, "SSD_mobileCamera.mdf");
    public string LogPath => Path.Combine(DataDir, "logs", "app.log");
    public AppSettings Current { get; private set; }

    string SettingsPath => Path.Combine(DataDir, "settings.json");

    public SettingsStore(string dataDir)
    {
        DataDir = Path.GetFullPath(dataDir);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(PhotosDir);
        Directory.CreateDirectory(ThumbsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        Current = Load();
        if (string.IsNullOrWhiteSpace(Current.Token)) Regenerate();
        else tokenBytes[0] = Encoding.UTF8.GetBytes(Current.Token);
    }

    AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // A damaged settings file must not stop the app; a new token is issued instead.
            File.Copy(SettingsPath, SettingsPath + ".broken", overwrite: true);
        }
        return new AppSettings();
    }

    void Save() => File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, Json));

    public string Regenerate()
    {
        lock (gate)
        {
            Current.Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            Current.TokenCreatedAt = DateTimeOffset.UtcNow;
            tokenBytes[0] = Encoding.UTF8.GetBytes(Current.Token);
            Save();
            return Current.Token;
        }
    }

    public void UpdateRetention(bool enabled, int days)
    {
        lock (gate)
        {
            Current.RetentionEnabled = enabled;
            Current.RetentionDays = days;
            Save();
        }
    }

    /// <summary>Sets or clears the password required for remote admin access. Empty password clears it (blocks remote access again).</summary>
    public void SetAdminPassword(string password)
    {
        lock (gate)
        {
            if (string.IsNullOrEmpty(password))
            {
                Current.AdminPasswordHash = "";
                Current.AdminPasswordSalt = "";
            }
            else
            {
                var salt = RandomNumberGenerator.GetBytes(16);
                var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
                Current.AdminPasswordSalt = Convert.ToBase64String(salt);
                Current.AdminPasswordHash = Convert.ToBase64String(hash);
            }
            Save();
        }
    }

    public bool HasAdminPassword => !string.IsNullOrEmpty(Current.AdminPasswordHash);

    public bool IsValidAdminPassword(string? supplied)
    {
        if (string.IsNullOrEmpty(supplied) || !HasAdminPassword) return false;
        var salt = Convert.FromBase64String(Current.AdminPasswordSalt);
        var expected = Convert.FromBase64String(Current.AdminPasswordHash);
        var given = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(supplied), salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(given, expected);
    }

    public bool IsValidToken(string? supplied)
    {
        if (string.IsNullOrEmpty(supplied)) return false;
        var expected = tokenBytes[0];
        var given = Encoding.UTF8.GetBytes(supplied);
        return given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected);
    }

    public void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}"); }
        catch { /* logging must never break the app */ }
    }
}

/// <summary>Where the data folder lives: --data arg, SSD_DATA env var, the installer's pointer file, then a default.</summary>
public static class DataFolder
{
    public static string PointerFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SSDPhotoCapture", "datafolder.txt");

    public static string Resolve(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--data") return args[i + 1];
        var env = Environment.GetEnvironmentVariable("SSD_DATA");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        try
        {
            if (File.Exists(PointerFile))
            {
                var saved = File.ReadAllText(PointerFile).Trim();
                if (saved.Length > 0) return saved;
            }
        }
        catch (IOException) { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SSDPhotoData");
    }

    public static void Remember(string folder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PointerFile)!);
        File.WriteAllText(PointerFile, folder);
    }
}
