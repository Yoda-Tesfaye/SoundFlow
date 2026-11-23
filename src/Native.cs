using System;
using System.Runtime.InteropServices;

namespace SoundFlow
{
    /// <summary>Minimal Win32 interop used by the tray icon and the flyout menu.</summary>
    internal static class Native
    {
        // window messages
        public const int WM_APP = 0x8000;
        public const int WM_TRAY = WM_APP + 1;
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_CONTEXTMENU = 0x007B;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONUP = 0x0205;

        // tray callback ids (NOTIFYICON_VERSION_4)
        public const int NIN_SELECT = 0x0400;
        public const int NIN_KEYSELECT = 0x0401;
        public const int NIN_BALLOONUSERCLICK = 0x0405;

        // NOTIFYICONDATA
        public const int NIF_MESSAGE = 0x00000001;
        public const int NIF_ICON = 0x00000002;
        public const int NIF_TIP = 0x00000004;
        public const int NIF_INFO = 0x00000010;
        public const int NIF_SHOWTIP = 0x00000080;

        public const int NIM_ADD = 0;
        public const int NIM_MODIFY = 1;
        public const int NIM_DELETE = 2;
        public const int NIM_SETVERSION = 4;
        public const int NOTIFYICON_VERSION_4 = 4;

        public const int NIIF_INFO = 1;
        public const int NIIF_ERROR = 3;

        public const int SM_CXSMICON = 49;
        public const int SM_CYSMICON = 50;

        // document window attributes (Windows 11)
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;

        // monitor dpi
        public const int MONITOR_DEFAULTTONEAREST = 2;
        public const int MDT_EFFECTIVE_DPI = 0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;

            public int dwState;
            public int dwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;

            // union: uTimeout / uVersion
            public int uTimeoutOrVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;

            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public Guid guidItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern bool Shell_NotifyIconW(int message, ref NOTIFYICONDATA data);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT rect);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr ShellExecuteW(IntPtr hwnd, string operation, string file, string parameters, string directory, int show);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessageW(string name);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT point, int flags);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out int dpiX, out int dpiY);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr reserved, int coInit);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();
    }
}

