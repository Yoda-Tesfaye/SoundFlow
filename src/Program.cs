using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Foundation;
using Windows.Media.Audio;

namespace SoundFlow
{
    internal static class Program
    {
        internal static string ExePath;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            ExePath = Application.ExecutablePath;

            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\SoundFlow.Single", out created))
            {
                if (!created)
                {
                    // Another instance owns the tray icon: ask it to open its menu instead.
                    Native.PostMessage(
                        new IntPtr(0xFFFF),
                        Native.RegisterWindowMessageW(App.ShowMenuMessageName),
                        IntPtr.Zero,
                        IntPtr.Zero);
                    return;
                }

                if (!AudioLink.IsSupported())
                {
                    MessageBox.Show(
                        "此版本的 Windows 不支持蓝牙音频接收 (A2DP Sink)。\n需要 Windows 10 2004 或更高版本。",
                        App.DisplayName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                try
                {
                    Application.Run(new App());
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }
        }
    }

    internal sealed class PendingConnect
    {
        public string DeviceId;
        public string Name;
        public AudioPlaybackConnection Connection;
        public IAsyncInfo Operation;
        public int Step; // 1 = StartAsync running, 2 = OpenAsync running
        public DateTime Started;
    }

    internal sealed class App : ApplicationContext
    {
        internal const string DisplayName = "SoundFlow";
        internal const string ShowMenuMessageName = "SoundFlow.ShowMenu";

        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValueName = "SoundFlow";

        readonly HostWindow _host;
        readonly TrayIcon _tray;
        readonly DeviceWatcher _watcher;
        readonly System.Windows.Forms.Timer _tick;
        readonly System.Windows.Forms.Timer _reopen;
        readonly Dictionary<string, AudioPlaybackConnection> _links = new Dictionary<string, AudioPlaybackConnection>();
        readonly Dictionary<string, PendingConnect> _pending = new Dictionary<string, PendingConnect>();

        readonly int _taskbarCreatedMessage;
        readonly int _showMenuMessage;

        bool _menuOpen;
        DateTime _suppressMenuUntil = DateTime.MinValue;
        int _ticks;
        volatile bool _receiving;

        public App()
        {
            _host = new HostWindow();
            _host.Handler = HandleMessage;

            _taskbarCreatedMessage = Native.RegisterWindowMessageW("TaskbarCreated");
            _showMenuMessage = Native.RegisterWindowMessageW(ShowMenuMessageName);

            _tray = new TrayIcon(_host.Handle, Native.WM_TRAY);
            UpdateIcon();
            UpdateTip();

            _watcher = new DeviceWatcher();
            _watcher.PauseRefresh = IsReceiving;

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 250;
            _tick.Tick += OnTick;
            _tick.Start();

            _reopen = new System.Windows.Forms.Timer();
            _reopen.Interval = 280;
            _reopen.Tick += OnReopenTick;
        }

        bool HandleMessage(Message message)
        {
            if (message.Msg == Native.WM_TRAY)
            {
                OnTrayCallback(message.LParam);
                return true;
            }

            if (message.Msg == Native.WM_SETTINGCHANGE)
            {
                if (message.LParam != IntPtr.Zero)
                {
                    string area = Marshal.PtrToStringUni(message.LParam);
                    if (string.Equals(area, "ImmersiveColorSet", StringComparison.Ordinal))
                    {
                        UpdateIcon();
                    }
                }
                return false;
            }

            if (_taskbarCreatedMessage != 0 && message.Msg == _taskbarCreatedMessage)
            {
                _tray.ReInstall(BuildTip());
                UpdateIcon();
                return true;
            }

            if (_showMenuMessage != 0 && message.Msg == _showMenuMessage)
            {
                ShowMenuFromTray();
                return true;
            }

            return false;
        }

        void OnTrayCallback(IntPtr lParam)
        {
            int notification = (int)(lParam.ToInt64() & 0xFFFF);
            if (notification == Native.WM_CONTEXTMENU ||
                notification == Native.NIN_SELECT ||
                notification == Native.NIN_KEYSELECT ||
                notification == Native.WM_LBUTTONUP ||
                notification == Native.WM_RBUTTONUP)
            {
                ShowMenuFromTray();
            }
        }

        void ShowMenuFromTray()
        {
            if (_menuOpen)
            {
                return;
            }
            if (DateTime.UtcNow < _suppressMenuUntil)
            {
                return;
            }

            Rectangle anchor;
            if (!_tray.TryGetRect(out anchor) || !IsVisibleAnchor(anchor))
            {
                anchor = CursorAnchor();
            }
            ShowMenu(anchor);
        }

        void OnReopenTick(object sender, EventArgs e)
        {
            _reopen.Stop();
            if (_menuOpen)
            {
                return;
            }
            Rectangle anchor;
            if (!_tray.TryGetRect(out anchor) || !IsVisibleAnchor(anchor))
            {
                anchor = CursorAnchor();
            }
            ShowMenu(anchor);
        }

        static bool IsVisibleAnchor(Rectangle rect)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.Contains(rect))
                {
                    return true;
                }
            }
            return false;
        }

        static Rectangle CursorAnchor()
        {
            Native.POINT point;
            Native.GetCursorPos(out point);
            return new Rectangle(point.X - 8, point.Y - 8, 16, 16);
        }

        void ShowMenu(Rectangle anchor)
        {
            if (_menuOpen)
            {
                return;
            }

            Theme theme = Theme.Load();
            float scale = DpiScaleFor(anchor);
            List<MenuItem> items = BuildMenuItems();

            MenuItem selected = null;
            using (MenuForm form = new MenuForm(items, theme, scale))
            {
                form.PlaceNear(anchor);
                _menuOpen = true;
                try
                {
                    form.ShowDialog();
                    selected = form.Selected;
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
                finally
                {
                    _menuOpen = false;
                    _suppressMenuUntil = DateTime.UtcNow.AddMilliseconds(180);
                }
            }

            if (selected != null && selected.Click != null)
            {
                try
                {
                    selected.Click();
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }
        }

        List<MenuItem> BuildMenuItems()
        {
            List<MenuItem> items = new List<MenuItem>();
            List<AudioDevice> devices = _watcher.Snapshot;

            if (devices.Count == 0)
            {
                items.Add(new MenuItem { Text = "未找到可接收的设备", Enabled = false });
                items.Add(new MenuItem { Text = "请先在蓝牙设置中配对手机", Enabled = false });
            }
            else
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    AudioDevice device = devices[i];
                    string id = device.Id;
                    bool linked = _links.ContainsKey(id);
                    bool connecting = _pending.ContainsKey(id);

                    MenuItem item = new MenuItem();
                    item.Text = device.Name;
                    item.Glyph = linked ? null : Glyphs.Phone;
                    item.Checked = linked;
                    item.Enabled = !connecting;
                    if (connecting)
                    {
                        item.RightText = "连接中…";
                    }
                    else if (linked)
                    {
                        item.RightText = "接收中";
                    }
                    item.Click = delegate { ToggleDevice(id); };
                    items.Add(item);
                }
            }

            items.Add(MenuItem.SeparatorItem());
            items.Add(new MenuItem { Text = "刷新设备", Glyph = Glyphs.Refresh, Click = RefreshDevices });
            items.Add(new MenuItem { Text = "蓝牙设置", Glyph = Glyphs.Settings, Click = OpenBluetoothSettings });
            items.Add(new MenuItem { Text = "开机启动", Checked = StartupEnabled(), Click = ToggleStartup });
            items.Add(MenuItem.SeparatorItem());
            items.Add(new MenuItem { Text = "退出", Glyph = Glyphs.Power, Click = ExitApp });
            return items;
        }

        void RefreshDevices()
        {
            AudioLink.ClearNameCache();
            _watcher.Refresh();
            _reopen.Stop();
            _reopen.Start();
        }

        bool IsReceiving()
        {
            return _receiving;
        }

        void ToggleDevice(string id)
        {
            if (_links.ContainsKey(id))
            {
                DisconnectDevice(id);
                return;
            }
            if (_pending.ContainsKey(id))
            {
                return;
            }
            StartConnect(id);
        }

        void StartConnect(string id)
        {
            string name = DeviceNameFor(id);
            AudioPlaybackConnection connection = null;
            try
            {
                connection = AudioPlaybackConnection.TryCreateFromId(id);
                if (connection == null)
                {
                    NotifyFailure(name, "此设备不支持音频接收");
                    return;
                }

                IAsyncAction start = connection.StartAsync();
                PendingConnect pending = new PendingConnect();
                pending.DeviceId = id;
                pending.Name = name;
                pending.Connection = connection;
                pending.Step = 1;
                pending.Operation = start;
                pending.Started = DateTime.UtcNow;
                _pending[id] = pending;

                UpdateIcon();
                UpdateTip();
            }
            catch (Exception ex)
            {
                if (connection != null)
                {
                    try
                    {
                        connection.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }
                NotifyFailure(name, AudioLink.Describe(ex));
            }
        }

        void DisconnectDevice(string id)
        {
            AudioPlaybackConnection connection;
            if (!_links.TryGetValue(id, out connection))
            {
                return;
            }
            _links.Remove(id);
            try
            {
                connection.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            UpdateIcon();
            UpdateTip();
        }

        void OnTick(object sender, EventArgs e)
        {
            _ticks++;
            PollPending();
            if (_ticks % 4 == 0)
            {
                SyncLinks();
            }
        }

        void PollPending()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            List<PendingConnect> snapshot = new List<PendingConnect>(_pending.Values);
            for (int i = 0; i < snapshot.Count; i++)
            {
                PendingConnect pending = snapshot[i];

                if ((DateTime.UtcNow - pending.Started).TotalSeconds > 30.0)
                {
                    FailPending(pending, "连接超时");
                    continue;
                }

                AsyncStatus status;
                try
                {
                    status = pending.Operation.Status;
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    continue;
                }

                if (status == AsyncStatus.Started)
                {
                    continue;
                }
                if (status != AsyncStatus.Completed)
                {
                    FailPending(pending, "连接已取消");
                    continue;
                }

                try
                {
                    if (pending.Step == 1)
                    {
                        pending.Operation = pending.Connection.OpenAsync();
                        pending.Step = 2;
                        pending.Started = DateTime.UtcNow;
                        continue;
                    }

                    IAsyncOperation<AudioPlaybackConnectionOpenResult> open =
                        (IAsyncOperation<AudioPlaybackConnectionOpenResult>)pending.Operation;
                    AudioPlaybackConnectionOpenResult result = open.GetResults();
                    if (result.Status == AudioPlaybackConnectionOpenResultStatus.Success)
                    {
                        CompleteConnect(pending);
                    }
                    else
                    {
                        FailPending(pending, AudioLink.OpenResultText(result.Status));
                    }
                }
                catch (Exception ex)
                {
                    FailPending(pending, AudioLink.Describe(ex));
                }
            }
        }

        void CompleteConnect(PendingConnect pending)
        {
            _pending.Remove(pending.DeviceId);
            _links[pending.DeviceId] = pending.Connection;
            UpdateIcon();
            UpdateTip();
            _tray.Balloon("已开始接收音频", pending.Name, false);
        }

        void FailPending(PendingConnect pending, string reason)
        {
            _pending.Remove(pending.DeviceId);
            try
            {
                pending.Connection.Dispose();
            }
            catch (Exception)
            {
            }
            NotifyFailure(pending.Name, reason);
        }

        void NotifyFailure(string name, string reason)
        {
            UpdateIcon();
            UpdateTip();
            _tray.Balloon("无法接收音频", name + "\n" + reason, true);
        }

        void SyncLinks()
        {
            if (_links.Count == 0)
            {
                return;
            }

            List<string> closed = null;
            foreach (KeyValuePair<string, AudioPlaybackConnection> pair in _links)
            {
                bool isClosed;
                try
                {
                    isClosed = pair.Value.State == AudioPlaybackConnectionState.Closed;
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    isClosed = true;
                }
                if (isClosed)
                {
                    if (closed == null)
                    {
                        closed = new List<string>();
                    }
                    closed.Add(pair.Key);
                }
            }

            if (closed == null)
            {
                return;
            }

            for (int i = 0; i < closed.Count; i++)
            {
                AudioPlaybackConnection connection;
                if (_links.TryGetValue(closed[i], out connection))
                {
                    _links.Remove(closed[i]);
                    try
                    {
                        connection.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            UpdateIcon();
            UpdateTip();
        }

        void UpdateIcon()
        {
            Theme theme = Theme.Load();
            int size = (int)Math.Round(16.0 * DpiScaleFor(new Rectangle(0, 0, 1, 1)));
            if (size < 16)
            {
                size = 16;
            }

            bool receiving = _links.Count > 0 || _pending.Count > 0;
            _receiving = receiving;
            Color color = receiving ? theme.Accent : theme.TrayGlyph;
            _tray.SetIcon(TrayIcon.RenderGlyph(Glyphs.Bluetooth, size, color), BuildTip());
        }

        void UpdateTip()
        {
            _tray.SetTip(BuildTip());
        }

        string BuildTip()
        {
            string state;
            if (_pending.Count > 0)
            {
                state = "正在连接…";
            }
            else if (_links.Count == 1)
            {
                state = "正在接收：" + DeviceNameFor(FirstKey(_links));
            }
            else if (_links.Count > 1)
            {
                state = "正在接收 " + _links.Count + " 个设备";
            }
            else
            {
                state = "未连接";
            }
            string tip = DisplayName + " · " + state;
            return tip.Length > 120 ? tip.Substring(0, 120) : tip;
        }

        static string FirstKey(Dictionary<string, AudioPlaybackConnection> map)
        {
            foreach (string key in map.Keys)
            {
                return key;
            }
            return string.Empty;
        }

        string DeviceNameFor(string id)
        {
            List<AudioDevice> devices = _watcher.Snapshot;
            for (int i = 0; i < devices.Count; i++)
            {
                if (string.Equals(devices[i].Id, id, StringComparison.Ordinal))
                {
                    return devices[i].Name;
                }
            }
            return "设备";
        }

        static float DpiScaleFor(Rectangle anchor)
        {
            Native.POINT point = new Native.POINT(anchor.Left + anchor.Width / 2, anchor.Top + anchor.Height / 2);
            IntPtr monitor = Native.MonitorFromPoint(point, Native.MONITOR_DEFAULTTONEAREST);
            int dpiX;
            int dpiY;
            if (Native.GetDpiForMonitor(monitor, Native.MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0 && dpiX > 0)
            {
                return dpiX / 96f;
            }
            return 1f;
        }

        static bool StartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    string value = key.GetValue(RunValueName) as string;
                    if (string.IsNullOrEmpty(value))
                    {
                        return false;
                    }
                    return string.Equals(value.Trim('"'), Program.ExePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void SetStartup(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        return;
                    }
                    if (enabled)
                    {
                        key.SetValue(RunValueName, "\"" + Program.ExePath + "\"", RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(RunValueName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        static void ToggleStartup()
        {
            SetStartup(!StartupEnabled());
        }

        void OpenBluetoothSettings()
        {
            try
            {
                Native.ShellExecuteW(_host.Handle, "open", "ms-settings:bluetooth", null, null, 1);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        void ExitApp()
        {
            foreach (AudioPlaybackConnection connection in new List<AudioPlaybackConnection>(_links.Values))
            {
                try
                {
                    connection.Dispose();
                }
                catch (Exception)
                {
                }
            }
            _links.Clear();

            _tick.Stop();
            _reopen.Stop();
            _watcher.Dispose();
            _tray.Dispose();
            ExitThread();
        }
    }

    /// <summary>Hidden top level window that receives the tray icon and shell broadcasts.</summary>
    internal sealed class HostWindow : NativeWindow
    {
        public Func<Message, bool> Handler;

        public HostWindow()
        {
            CreateParams parameters = new CreateParams();
            parameters.Caption = "SoundFlow";
            parameters.ClassName = "STATIC";
            parameters.Style = unchecked((int)0x80000000);
            parameters.X = 0;
            parameters.Y = 0;
            parameters.Width = 1;
            parameters.Height = 1;
            CreateHandle(parameters);
        }

        protected override void WndProc(ref Message message)
        {
            if (Handler != null && Handler(message))
            {
                return;
            }
            base.WndProc(ref message);
        }
    }
}


