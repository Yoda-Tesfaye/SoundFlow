using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SoundFlow
{
    /// <summary>Shell_NotifyIcon wrapper plus the monochrome tray glyphs.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        const int IconId = 1;

        readonly IntPtr _hwnd;
        readonly int _callbackMessage;
        IntPtr _icon;
        bool _installed;

        public TrayIcon(IntPtr hwnd, int callbackMessage)
        {
            _hwnd = hwnd;
            _callbackMessage = callbackMessage;
        }

        public void Install(IntPtr icon, string tip)
        {
            _icon = icon;
            Native.NOTIFYICONDATA data = CreateData();
            data.uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP;
            data.hIcon = icon;
            data.szTip = tip;

            if (Native.Shell_NotifyIconW(Native.NIM_ADD, ref data))
            {
                _installed = true;
                data.uTimeoutOrVersion = Native.NOTIFYICON_VERSION_4;
                Native.Shell_NotifyIconW(Native.NIM_SETVERSION, ref data);
            }
        }

        public void ReInstall(string tip)
        {
            _installed = false;
            Install(_icon, tip);
        }

        public void SetIcon(IntPtr icon, string tip)
        {
            if (icon == IntPtr.Zero)
            {
                icon = _icon;
            }
            if (_icon != IntPtr.Zero && _icon != icon)
            {
                Native.DestroyIcon(_icon);
            }
            _icon = icon;

            Native.NOTIFYICONDATA data = CreateData();
            data.uFlags = Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP;
            data.hIcon = icon;
            data.szTip = tip;
            if (!Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref data) && !_installed)
            {
                Install(icon, tip);
            }
        }

        public void SetTip(string tip)
        {
            if (!_installed)
            {
                return;
            }
            Native.NOTIFYICONDATA data = CreateData();
            data.uFlags = Native.NIF_TIP | Native.NIF_SHOWTIP;
            data.szTip = tip;
            Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref data);
        }

        public bool TryGetRect(out Rectangle rect)
        {
            Native.NOTIFYICONIDENTIFIER identifier = new Native.NOTIFYICONIDENTIFIER();
            identifier.cbSize = Marshal.SizeOf(typeof(Native.NOTIFYICONIDENTIFIER));
            identifier.hWnd = _hwnd;
            identifier.uID = IconId;

            Native.RECT native;
            if (Native.Shell_NotifyIconGetRect(ref identifier, out native) == 0)
            {
                rect = new Rectangle(
                    native.Left,
                    native.Top,
                    native.Right - native.Left,
                    native.Bottom - native.Top);
                return rect.Width > 1 && rect.Height > 1;
            }
            rect = Rectangle.Empty;
            return false;
        }

        public void Balloon(string title, string text, bool error)
        {
            if (!_installed)
            {
                return;
            }
            Native.NOTIFYICONDATA data = CreateData();
            data.uFlags = Native.NIF_INFO;
            data.szInfo = text;
            data.szInfoTitle = title;
            data.dwInfoFlags = error ? Native.NIIF_ERROR : Native.NIIF_INFO;
            data.uTimeoutOrVersion = 0;
            Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref data);
        }

        public void Remove()
        {
            if (!_installed)
            {
                return;
            }
            Native.NOTIFYICONDATA data = CreateData();
            Native.Shell_NotifyIconW(Native.NIM_DELETE, ref data);
            _installed = false;
        }

        Native.NOTIFYICONDATA CreateData()
        {
            Native.NOTIFYICONDATA data = new Native.NOTIFYICONDATA();
            data.cbSize = Marshal.SizeOf(typeof(Native.NOTIFYICONDATA));
            data.hWnd = _hwnd;
            data.uID = IconId;
            data.uCallbackMessage = _callbackMessage;
            return data;
        }

        /// <summary>Rasterises a Segoe Fluent Icons glyph into a tray sized HICON.</summary>
        public static IntPtr RenderGlyph(string glyph, int pixelSize, Color color)
        {
            if (pixelSize < 8)
            {
                pixelSize = 8;
            }

            using (Bitmap bitmap = new Bitmap(pixelSize, pixelSize, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    using (Font font = new Font(Theme.GlyphFamily, pixelSize * 0.94f, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (SolidBrush brush = new SolidBrush(color))
                    using (StringFormat format = new StringFormat(StringFormatFlags.NoWrap))
                    {
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        graphics.DrawString(glyph, font, brush, new RectangleF(0, 0, pixelSize, pixelSize), format);
                    }
                }
                return bitmap.GetHicon();
            }
        }

        public void Dispose()
        {
            Remove();
            if (_icon != IntPtr.Zero)
            {
                Native.DestroyIcon(_icon);
                _icon = IntPtr.Zero;
            }
        }
    }
}

