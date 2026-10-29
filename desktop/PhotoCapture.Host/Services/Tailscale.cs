using System.Diagnostics;
using System.Text.Json;

namespace PhotoCapture.Host.Services;

public sealed record TailscaleInfo(bool Installed, bool Running, string? DnsName, bool ServeConfigured, bool AdminServeConfigured, string? Message);

/// <summary>Thin wrapper over the Tailscale CLI so the admin page can show and fix the phone link.</summary>
public static class Tailscale
{
    static string? FindExe()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tailscale", "tailscale.exe"),
        };
        foreach (var c in candidates) if (File.Exists(c)) return c;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { var p = Path.Combine(dir.Trim(), "tailscale.exe"); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    static (int Code, string Output) Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Tailscale.");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(10_000))
        {
            try { p.Kill(true); } catch { }
            return (-1, "Tailscale did not respond in time.");
        }
        return (p.ExitCode, (stdout.Result + stderr.Result).Trim());
    }

    static readonly object CacheGate = new();
    static (TailscaleInfo Info, DateTime At, int Port)? cache;

    /// <summary>The CLI takes a few seconds per call, so results are reused for a short time.</summary>
    public static TailscaleInfo Status(int apiPort, int adminPort)
    {
        lock (CacheGate)
        {
            if (cache is { } c && c.Port == apiPort && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(20)) return c.Info;
            var info = Query(apiPort, adminPort);
            cache = (info, DateTime.UtcNow, apiPort);
            return info;
        }
    }

    static TailscaleInfo Query(int apiPort, int adminPort)
    {
        var exe = FindExe();
        if (exe is null) return new(false, false, null, false, false, "Tailscale is not installed on this PC.");
        try
        {
            var serveTask = Task.Run(() => Run(exe, "serve status"));
            var (code, output) = Run(exe, "status --json");
            if (code != 0) return new(true, false, null, false, false, "Tailscale is installed but not signed in or not running.");
            using var doc = JsonDocument.Parse(output);
            var state = doc.RootElement.TryGetProperty("BackendState", out var s) ? s.GetString() : null;
            string? dns = null;
            if (doc.RootElement.TryGetProperty("Self", out var self) && self.TryGetProperty("DNSName", out var d))
                dns = d.GetString()?.TrimEnd('.');
            var running = state == "Running";
            var serve = false;
            var adminServe = false;
            if (running)
            {
                var (_, serveOut) = serveTask.GetAwaiter().GetResult();
                serve = serveOut.Contains($"127.0.0.1:{apiPort}", StringComparison.Ordinal)
                        || serveOut.Contains($"localhost:{apiPort}", StringComparison.Ordinal);
                adminServe = serveOut.Contains($"127.0.0.1:{adminPort}", StringComparison.Ordinal)
                        || serveOut.Contains($"localhost:{adminPort}", StringComparison.Ordinal);
            }
            return new(true, running, dns, serve, adminServe, running ? null : $"Tailscale state: {state}.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new(true, false, null, false, false, ex.Message);
        }
    }

    /// <summary>Publishes only the phone API port inside the tailnet (HTTPS, trusted certificate).</summary>
    public static (bool Ok, string Message) EnableServe(int apiPort)
    {
        var exe = FindExe();
        if (exe is null) return (false, "Tailscale is not installed on this PC.");
        var (code, output) = Run(exe, $"serve --bg {apiPort}");
        lock (CacheGate) cache = null;
        return (code == 0, output.Length > 0 ? output : "Done.");
    }

    /// <summary>Publishes the admin port on a separate HTTPS port (8443) inside the tailnet, for remote viewing. Requires a password to be set (enforced by the caller).</summary>
    public static (bool Ok, string Message) EnableAdminServe(int adminPort)
    {
        var exe = FindExe();
        if (exe is null) return (false, "Tailscale is not installed on this PC.");
        var (code, output) = Run(exe, $"serve --bg --https=8443 {adminPort}");
        lock (CacheGate) cache = null;
        return (code == 0, output.Length > 0 ? output : "Done.");
    }
}
