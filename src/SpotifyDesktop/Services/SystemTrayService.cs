using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace SpotifyDesktop.Services;

public sealed class SystemTrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Action _showWindowAction;
    private readonly Func<string, Task> _executeMediaCommandAsync;
    private readonly Action _exitApplicationAction;
    private bool _disposed;

    public SystemTrayService(
        Action showWindowAction,
        Func<string, Task> executeMediaCommandAsync,
        Action exitApplicationAction)
    {
        _showWindowAction = showWindowAction;
        _executeMediaCommandAsync = executeMediaCommandAsync;
        _exitApplicationAction = exitApplicationAction;

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Spotify",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _showWindowAction();
            }
        };

        _notifyIcon.MouseDoubleClick += (_, _) => _showWindowAction();
    }

    public void UpdateTrackTooltip(string? trackTitle)
    {
        if (_disposed)
        {
            return;
        }

        string text = string.IsNullOrWhiteSpace(trackTitle) || trackTitle == "Spotify"
            ? "Spotify"
            : $"Spotify: {trackTitle}";

        // Win32 NotifyIcon.Text limit is 63 characters
        if (text.Length > 63)
        {
            text = string.Concat(text.AsSpan(0, 60), "...");
        }

        _notifyIcon.Text = text;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        ContextMenuStrip menu = new()
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkTrayColorTable()),
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.FromArgb(241, 241, 241),
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Regular)
        };

        ToolStripMenuItem openItem = new("Open Spotify")
        {
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold)
        };
        openItem.Click += (_, _) => _showWindowAction();

        ToolStripMenuItem playPauseItem = new("Play / Pause")
        {
            ForeColor = Color.FromArgb(241, 241, 241)
        };
        playPauseItem.Click += async (_, _) => await _executeMediaCommandAsync("playPause");

        ToolStripMenuItem nextItem = new("Next Track")
        {
            ForeColor = Color.FromArgb(241, 241, 241)
        };
        nextItem.Click += async (_, _) => await _executeMediaCommandAsync("next");

        ToolStripMenuItem prevItem = new("Previous Track")
        {
            ForeColor = Color.FromArgb(241, 241, 241)
        };
        prevItem.Click += async (_, _) => await _executeMediaCommandAsync("previous");

        ToolStripMenuItem exitItem = new("Exit")
        {
            ForeColor = Color.FromArgb(255, 110, 110)
        };
        exitItem.Click += (_, _) => _exitApplicationAction();

        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(playPauseItem);
        menu.Items.Add(nextItem);
        menu.Items.Add(prevItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            Uri iconUri = new("pack://application:,,,/Assets/spotify.ico", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(iconUri);
            if (streamInfo?.Stream is not null)
            {
                using Stream s = streamInfo.Stream;
                return new Icon(s);
            }
        }
        catch
        {
            // Fallback to executable icon if resource stream fails
        }

        string? exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
        {
            Icon? extracted = Icon.ExtractAssociatedIcon(exePath);
            if (extracted is not null)
            {
                return extracted;
            }
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private sealed class DarkTrayColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(24, 24, 24);
        public override Color MenuBorder => Color.FromArgb(48, 48, 48);
        public override Color MenuItemBorder => Color.FromArgb(65, 65, 65);
        public override Color MenuItemSelected => Color.FromArgb(50, 50, 50);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(50, 50, 50);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(50, 50, 50);
        public override Color SeparatorDark => Color.FromArgb(50, 50, 50);
        public override Color SeparatorLight => Color.FromArgb(50, 50, 50);
    }
}
