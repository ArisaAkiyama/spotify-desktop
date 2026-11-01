using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using SpotifyDesktop.Interop;
using SpotifyDesktop.Models;
using SpotifyDesktop.Services;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;

namespace SpotifyDesktop;

public partial class MainWindow : Window
{
    private const string SpotifyUrl = "https://open.spotify.com";
    private const string MaximizeGeometryData = "M 0.5,0.5 L 9.5,0.5 L 9.5,9.5 L 0.5,9.5 Z";
    private const string RestoreGeometryData = "M 2.5,0.5 L 9.5,0.5 L 9.5,7.5 L 7.5,7.5 M 0.5,2.5 L 7.5,2.5 L 7.5,9.5 L 0.5,9.5 Z";

    // Monochromatic Black-and-White Design Brushes (Zero AI Slop)
    private static readonly Brush SwitchOnTrackBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly Brush SwitchOnThumbBrush = new SolidColorBrush(Color.FromRgb(18, 18, 21));
    private static readonly Brush SwitchOffTrackBrush = new SolidColorBrush(Color.FromRgb(42, 42, 52));
    private static readonly Brush SwitchOffThumbBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));

    private static readonly Brush NavActiveBgBrush = new SolidColorBrush(Color.FromRgb(37, 37, 44));
    private static readonly Brush NavActiveFgBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly Brush NavInactiveFgBrush = new SolidColorBrush(Color.FromRgb(148, 148, 160));

    private static readonly Brush CardSelectedBgBrush = new SolidColorBrush(Color.FromRgb(32, 32, 38));
    private static readonly Brush CardSelectedBorderBrush = new SolidColorBrush(Color.FromRgb(72, 72, 84));
    private static readonly Brush CardUnselectedBgBrush = new SolidColorBrush(Color.FromRgb(22, 22, 27));
    private static readonly Brush CardUnselectedBorderBrush = new SolidColorBrush(Color.FromRgb(34, 34, 39));

    private static readonly Brush PillSelectedBgBrush = new SolidColorBrush(Color.FromRgb(42, 42, 50));
    private static readonly Brush PillSelectedBorderBrush = new SolidColorBrush(Color.FromRgb(74, 74, 86));
    private static readonly Brush PillSelectedFgBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly Brush PillUnselectedBgBrush = new SolidColorBrush(Color.FromRgb(22, 22, 27));
    private static readonly Brush PillUnselectedBorderBrush = new SolidColorBrush(Color.FromRgb(34, 34, 39));
    private static readonly Brush PillUnselectedFgBrush = new SolidColorBrush(Color.FromRgb(142, 142, 152));

    private static readonly Brush IconActiveBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly Brush IconInactiveBrush = new SolidColorBrush(Color.FromRgb(130, 130, 140));

    private readonly AdBlockService _adBlockService = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly SystemTrayService _systemTrayService;
    private readonly DynamicIslandWindow _dynamicIsland;
    private readonly DispatcherTimer _settingsSaveDebounceTimer;
    private string _currentDocumentTitle = "Spotify";
    private WindowState _lastWindowState = WindowState.Maximized;
    private bool _isSettingsOpen;
    private bool _isSyncingSettingsUi;
    private bool _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();
        AdaptWindowToMonitorWorkArea();

        _systemTrayService = new SystemTrayService(
            showWindowAction: () => Dispatcher.Invoke(RestoreFromTray),
            executeMediaCommandAsync: ExecutePlayerCommandAsync,
            exitApplicationAction: () => Dispatcher.Invoke(ExitApplication));

        _dynamicIsland = new DynamicIslandWindow(
            restoreMainWindowAction: () => Dispatcher.Invoke(RestoreFromTray),
            executeMediaCommandAsync: ExecutePlayerCommandAsync);

        _settingsSaveDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _settingsSaveDebounceTimer.Tick += (_, _) =>
        {
            _settingsSaveDebounceTimer.Stop();
            _settingsService.Save(_settingsService.Current);
        };

        string appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        AboutVersionText.Text = $"v{appVersion}";

        SyncSettingsUiFromModel(_settingsService.Current);
        UpdateHeaderIndicators(_settingsService.Current);
        UpdateTitleBarStateVisuals();
        _lastWindowState = WindowState;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        StateChanged += OnWindowStateChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>
    /// Dynamically calculates window dimensions and position based on the monitor's usable WorkArea
    /// so the window never overflows the screen or hides the titlebar/taskbar.
    /// </summary>
    private void AdaptWindowToMonitorWorkArea()
    {
        Rect workArea = SystemParameters.WorkArea;

        double targetWidth = Math.Round(Math.Clamp(workArea.Width * 0.85, MinWidth, workArea.Width - 20));
        double targetHeight = Math.Round(Math.Clamp(workArea.Height * 0.85, MinHeight, workArea.Height - 20));

        Width = targetWidth;
        Height = targetHeight;

        Left = Math.Max(workArea.Left, workArea.Left + Math.Round((workArea.Width - targetWidth) / 2.0));
        Top = Math.Max(workArea.Top, workArea.Top + Math.Round((workArea.Height - targetHeight) / 2.0));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AdaptWindowToMonitorWorkArea();
            _dynamicIsland.PositionAtTopCenter();
        });
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        NativeMethods.ApplyDarkTitleBar(this);
        UpdateTitleBarStateVisuals();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        UpdateTitleBarStateVisuals();

        if (WindowState != WindowState.Minimized)
        {
            _lastWindowState = WindowState;
        }

        if (WindowState == WindowState.Minimized)
        {
            if (_settingsService.Current.DynamicIslandEnabled)
            {
                _dynamicIsland.ShowIsland();
            }
            else
            {
                _dynamicIsland.HideIsland();
            }
        }
        else
        {
            _dynamicIsland.HideIsland();
        }
    }

    private void UpdateTitleBarStateVisuals()
    {
        if (WindowState == WindowState.Maximized)
        {
            RootLayoutGrid.Margin = new Thickness(7);
            MaxRestoreIconPath.Data = Geometry.Parse(RestoreGeometryData);
        }
        else
        {
            RootLayoutGrid.Margin = new Thickness(0);
            MaxRestoreIconPath.Data = Geometry.Parse(MaximizeGeometryData);
        }
    }

    private void UpdateHeaderIndicators(AppSettings settings)
    {
        DolbyHeaderIndicatorDot.Visibility = settings.DolbyEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // =========================================================================
    // IN-APP FULL-SCREEN MONOCHROMATIC SETTINGS LOGIC
    // =========================================================================
    public void OpenSettings()
    {
        _isSettingsOpen = true;
        SyncSettingsUiFromModel(_settingsService.Current);
        MusicWebView.Visibility = Visibility.Hidden;
        SettingsOverlayGrid.Visibility = Visibility.Visible;
        Title = "Settings • Spotify";
        SwitchSettingsTab(0);
        SettingsOverlayGrid.Focus();
    }

    public void CloseSettings()
    {
        FlushPendingSettingsSave();
        _isSettingsOpen = false;
        SettingsOverlayGrid.Visibility = Visibility.Collapsed;
        MusicWebView.Visibility = Visibility.Visible;
        Title = _currentDocumentTitle;
        MusicWebView.Focus();
    }

    public void ToggleSettings()
    {
        if (_isSettingsOpen)
        {
            CloseSettings();
        }
        else
        {
            OpenSettings();
        }
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isSettingsOpen)
        {
            e.Handled = true;
            CloseSettings();
        }
    }

    private void OnSettingsButtonClick(object sender, RoutedEventArgs e)
    {
        ToggleSettings();
    }

    private void OnCloseSettingsButtonClick(object sender, RoutedEventArgs e)
    {
        CloseSettings();
    }

    private void OnNavDolbyClick(object sender, RoutedEventArgs e) => SwitchSettingsTab(0);
    private void OnNavIslandClick(object sender, RoutedEventArgs e) => SwitchSettingsTab(1);
    private void OnNavAboutClick(object sender, RoutedEventArgs e) => SwitchSettingsTab(2);

    private void SwitchSettingsTab(int index)
    {
        SettingsPanelDolby.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanelIsland.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanelAbout.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

        NavBtnDolby.Background = index == 0 ? NavActiveBgBrush : Brushes.Transparent;
        NavTxtDolby.Foreground = index == 0 ? NavActiveFgBrush : NavInactiveFgBrush;

        NavBtnIsland.Background = index == 1 ? NavActiveBgBrush : Brushes.Transparent;
        NavTxtIsland.Foreground = index == 1 ? NavActiveFgBrush : NavInactiveFgBrush;

        NavBtnAbout.Background = index == 2 ? NavActiveBgBrush : Brushes.Transparent;
        NavTxtAbout.Foreground = index == 2 ? NavActiveFgBrush : NavInactiveFgBrush;
    }

    public void SyncSettingsUiFromModel(AppSettings settings)
    {
        _isSyncingSettingsUi = true;
        try
        {
            // 1. Dolby Master Toggle (Monochromatic: White/Black vs DarkGray/White)
            DolbyToggleTrack.Background = settings.DolbyEnabled ? SwitchOnTrackBrush : SwitchOffTrackBrush;
            DolbyToggleThumb.Fill = settings.DolbyEnabled ? SwitchOnThumbBrush : SwitchOffThumbBrush;
            DolbyToggleThumb.HorizontalAlignment = settings.DolbyEnabled
                ? System.Windows.HorizontalAlignment.Right
                : System.Windows.HorizontalAlignment.Left;

            // 2. Output Device Cards (Headphones vs Laptop Speakers)
            bool isLaptop = string.Equals(settings.DolbyDeviceMode, "LaptopSpeakers", StringComparison.OrdinalIgnoreCase);
            CardHeadphones.Background = isLaptop ? CardUnselectedBgBrush : CardSelectedBgBrush;
            CardHeadphones.BorderBrush = isLaptop ? CardUnselectedBorderBrush : CardSelectedBorderBrush;
            HeadphoneIconPath.Stroke = isLaptop ? IconInactiveBrush : IconActiveBrush;

            CardLaptopSpeakers.Background = isLaptop ? CardSelectedBgBrush : CardUnselectedBgBrush;
            CardLaptopSpeakers.BorderBrush = isLaptop ? CardSelectedBorderBrush : CardUnselectedBorderBrush;
            LaptopIconPath.Stroke = isLaptop ? IconActiveBrush : IconInactiveBrush;

            // 3. Preset Buttons
            UpdatePresetPillSelection(settings);

            // 4. Sliders
            SurroundSlider.Value = Math.Clamp(settings.SurroundLevel, 0, 100);
            BassSlider.Value = Math.Clamp(settings.BassLevel, 0, 100);
            ClaritySlider.Value = Math.Clamp(settings.ClarityLevel, 0, 100);
            SurroundValueText.Text = $"{(int)SurroundSlider.Value}%";
            BassValueText.Text = $"{(int)BassSlider.Value}%";
            ClarityValueText.Text = $"{(int)ClaritySlider.Value}%";

            // 5. Dynamic Island Toggle
            IslandToggleTrack.Background = settings.DynamicIslandEnabled ? SwitchOnTrackBrush : SwitchOffTrackBrush;
            IslandToggleThumb.Fill = settings.DynamicIslandEnabled ? SwitchOnThumbBrush : SwitchOffThumbBrush;
            IslandToggleThumb.HorizontalAlignment = settings.DynamicIslandEnabled
                ? System.Windows.HorizontalAlignment.Right
                : System.Windows.HorizontalAlignment.Left;

            // 6. Dolby Master Dependent Controls
            DspSlidersContainer.IsEnabled = settings.DolbyEnabled;
            DspSlidersContainer.Opacity = settings.DolbyEnabled ? 1.0 : 0.38;
        }
        finally
        {
            _isSyncingSettingsUi = false;
        }
    }

    private void UpdatePresetPillSelection(AppSettings settings)
    {
        bool isMusic = string.Equals(settings.DolbyPreset, "Music", StringComparison.OrdinalIgnoreCase)
            && settings.SurroundLevel == 70 && settings.BassLevel == 68 && settings.ClarityLevel == 65;
        bool isCinema = string.Equals(settings.DolbyPreset, "Cinema", StringComparison.OrdinalIgnoreCase)
            && settings.SurroundLevel == 92 && settings.BassLevel == 75 && settings.ClarityLevel == 72;
        bool isBass = string.Equals(settings.DolbyPreset, "BassBoost", StringComparison.OrdinalIgnoreCase)
            && settings.SurroundLevel == 65 && settings.BassLevel == 92 && settings.ClarityLevel == 58;
        bool isVocal = string.Equals(settings.DolbyPreset, "Vocal", StringComparison.OrdinalIgnoreCase)
            && settings.SurroundLevel == 55 && settings.BassLevel == 42 && settings.ClarityLevel == 90;
        bool isDynamic = string.Equals(settings.DolbyPreset, "Dynamic", StringComparison.OrdinalIgnoreCase)
            && settings.SurroundLevel == 78 && settings.BassLevel == 72 && settings.ClarityLevel == 75;

        SetPillState(PresetMusicBtn, isMusic);
        SetPillState(PresetCinemaBtn, isCinema);
        SetPillState(PresetBassBtn, isBass);
        SetPillState(PresetVocalBtn, isVocal);
        SetPillState(PresetDynamicBtn, isDynamic);
    }

    private static void SetPillState(Button btn, bool isSelected)
    {
        btn.Background = isSelected ? PillSelectedBgBrush : PillUnselectedBgBrush;
        btn.Foreground = isSelected ? PillSelectedFgBrush : PillUnselectedFgBrush;
        btn.BorderBrush = isSelected ? PillSelectedBorderBrush : PillUnselectedBorderBrush;
    }

    private void SaveAndApplySettings(AppSettings updated)
    {
        _settingsSaveDebounceTimer.Stop();
        _isAudioPushScheduled = false;
        _settingsService.Save(updated);
        SyncSettingsUiFromModel(updated);
        UpdateHeaderIndicators(updated);

        if (!updated.DynamicIslandEnabled)
        {
            _dynamicIsland.HideIsland();
        }
        else if (WindowState == WindowState.Minimized || !IsVisible)
        {
            _dynamicIsland.ShowIsland();
        }

        _ = PushAudioSettingsToWebViewAsync();
    }

    private void ScheduleSettingsSave()
    {
        _settingsSaveDebounceTimer.Stop();
        _settingsSaveDebounceTimer.Start();
    }

    private void FlushPendingSettingsSave()
    {
        if (_settingsSaveDebounceTimer.IsEnabled)
        {
            _settingsSaveDebounceTimer.Stop();
            _settingsService.Save(_settingsService.Current);
        }
    }

    private long _lastAudioPushTimestamp;
    private bool _isAudioPushScheduled;

    private async Task ThrottledPushAudioSettingsToWebViewAsync()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedMs = (now - _lastAudioPushTimestamp) * 1000.0 / Stopwatch.Frequency;

        if (elapsedMs >= 32)
        {
            _lastAudioPushTimestamp = now;
            await PushAudioSettingsToWebViewAsync();
        }
        else if (!_isAudioPushScheduled)
        {
            _isAudioPushScheduled = true;
            int delayMs = (int)Math.Max(1, 32 - elapsedMs);
            _ = Task.Delay(delayMs).ContinueWith(_ =>
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    _isAudioPushScheduled = false;
                    _lastAudioPushTimestamp = Stopwatch.GetTimestamp();
                    await PushAudioSettingsToWebViewAsync();
                });
            });
        }
    }

    private void OnDolbyMasterToggleClick(object sender, RoutedEventArgs e)
    {
        AppSettings next = _settingsService.Current.Clone();
        next.DolbyEnabled = !next.DolbyEnabled;
        SaveAndApplySettings(next);
    }

    private void OnDeviceHeadphonesClick(object sender, RoutedEventArgs e)
    {
        AppSettings next = _settingsService.Current.Clone();
        next.DolbyDeviceMode = "Headphones";
        next.DolbyEnabled = true;
        SaveAndApplySettings(next);
    }

    private void OnDeviceLaptopClick(object sender, RoutedEventArgs e)
    {
        AppSettings next = _settingsService.Current.Clone();
        next.DolbyDeviceMode = "LaptopSpeakers";
        next.DolbyEnabled = true;
        SaveAndApplySettings(next);
    }

    private void ApplyPreset(string presetName, int surround, int bass, int clarity)
    {
        AppSettings next = _settingsService.Current.Clone();
        next.DolbyEnabled = true;
        next.DolbyPreset = presetName;
        next.SurroundLevel = surround;
        next.BassLevel = bass;
        next.ClarityLevel = clarity;
        SaveAndApplySettings(next);
    }

    private void OnPresetMusicClick(object sender, RoutedEventArgs e) => ApplyPreset("Music", 70, 68, 65);
    private void OnPresetCinemaClick(object sender, RoutedEventArgs e) => ApplyPreset("Cinema", 92, 75, 72);
    private void OnPresetBassClick(object sender, RoutedEventArgs e) => ApplyPreset("BassBoost", 65, 92, 58);
    private void OnPresetVocalClick(object sender, RoutedEventArgs e) => ApplyPreset("Vocal", 55, 42, 90);
    private void OnPresetDynamicClick(object sender, RoutedEventArgs e) => ApplyPreset("Dynamic", 78, 72, 75);

    private void OnDspSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingSettingsUi || !IsLoaded)
        {
            return;
        }

        int s = (int)Math.Round(SurroundSlider.Value);
        int b = (int)Math.Round(BassSlider.Value);
        int c = (int)Math.Round(ClaritySlider.Value);

        SurroundValueText.Text = $"{s}%";
        BassValueText.Text = $"{b}%";
        ClarityValueText.Text = $"{c}%";

        _settingsService.Current.SurroundLevel = s;
        _settingsService.Current.BassLevel = b;
        _settingsService.Current.ClarityLevel = c;
        UpdatePresetPillSelection(_settingsService.Current);

        _ = ThrottledPushAudioSettingsToWebViewAsync();
        ScheduleSettingsSave();
    }

    private void OnIslandMasterToggleClick(object sender, RoutedEventArgs e)
    {
        AppSettings next = _settingsService.Current.Clone();
        next.DynamicIslandEnabled = !next.DynamicIslandEnabled;
        SaveAndApplySettings(next);
    }

    private void OnOpenGitHubClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/ArisaAkiyama/ytm-desktop")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch errors
        }
    }

    private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreButtonClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseCaptionButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task PushAudioSettingsToWebViewAsync()
    {
        if (MusicWebView.CoreWebView2 is null)
        {
            return;
        }

        string settingsJson = _settingsService.ToJson();
        try
        {
            await MusicWebView.CoreWebView2.ExecuteScriptAsync(
                $"window.__spotifyDesktopControl && window.__spotifyDesktopControl.applyAudioSettings({settingsJson});");
        }
        catch
        {
            // Ignore transient script errors
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await InitializeWebViewAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize WebView2 engine:\n{ex.Message}",
                "Spotify",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task InitializeWebViewAsync()
    {
        string userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpotifyDesktop",
            "UserData");

        Directory.CreateDirectory(userDataFolder);

        string browserArgs = string.Join(" ",
        [
            "--autoplay-policy=no-user-gesture-required",
            "--enable-features=EncryptedMedia,HardwareMediaKeyHandling,MediaSessionService",
            "--disable-background-timer-throttling",
            "--disable-renderer-backgrounding",
            "--disable-backgrounding-occluded-windows",
            "--disable-features=CalculateNativeWinOcclusion,msWebOOUI,msPdfOOUI"
        ]);

        var envOptions = new CoreWebView2EnvironmentOptions(browserArgs)
        {
            Language = "id-ID"
        };
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder,
            options: envOptions);

        await MusicWebView.EnsureCoreWebView2Async(environment);

        CoreWebView2 coreWv = MusicWebView.CoreWebView2;

        // Clean Desktop Chrome User-Agent to ensure seamless Account Sign-In & full Spotify Web Player features
        string defaultUa = coreWv.Settings.UserAgent;
        int edgIndex = defaultUa.IndexOf(" Edg/", StringComparison.OrdinalIgnoreCase);
        if (edgIndex > 0)
        {
            coreWv.Settings.UserAgent = defaultUa[..edgIndex];
        }

        coreWv.Settings.AreDefaultContextMenusEnabled = true;
        coreWv.Settings.AreDevToolsEnabled = true;
        coreWv.Settings.IsStatusBarEnabled = false;
        coreWv.Settings.IsZoomControlEnabled = true;
        coreWv.Settings.IsWebMessageEnabled = true;

        coreWv.DocumentTitleChanged += (_, _) =>
        {
            string docTitle = coreWv.DocumentTitle;
            _currentDocumentTitle = string.IsNullOrWhiteSpace(docTitle) ? "Spotify" : docTitle;
            if (!_isSettingsOpen)
            {
                Title = _currentDocumentTitle;
            }
            _systemTrayService.UpdateTrackTooltip(_currentDocumentTitle);

            if (!string.IsNullOrEmpty(docTitle) &&
                (docTitle.Contains("Iklan", StringComparison.OrdinalIgnoreCase) ||
                 docTitle.Contains("Advertisement", StringComparison.OrdinalIgnoreCase)))
            {
                _ = coreWv.ExecuteScriptAsync("window.__spotifyDesktopControl && window.__spotifyDesktopControl.forceSkipAd();");
            }
        };

        coreWv.DOMContentLoaded += async (_, _) =>
        {
            await PushAudioSettingsToWebViewAsync();
        };

        coreWv.NavigationCompleted += async (_, _) =>
        {
            await PushAudioSettingsToWebViewAsync();
        };

        coreWv.WebMessageReceived += OnWebMessageReceived;
        coreWv.NewWindowRequested += OnNewWindowRequested;
        coreWv.PermissionRequested += (_, args) =>
        {
            if (args.PermissionKind == CoreWebView2PermissionKind.Notifications ||
                args.PermissionKind == CoreWebView2PermissionKind.Autoplay)
            {
                args.State = CoreWebView2PermissionState.Allow;
            }
        };

        // Attach 4-Layer AdBlock & Studio Dolby Engine BEFORE first navigation
        await _adBlockService.AttachAsync(coreWv);

        coreWv.Navigate(SpotifyUrl);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            MediaPlaybackState? state = JsonSerializer.Deserialize<MediaPlaybackState>(json);
            if (state is not null)
            {
                _dynamicIsland.UpdateMediaState(state);
            }
        }
        catch
        {
            // Ignore malformed telemetry messages
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? targetUri))
        {
            return;
        }

        if (IsTrustedInternalHost(targetUri.Host))
        {
            MusicWebView.CoreWebView2?.Navigate(targetUri.AbsoluteUri);
            return;
        }

        if (targetUri.Scheme == Uri.UriSchemeHttps || targetUri.Scheme == Uri.UriSchemeHttp)
        {
            try
            {
                Process.Start(new ProcessStartInfo(targetUri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore if default browser launch fails
            }
        }
    }

    private static bool IsTrustedInternalHost(string host) =>
        host.Equals("open.spotify.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("spotify.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".spotify.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("google.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("facebook.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("appleid.apple.com", StringComparison.OrdinalIgnoreCase);

    private async Task ExecutePlayerCommandAsync(string command)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            if (MusicWebView.CoreWebView2 is null)
            {
                return;
            }

            string invocation = command.Contains('(') ? command : $"{command}()";
            try
            {
                await MusicWebView.CoreWebView2.ExecuteScriptAsync(
                    $"window.__spotifyDesktopControl && window.__spotifyDesktopControl.{invocation};");
            }
            catch
            {
                // Ignore transient JS errors
            }
        });
    }

    public void RestoreFromTray()
    {
        _dynamicIsland.HideIsland();

        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = _lastWindowState;
        }

        Activate();
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(hwnd);
        }
        if (!_isSettingsOpen)
        {
            MusicWebView.Focus();
        }
    }

    public void AllowSessionEndingClose()
    {
        FlushPendingSettingsSave();
        _isExplicitExit = true;
        _dynamicIsland.SafeClose();
        _systemTrayService.Dispose();
    }

    private void ExitApplication()
    {
        FlushPendingSettingsSave();
        _isExplicitExit = true;
        _dynamicIsland.SafeClose();
        _systemTrayService.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        FlushPendingSettingsSave();

        if (!_isExplicitExit)
        {
            // Keep music playing in background via System Tray & show Dynamic Island (if enabled) when user clicks 'X'
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;

            if (_settingsService.Current.DynamicIslandEnabled)
            {
                _dynamicIsland.ShowIsland();
            }
            else
            {
                _dynamicIsland.HideIsland();
            }
            return;
        }

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _dynamicIsland.SafeClose();
        _systemTrayService.Dispose();
        base.OnClosing(e);
    }
}
