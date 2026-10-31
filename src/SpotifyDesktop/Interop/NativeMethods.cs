using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace SpotifyDesktop.Interop;

public static class NativeMethods
{
    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMNCRP_DISABLED = 1;
    private const int DWMWA_CLOAKED = 14;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    public const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Applies Windows 11 Native Dark Mode Titlebar matching Spotify Web's #000000 dark theme.
    /// </summary>
    public static void ApplyDarkTitleBar(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int useDarkMode = 1;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
        }

        // COLORREF format is 0x00BBGGRR -> #000000 is 0x00000000
        int captionColor = 0x00000000;
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

        // White text on titlebar -> 0x00F1F1F1
        int textColor = 0x00F1F1F1;
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
    }

    /// <summary>
    /// Configures the Dynamic Island overlay so it has NO rectangular DWM shadow,
    /// never steals focus from active apps, and hides from Alt+Tab.
    /// </summary>
    public static void ConfigureNoActivateOverlay(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        // Disable any Windows DWM rectangular non-client shadow around the transparent window
        int ncPolicy = DWMNCRP_DISABLED;
        DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref ncPolicy, sizeof(int));
    }

    /// <summary>
    /// Returns true ONLY when the user is viewing the Desktop ("Show Desktop" / Win+D or all other apps minimized),
    /// and false whenever any other application window (such as Microsoft Edge, Explorer, IDEs, etc.) is open on screen.
    /// </summary>
    public static bool IsDesktopCurrentlyShown()
    {
        uint currentPid = (uint)Environment.ProcessId;
        bool isDesktopShown = true;
        StringBuilder classBuilder = new(128);

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == currentPid)
            {
                return true;
            }

            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            classBuilder.Clear();
            if (GetClassName(hwnd, classBuilder, classBuilder.Capacity) <= 0)
            {
                return true;
            }

            string className = classBuilder.ToString();

            // If we hit the Desktop wallpaper host (Progman or WorkerW containing SHELLDLL_DefView)
            // before hitting any visible application window, then "Show Desktop" is active!
            if (className is "Progman" or "WorkerW")
            {
                IntPtr defView = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                {
                    isDesktopShown = true;
                    return false; // Stop enumerating: Desktop is above all other windows
                }
                return true;
            }

            // Ignore system taskbar, tray popups, and shell overlays
            if (className is "Shell_TrayWnd"
                or "Shell_SecondaryTrayWnd"
                or "NotifyIconOverflowWindow"
                or "TopLevelWindowForOverflowXamlIsland"
                or "Windows.UI.Core.CoreWindow"
                or "XamlExplorerHostIslandWindow"
                or "ForegroundStaging"
                or "ApplicationManager_DesktopShellWindow"
                or "DummyDWMListenerWindow"
                or "EdgeUiInputTopWndClass"
                or "EdgeUiInputWndClass")
            {
                return true;
            }

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0;
            bool isNoActivate = (exStyle & WS_EX_NOACTIVATE) != 0;
            bool isTransparent = (exStyle & WS_EX_TRANSPARENT) != 0;
            if (isToolWindow || isNoActivate || isTransparent)
            {
                return true;
            }

            if (!GetWindowRect(hwnd, out RECT rect))
            {
                return true;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 120 || height < 80)
            {
                return true;
            }

            // Found a visible, un-minimized user application window above the Desktop
            isDesktopShown = false;
            return false;
        }, IntPtr.Zero);

        return isDesktopShown;
    }
}
