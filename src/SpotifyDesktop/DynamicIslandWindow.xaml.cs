using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SpotifyDesktop.Interop;
using SpotifyDesktop.Models;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace SpotifyDesktop;

public partial class DynamicIslandWindow : Window
{
    private enum IslandMode
    {
        Hidden,
        Compact,
        Expanded
    }

    private const double CompactW = 256.0;
    private const double CompactH = 38.0;
    private const double CompactR = 19.0;

    private const double ExpandedW = 380.0;
    private const double ExpandedH = 168.0;
    private const double ExpandedR = 32.0;

    private const string PlayGeometryData = "M 3,1.5 L 15,8.5 L 3,15.5 Z";
    private const string PauseGeometryData = "M 2.5,2 L 6.5,2 L 6.5,15 L 2.5,15 Z M 10.5,2 L 14.5,2 L 14.5,15 L 10.5,15 Z";

    private readonly Action _restoreMainWindowAction;
    private readonly Func<string, Task> _executeMediaCommandAsync;
    private readonly DispatcherTimer _desktopVisibilityTimer;
    private readonly DispatcherTimer _collapseDebounceTimer;
    private readonly DispatcherTimer _autoCollapseToastTimer;
    private readonly Stopwatch _renderClock = new();

    private IslandMode _currentMode = IslandMode.Hidden;
    private bool _isAppMinimized;
    private bool _isIslandVisibleOnDesktop;
    private bool _isRenderingHooked;
    private bool _isPlaying;
    private double _currentSeconds;
    private double _durationSeconds;
    private string _lastAlbumArtUrl = string.Empty;
    private string _lastTrackTitle = string.Empty;
    private double _lastFrameTimeSeconds;

    // Unified Shared-Element Morph State:
    // _curMorph: 0.0 = Compact Pill (256x38), 1.0 = Expanded Card (380x168)
    // _curPresence: 0.0 = Hidden, 1.0 = Visible on Desktop
    private double _curMorph;
    private double _velMorph;
    private double _targetMorph;

    private double _curPresence;
    private double _velPresence;
    private double _targetPresence;

    private double _wavePhase;

    // Horizontal drag state
    private bool _isDraggingIsland;
    private bool _didMoveDuringDrag;
    private Point _dragStartScreenPoint;
    private double _dragStartWindowLeft;

    public DynamicIslandWindow(
        Action restoreMainWindowAction,
        Func<string, Task> executeMediaCommandAsync)
    {
        InitializeComponent();

        _restoreMainWindowAction = restoreMainWindowAction;
        _executeMediaCommandAsync = executeMediaCommandAsync;

        PositionAtTopCenter();
        SetDefaultAlbumArt();

        SourceInitialized += (_, _) => NativeMethods.ConfigureNoActivateOverlay(this);

        // Checks whether user is on "Show Desktop" vs using another app (like Microsoft Edge)
        _desktopVisibilityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(160)
        };
        _desktopVisibilityTimer.Tick += OnDesktopVisibilityTick;

        _collapseDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _collapseDebounceTimer.Tick += (_, _) =>
        {
            _collapseDebounceTimer.Stop();
            if (!IslandPill.IsMouseOver && !_isDraggingIsland && _currentMode == IslandMode.Expanded)
            {
                SetMode(IslandMode.Compact);
            }
        };

        _autoCollapseToastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2600)
        };
        _autoCollapseToastTimer.Tick += (_, _) =>
        {
            _autoCollapseToastTimer.Stop();
            if (!IslandPill.IsMouseOver && _currentMode == IslandMode.Expanded)
            {
                SetMode(IslandMode.Compact);
            }
        };
    }

    public void PositionAtTopCenter()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left + Math.Round((workArea.Width - Width) / 2.0);
        Top = workArea.Top;
    }

    public void ShowIsland()
    {
        _isAppMinimized = true;
        _desktopVisibilityTimer.Start();
        EvaluateDesktopVisibility();
    }

    public void HideIsland()
    {
        _isAppMinimized = false;
        _desktopVisibilityTimer.Stop();
        HideFromDesktopSurface();
    }

    private void OnDesktopVisibilityTick(object? sender, EventArgs e)
    {
        if (!_isAppMinimized)
        {
            return;
        }
        EvaluateDesktopVisibility();
    }

    private void EvaluateDesktopVisibility()
    {
        bool desktopShown = NativeMethods.IsDesktopCurrentlyShown();
        if (desktopShown && !_isIslandVisibleOnDesktop)
        {
            ShowOnDesktopSurface();
        }
        else if (!desktopShown && _isIslandVisibleOnDesktop)
        {
            HideFromDesktopSurface();
        }
    }

    private void ShowOnDesktopSurface()
    {
        _isIslandVisibleOnDesktop = true;

        if (!IsVisible)
        {
            _curMorph = 0.0;
            _velMorph = 0.0;
            _curPresence = 0.0;
            _velPresence = 0.0;
            ApplySharedElementMorph();
            Show();
        }

        SetMode(IslandMode.Compact);
        StartVSyncLoop();
    }

    private void HideFromDesktopSurface()
    {
        _isIslandVisibleOnDesktop = false;
        _collapseDebounceTimer.Stop();
        _autoCollapseToastTimer.Stop();
        SetMode(IslandMode.Hidden);
        StartVSyncLoop();
    }

    private void StartVSyncLoop()
    {
        if (_isRenderingHooked)
        {
            return;
        }
        _isRenderingHooked = true;
        _renderClock.Restart();
        _lastFrameTimeSeconds = 0;
        CompositionTarget.Rendering += OnVSyncRendering;
    }

    private void StopVSyncLoop()
    {
        if (!_isRenderingHooked)
        {
            return;
        }
        _isRenderingHooked = false;
        CompositionTarget.Rendering -= OnVSyncRendering;
        _renderClock.Stop();
    }

    public void UpdateMediaState(MediaPlaybackState state)
    {
        _isPlaying = state.IsPlaying;
        _currentSeconds = state.CurrentSeconds;
        _durationSeconds = state.DurationSeconds;

        string title = string.IsNullOrWhiteSpace(state.Title) ? "Spotify" : state.Title;
        string artist = string.IsNullOrWhiteSpace(state.Artist) ? "Spotify" : state.Artist;

        SharedTitleText.Text = title;
        SharedArtistText.Text = artist;

        PlayPauseIconPath.Data = Geometry.Parse(_isPlaying ? PauseGeometryData : PlayGeometryData);

        CurrentTimeText.Text = FormatTime(_currentSeconds);
        TotalTimeText.Text = FormatTime(_durationSeconds);
        UpdateProgressBarFillWidth();

        if (!string.IsNullOrWhiteSpace(state.AlbumArtUrl) &&
            !string.Equals(state.AlbumArtUrl, _lastAlbumArtUrl, StringComparison.Ordinal))
        {
            _lastAlbumArtUrl = state.AlbumArtUrl;
            UpdateAlbumArtImage(state.AlbumArtUrl);
        }

        // Briefly pop out like iPhone Live Activity when song changes on Desktop
        if (_isIslandVisibleOnDesktop &&
            !string.IsNullOrEmpty(_lastTrackTitle) &&
            !string.Equals(_lastTrackTitle, title, StringComparison.Ordinal) &&
            !string.Equals(title, "Spotify", StringComparison.Ordinal) &&
            _currentMode == IslandMode.Compact)
        {
            SetMode(IslandMode.Expanded);
            _autoCollapseToastTimer.Stop();
            _autoCollapseToastTimer.Start();
        }

        _lastTrackTitle = title;
    }

    private void UpdateProgressBarFillWidth()
    {
        double progressRatio = _durationSeconds > 0
            ? Math.Clamp(_currentSeconds / _durationSeconds, 0.0, 1.0)
            : 0.0;
        double trackWidth = ProgressBarTrack.ActualWidth > 0 ? ProgressBarTrack.ActualWidth : 244.0;
        ProgressBarFill.Width = Math.Round(trackWidth * progressRatio);
    }

    private void SetMode(IslandMode mode)
    {
        _currentMode = mode;

        switch (mode)
        {
            case IslandMode.Hidden:
                _targetMorph = 0.0;
                _targetPresence = 0.0;
                BottomControlsPanel.IsHitTestVisible = false;
                break;

            case IslandMode.Compact:
                _targetMorph = 0.0;
                _targetPresence = 1.0;
                BottomControlsPanel.IsHitTestVisible = false;
                break;

            case IslandMode.Expanded:
                _targetMorph = 1.0;
                _targetPresence = 1.0;
                BottomControlsPanel.Visibility = Visibility.Visible;
                BottomControlsPanel.IsHitTestVisible = true;
                break;
        }
    }

    private void OnVSyncRendering(object? sender, EventArgs e)
    {
        double now = _renderClock.Elapsed.TotalSeconds;
        double dt = now - _lastFrameTimeSeconds;
        _lastFrameTimeSeconds = now;

        // Clamp delta time to prevent jumps if system was paused
        dt = Math.Clamp(dt, 0.001, 0.028);

        // Unified Spring Physics (Critically Balanced iOS 17 Spring: omega = 22.5, zeta = 0.85)
        const double omegaMorph = 22.5;
        const double zetaMorph = 0.85;
        const double omegaPresence = 26.0;
        const double zetaPresence = 0.88;

        const int subSteps = 4;
        double stepDt = dt / subSteps;

        for (int i = 0; i < subSteps; i++)
        {
            StepSpring(ref _curMorph, ref _velMorph, _targetMorph, omegaMorph, zetaMorph, stepDt);
            StepSpring(ref _curPresence, ref _velPresence, _targetPresence, omegaPresence, zetaPresence, stepDt);
        }

        ApplySharedElementMorph();

        if (_currentMode == IslandMode.Hidden &&
            _curPresence < 0.02 &&
            Math.Abs(_curMorph) < 0.02)
        {
            StopVSyncLoop();
            Hide();
            return;
        }

        AnimateWaveformBars(dt);
    }

    private static void StepSpring(
        ref double value,
        ref double velocity,
        double target,
        double omega,
        double zeta,
        double dt)
    {
        double displacement = value - target;
        double accel = (-omega * omega * displacement) - (2.0 * zeta * omega * velocity);
        velocity += accel * dt;
        value += velocity * dt;
    }

    /// <summary>
    /// Computes all shared-element coordinates, sizes, and opacities from a single unified morph factor (_curMorph)
    /// so the capsule, album art, title, artist, waveform, and controls move as one cohesive organism.
    /// </summary>
    private void ApplySharedElementMorph()
    {
        double t = _curMorph;
        double clampedT = Math.Clamp(t, 0.0, 1.0);
        double presence = Math.Clamp(_curPresence, 0.0, 1.0);

        // 1. Capsule Container Size & Clipping Geometry
        double curW = Math.Max(120.0, Lerp(CompactW, ExpandedW, t));
        double curH = Math.Max(20.0, Lerp(CompactH, ExpandedH, t));
        double curR = Math.Max(10.0, Lerp(CompactR, ExpandedR, clampedT));

        IslandPill.Width = curW;
        IslandPill.Height = curH;
        IslandPill.CornerRadius = new CornerRadius(curR);
        IslandPill.Opacity = presence;

        double popScale = Lerp(0.75, 1.0, presence);
        IslandScale.ScaleX = popScale;
        IslandScale.ScaleY = popScale;

        IslandClipGeometry.Rect = new Rect(0, 0, curW, curH);
        IslandClipGeometry.RadiusX = curR;
        IslandClipGeometry.RadiusY = curR;

        // 2. Shared Element 1: Album Art (24x24 at [11, 7] -> 54x54 at [22, 18])
        double artSize = Math.Max(16.0, Lerp(24.0, 54.0, t));
        double artRadius = Math.Max(4.0, Lerp(7.0, 13.0, clampedT));
        double artLeft = Lerp(11.0, 22.0, t);
        double artTop = Lerp(7.0, 18.0, t);

        SharedAlbumArtBorder.Width = artSize;
        SharedAlbumArtBorder.Height = artSize;
        SharedAlbumArtBorder.CornerRadius = new CornerRadius(artRadius);
        SharedAlbumArtBorder.Margin = new Thickness(artLeft, artTop, 0, 0);
        SharedAlbumArtClip.Rect = new Rect(0, 0, artSize, artSize);
        SharedAlbumArtClip.RadiusX = artRadius;
        SharedAlbumArtClip.RadiusY = artRadius;

        // 3. Shared Element 2: Track Title & Artist (Glides beside growing Album Art)
        double textLeft = Lerp(46.0, 90.0, t);
        double textRight = Lerp(56.0, 74.0, t);
        double titleTop = Lerp(10.0, 23.0, t);
        double artistTop = Lerp(24.0, 45.0, t);

        SharedTitleText.Margin = new Thickness(textLeft, titleTop, textRight, 0);
        SharedTitleText.FontSize = Lerp(12.0, 14.0, clampedT);

        SharedArtistText.Margin = new Thickness(textLeft, artistTop, textRight, 0);
        SharedArtistText.Opacity = Math.Clamp((clampedT - 0.20) / 0.65, 0.0, 1.0);

        // 4. Shared Element 3: Right Audio Waveform (Rides the right edge & grows taller)
        double waveRight = Lerp(14.0, 22.0, t);
        double waveTop = Lerp(10.0, 36.0, t);
        double waveScaleX = Lerp(1.0, 1.22, clampedT);
        double waveScaleY = Lerp(1.0, 1.32, clampedT);

        SharedWaveformPanel.Margin = new Thickness(0, waveTop, waveRight, 0);
        WaveformScale.ScaleX = waveScaleX;
        WaveformScale.ScaleY = waveScaleY;

        // 5. Shared Element 4: Bottom Progress Bar & Media Controls Drawer
        if (clampedT < 0.06 && _currentMode != IslandMode.Expanded)
        {
            BottomControlsPanel.Visibility = Visibility.Collapsed;
            BottomControlsPanel.Opacity = 0.0;
        }
        else
        {
            BottomControlsPanel.Visibility = Visibility.Visible;
            double controlsProgress = Math.Clamp((clampedT - 0.22) / 0.72, 0.0, 1.0);
            BottomControlsPanel.Opacity = controlsProgress;

            double controlsScale = Lerp(0.92, 1.0, controlsProgress);
            BottomControlsScale.ScaleX = controlsScale;
            BottomControlsScale.ScaleY = controlsScale;
            BottomControlsTranslate.Y = Lerp(-10.0, 0.0, controlsProgress);
            UpdateProgressBarFillWidth();
        }
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private void AnimateWaveformBars(double dt)
    {
        double smoothFactor = Math.Clamp(dt * 18.0, 0.0, 1.0);

        if (!_isPlaying)
        {
            LerpBar(WaveBar1, 4.0, smoothFactor);
            LerpBar(WaveBar2, 4.0, smoothFactor);
            LerpBar(WaveBar3, 4.0, smoothFactor);
            LerpBar(WaveBar4, 4.0, smoothFactor);
            LerpBar(WaveBar5, 4.0, smoothFactor);
            return;
        }

        _wavePhase += dt * 10.5;

        LerpBar(WaveBar1, 4.5 + 9.5 * Math.Abs(Math.Sin(_wavePhase * 1.15 + 0.2)), smoothFactor);
        LerpBar(WaveBar2, 4.5 + 11.5 * Math.Abs(Math.Sin(_wavePhase * 1.60 + 1.4)), smoothFactor);
        LerpBar(WaveBar3, 5.0 + 12.0 * Math.Abs(Math.Sin(_wavePhase * 1.35 + 2.5)), smoothFactor);
        LerpBar(WaveBar4, 4.5 + 10.5 * Math.Abs(Math.Sin(_wavePhase * 1.80 + 0.9)), smoothFactor);
        LerpBar(WaveBar5, 4.5 + 8.5 * Math.Abs(Math.Sin(_wavePhase * 1.25 + 3.1)), smoothFactor);
    }

    private static void LerpBar(Rectangle bar, double target, double factor)
    {
        bar.Height += (target - bar.Height) * factor;
    }

    private void OnIslandMouseEnter(object sender, MouseEventArgs e)
    {
        _collapseDebounceTimer.Stop();
        _autoCollapseToastTimer.Stop();

        if (_currentMode == IslandMode.Compact)
        {
            SetMode(IslandMode.Expanded);
        }
    }

    private void OnIslandMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDraggingIsland)
        {
            return;
        }

        _collapseDebounceTimer.Stop();
        _collapseDebounceTimer.Start();
    }

    private void OnIslandMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInsideInteractiveChild(source))
        {
            return;
        }

        _isDraggingIsland = true;
        _didMoveDuringDrag = false;
        _dragStartScreenPoint = PointToScreen(e.GetPosition(this));
        _dragStartWindowLeft = Left;
        IslandPill.CaptureMouse();
        Cursor = Cursors.SizeWE;
    }

    private void OnIslandMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingIsland)
        {
            return;
        }

        Point currentScreenPoint = PointToScreen(e.GetPosition(this));
        double deltaX = currentScreenPoint.X - _dragStartScreenPoint.X;
        if (Math.Abs(deltaX) > 4)
        {
            _didMoveDuringDrag = true;
            Rect workArea = SystemParameters.WorkArea;
            Left = Math.Clamp(_dragStartWindowLeft + deltaX, workArea.Left - 80, workArea.Right - Width + 80);
        }
    }

    private void OnIslandMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingIsland)
        {
            return;
        }

        _isDraggingIsland = false;
        IslandPill.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;

        if (!_didMoveDuringDrag && _currentMode == IslandMode.Compact)
        {
            SetMode(IslandMode.Expanded);
        }
    }

    private static bool IsInsideInteractiveChild(DependencyObject element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is System.Windows.Controls.Button)
            {
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        _isPlaying = !_isPlaying;
        PlayPauseIconPath.Data = Geometry.Parse(_isPlaying ? PauseGeometryData : PlayGeometryData);
        await _executeMediaCommandAsync("playPause()");
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        await _executeMediaCommandAsync("next()");
    }

    private async void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        await _executeMediaCommandAsync("previous()");
    }

    private async void OnProgressBarClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (ProgressBarTrack.ActualWidth <= 0)
        {
            return;
        }

        Point clickPt = e.GetPosition(ProgressBarTrack);
        double fraction = Math.Clamp(clickPt.X / ProgressBarTrack.ActualWidth, 0.0, 1.0);
        ProgressBarFill.Width = Math.Round(ProgressBarTrack.ActualWidth * fraction);

        string fractionInvariant = fraction.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        await _executeMediaCommandAsync($"seekTo({fractionInvariant})");
    }

    private void OnOpenAppButtonClick(object sender, RoutedEventArgs e)
    {
        _restoreMainWindowAction();
    }

    private void OnAlbumArtClick(object sender, MouseButtonEventArgs e)
    {
        if (!_didMoveDuringDrag)
        {
            e.Handled = true;
            _restoreMainWindowAction();
        }
    }

    private bool _isDisposed;

    public void SafeClose()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isDisposed = true;
        _desktopVisibilityTimer.Stop();
        _collapseDebounceTimer.Stop();
        _autoCollapseToastTimer.Stop();
        StopVSyncLoop();
        base.OnClosed(e);
    }

    private void UpdateAlbumArtImage(string url)
    {
        try
        {
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(url, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DownloadFailed += (_, _) => Dispatcher.InvokeAsync(SetDefaultAlbumArt);
            bitmap.DecodeFailed += (_, _) => Dispatcher.InvokeAsync(SetDefaultAlbumArt);
            bitmap.EndInit();

            SharedAlbumArtImage.Source = bitmap;
        }
        catch
        {
            SetDefaultAlbumArt();
        }
    }

    private void SetDefaultAlbumArt()
    {
        try
        {
            BitmapImage fallback = new(new Uri("pack://application:,,,/Assets/spotify.ico", UriKind.Absolute));
            SharedAlbumArtImage.Source = fallback;
        }
        catch
        {
            // Ignore fallback load failure
        }
    }

    private static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
        {
            return "0:00";
        }

        TimeSpan ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
