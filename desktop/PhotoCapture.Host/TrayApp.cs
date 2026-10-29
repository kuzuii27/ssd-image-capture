using System.Diagnostics;
using Microsoft.Win32;
using PhotoCapture.Host.Services;

namespace PhotoCapture.Host;

/// <summary>Notification-area icon: the only visible "window" of the app.</summary>
public sealed class TrayApp : IDisposable
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "SSDPhotoCapture";

    readonly NotifyIcon icon;
    readonly ToolStripMenuItem autostart;

    public TrayApp(SettingsStore store, Action openAdmin, Action requestRestart)
    {
        autostart = new ToolStripMenuItem("Start with Windows") { Checked = IsAutostart(), CheckOnClick = true };
        autostart.CheckedChanged += (_, _) => SetAutostart(autostart.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open admin page", null, (_, _) => openAdmin());
        menu.Items.Add("Open data folder", null, (_, _) => Process.Start(new ProcessStartInfo(store.DataDir) { UseShellExecute = true }));
        menu.Items.Add("Change data folder…", null, (_, _) => ChangeFolder(store, requestRestart));
        menu.Items.Add(autostart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.ExitThread());

        icon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = "SSD Photo Capture",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) openAdmin(); };
    }

    public void Notify(string title, string text) => icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);

    static void ChangeFolder(SettingsStore store, Action requestRestart)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the folder that holds (or will hold) the photos and database.\nTo move to another PC, copy the folder there and choose it here.",
            UseDescriptionForTitle = true,
            SelectedPath = store.DataDir,
        };
        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;
        if (string.Equals(Path.GetFullPath(dialog.SelectedPath), store.DataDir, StringComparison.OrdinalIgnoreCase)) return;
        try { DataFolder.Remember(dialog.SelectedPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "SSD Photo Capture", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        requestRestart();
        Application.ExitThread();
    }

    static bool IsAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) is string;
    }

    static void SetAutostart(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(RunName, $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue(RunName, throwOnMissingValue: false);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
