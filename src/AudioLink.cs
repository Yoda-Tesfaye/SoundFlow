using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Media.Audio;

namespace SoundFlow
{
    /// <summary>A device that can send its audio to this PC (phone, player ...).</summary>
    internal sealed class AudioDevice
    {
        public string Id;
        public string Name;
        public string Mac; // 12 hex digits, upper case, may be null
    }

    /// <summary>Thin wrapper around the Windows "Bluetooth audio receive" (A2DP sink) API.</summary>
    internal static class AudioLink
    {
        static readonly Regex MacPattern = new Regex(@"&0&([0-9A-Fa-f]{12})", RegexOptions.Compiled);
        static readonly Dictionary<string, string> NameCache = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly object NameCacheSync = new object();
        static string _selector;

        /// <summary>
        /// Selector for Bluetooth devices that expose an audio source: those are the devices
        /// a phone streams music from, so connecting to them makes this PC the receiver.
        /// </summary>
        public static string Selector
        {
            get
            {
                if (_selector == null)
                {
                    _selector = AudioPlaybackConnection.GetDeviceSelector();
                }
                return _selector;
            }
        }

        public static bool IsSupported()
        {
            try
            {
                string ignored = Selector;
                return ignored != null && ignored.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static List<AudioDevice> Enumerate(int timeoutMs)
        {
            List<AudioDevice> result = new List<AudioDevice>();
            IAsyncOperation<DeviceInformationCollection> operation = DeviceInformation.FindAllAsync(Selector);
            WaitFor(operation, timeoutMs);

            DeviceInformationCollection collection = operation.GetResults();
            for (int i = 0; i < collection.Count; i++)
            {
                DeviceInformation info = collection[i];
                string mac = ParseMac(info.Id);
                result.Add(new AudioDevice { Id = info.Id, Mac = mac, Name = ResolveName(info, mac) });
            }
            return result;
        }

        /// <summary>
        /// The interface name Windows reports for an A2DP source is not always the remote
        /// name: a phone that also advertises a randomised Bluetooth LE name shows up as
        /// e.g. "NGMB40c9FSJYx4567IBBh9lC0". The real name lives on the classic Bluetooth
        /// device object, so ask that one first and fall back to a readable placeholder.
        /// </summary>
        static string ResolveName(DeviceInformation info, string mac)
        {
            if (!string.IsNullOrEmpty(mac))
            {
                lock (NameCacheSync)
                {
                    string cached;
                    if (NameCache.TryGetValue(mac, out cached) && !string.IsNullOrEmpty(cached))
                    {
                        return cached;
                    }
                }
            }

            string name = null;

            if (!string.IsNullOrEmpty(mac))
            {
                BluetoothDevice device = null;
                try
                {
                    IAsyncOperation<BluetoothDevice> operation =
                        BluetoothDevice.FromBluetoothAddressAsync(Convert.ToUInt64(mac, 16));
                    WaitFor(operation, 1500);
                    device = operation.GetResults();
                    if (device != null)
                    {
                        name = device.Name;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
                finally
                {
                    DisposeQuietly(device);
                }
            }

            name = SanitizeName(name);
            if (name.Length == 0)
            {
                name = SanitizeName(info.Name);
            }
            if (name.Length == 0 || LooksGenerated(name))
            {
                string tail = MacTail(mac);
                name = tail.Length == 0 ? "蓝牙设备" : "蓝牙设备 " + tail;
            }

            if (!string.IsNullOrEmpty(mac))
            {
                lock (NameCacheSync)
                {
                    NameCache[mac] = name;
                }
            }
            return name;
        }

        /// <summary>Forgets resolved names so the next enumeration asks the devices again.</summary>
        public static void ClearNameCache()
        {
            lock (NameCacheSync)
            {
                NameCache.Clear();
            }
        }

        static string ParseMac(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return null;
            }
            Match match = MacPattern.Match(deviceId);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }

        static string MacTail(string mac)
        {
            if (string.IsNullOrEmpty(mac) || mac.Length < 4)
            {
                return string.Empty;
            }
            return mac.Substring(mac.Length - 4);
        }

        /// <summary>
        /// Bluetooth names are free form and often contain emoji, which GDI text rendering
        /// turns into replacement boxes. Keep the readable characters only.
        /// </summary>
        static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
                    {
                        i++;
                    }
                    continue;
                }
                if (char.IsLowSurrogate(c) || char.IsControl(c))
                {
                    continue;
                }
                if (c == '\u200B' || c == '\u200D' || c == '\uFE0E' || c == '\uFE0F')
                {
                    continue;
                }
                builder.Append(c);
            }

            return builder.ToString().Trim();
        }

        /// <summary>Detects Windows' randomised placeholder names (24 mixed-case alphanumerics).</summary>
        static bool LooksGenerated(string name)
        {
            if (name.Length < 20)
            {
                return false;
            }
            bool digits = false;
            bool upper = false;
            bool lower = false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c >= '0' && c <= '9')
                {
                    digits = true;
                }
                else if (c >= 'A' && c <= 'Z')
                {
                    upper = true;
                }
                else if (c >= 'a' && c <= 'z')
                {
                    lower = true;
                }
                else
                {
                    return false;
                }
            }
            return digits && upper && lower;
        }

        public static void WaitFor(IAsyncInfo info, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (info.Status == AsyncStatus.Started)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException();
                }
                Thread.Sleep(10);
            }
            if (info.Status == AsyncStatus.Error)
            {
                throw info.ErrorCode;
            }
            if (info.Status == AsyncStatus.Canceled)
            {
                throw new OperationCanceledException();
            }
        }

        static void DisposeQuietly(object instance)
        {
            IDisposable disposable = instance as IDisposable;
            if (disposable == null)
            {
                return;
            }
            try
            {
                disposable.Dispose();
            }
            catch (Exception)
            {
            }
        }

        public static string Describe(Exception ex)
        {
            if (ex == null)
            {
                return "未知错误";
            }
            return string.Format("0x{0:X8}", ex.HResult);
        }

        public static string OpenResultText(AudioPlaybackConnectionOpenResultStatus status)
        {
            switch (status)
            {
                case AudioPlaybackConnectionOpenResultStatus.RequestTimedOut:
                    return "请求超时，请确认对方设备在附近并已开启蓝牙";
                case AudioPlaybackConnectionOpenResultStatus.DeniedBySystem:
                    return "操作被系统拒绝";
                case AudioPlaybackConnectionOpenResultStatus.UnknownFailure:
                    return "连接失败";
                default:
                    return "连接失败";
            }
        }
    }

    /// <summary>
    /// Keeps a fresh list of audio source devices on a background thread so opening the menu
    /// never waits for the Bluetooth stack. The periodic refresh is paused while audio is
    /// being received: touching the Bluetooth interfaces can suspend a running A2DP stream.
    /// </summary>
    internal sealed class DeviceWatcher : IDisposable
    {
        readonly object _sync = new object();
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        readonly Thread _thread;
        volatile bool _stopped;
        List<AudioDevice> _items = new List<AudioDevice>();

        public DeviceWatcher()
        {
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "AudioDeviceWatcher";
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        /// <summary>When it returns true the regular refresh interval is skipped.</summary>
        public Func<bool> PauseRefresh;

        public List<AudioDevice> Snapshot
        {
            get
            {
                lock (_sync)
                {
                    return new List<AudioDevice>(_items);
                }
            }
        }

        public void Refresh()
        {
            _wake.Set();
        }

        void Run()
        {
            Native.CoInitializeEx(IntPtr.Zero, 0);
            try
            {
                bool skip = false;
                while (!_stopped)
                {
                    if (!skip)
                    {
                        EnumerateOnce();
                    }
                    skip = false;

                    bool woken = _wake.WaitOne(8000);
                    if (_stopped)
                    {
                        break;
                    }

                    Func<bool> pause = PauseRefresh;
                    if (!woken && pause != null && pause())
                    {
                        skip = true;
                    }
                }
            }
            finally
            {
                Native.CoUninitialize();
            }
        }

        void EnumerateOnce()
        {
            try
            {
                List<AudioDevice> devices = AudioLink.Enumerate(4000);
                lock (_sync)
                {
                    _items = devices;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        public void Dispose()
        {
            _stopped = true;
            _wake.Set();
        }
    }

    internal static class Log
    {
        static readonly object Sync = new object();

        public static string FilePath
        {
            get
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SoundFlow");
                return System.IO.Path.Combine(dir, "app.log");
            }
        }

        public static void Error(Exception ex)
        {
            if (ex == null)
            {
                return;
            }
            Error(ex.ToString());
        }

        public static void Error(string message)
        {
            try
            {
                lock (Sync)
                {
                    string path = FilePath;
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    System.IO.FileInfo file = new System.IO.FileInfo(path);
                    if (file.Exists && file.Length > 256 * 1024)
                    {
                        file.Delete();
                    }
                    System.IO.File.AppendAllText(
                        path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}


