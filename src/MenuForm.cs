using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SoundFlow
{
    internal sealed class MenuItem
    {
        public string Text = string.Empty;
        public string Glyph;
        public string RightText;
        public bool Checked;
        public bool Enabled = true;
        public bool Separator;
        public Action Click;

        public static MenuItem SeparatorItem()
        {
            return new MenuItem { Separator = true };
        }
    }

    /// <summary>
    /// A borderless popup that renders like the Windows 11 shell context menu:
    /// rounded corners and the soft shadow come from DWM, the content is owner drawn.
    /// </summary>
    internal sealed class MenuForm : Form
    {
        readonly List<MenuItem> _items;
        readonly Theme _theme;
        readonly float _scale;
        readonly Font _font;
        readonly Font _glyphFont;
        readonly int _pad;
        readonly int _itemHeight;
        readonly int _iconSize;
        readonly int _iconLeft;
        readonly int _textLeft;
        readonly int _rightPad;
        readonly int _separatorHeight;
        readonly int _cornerRadius;
        readonly int _rightReserve;
        readonly int[] _tops;

        int _hover = -1;
        bool _activated;
        bool _closing;

        public MenuItem Selected;

        public MenuForm(List<MenuItem> items, Theme theme, float scale)
        {
            _items = items;
            _theme = theme;
            _scale = scale <= 0f ? 1f : scale;

            _pad = S(4);
            _itemHeight = S(32);
            _iconSize = S(16);
            _iconLeft = S(12);
            _textLeft = S(38);
            _rightPad = S(14);
            _separatorHeight = S(11);
            _cornerRadius = S(8);

            _font = new Font(Theme.TextFamily, F(12f), FontStyle.Regular, GraphicsUnit.Pixel);
            _glyphFont = new Font(Theme.GlyphFamily, F(16f), FontStyle.Regular, GraphicsUnit.Pixel);

            int widestRight = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Separator || string.IsNullOrEmpty(_items[i].RightText))
                {
                    continue;
                }
                widestRight = Math.Max(widestRight, Measure(_items[i].RightText));
            }
            _rightReserve = widestRight > 0 ? widestRight + S(16) : 0;

            int widest = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Separator)
                {
                    continue;
                }
                widest = Math.Max(widest, _textLeft + Measure(_items[i].Text) + _rightPad + _rightReserve);
            }
            int width = Math.Max(S(212), widest + _pad * 2);
            width = Math.Min(width, S(460));

            _tops = new int[_items.Count];
            int y = _pad;
            for (int i = 0; i < _items.Count; i++)
            {
                _tops[i] = y;
                y += _items[i].Separator ? _separatorHeight : _itemHeight;
            }

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            BackColor = _theme.MenuBg;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            ClientSize = new Size(width, y + _pad);
        }

        int S(int value)
        {
            return (int)Math.Round(value * _scale);
        }

        float F(float value)
        {
            return value * _scale;
        }

        int Measure(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            return TextRenderer.MeasureText(
                text,
                _font,
                new Size(int.MaxValue, _itemHeight),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }

        /// <summary>Places the flyout above the tray icon and inside the work area.</summary>
        public void PlaceNear(Rectangle anchor)
        {
            Screen screen = Screen.FromRectangle(anchor);
            Rectangle work = screen.WorkingArea;
            int gap = S(6);

            int x = anchor.Left + (anchor.Width - Width) / 2;
            int y = anchor.Top - Height - gap;

            if (y < work.Top + gap)
            {
                y = anchor.Bottom + gap;
            }
            if (y + Height > work.Bottom - gap)
            {
                y = Math.Max(work.Top + gap, work.Bottom - Height - gap);
            }
            x = Math.Max(work.Left + gap, Math.Min(x, work.Right - Width - gap));

            Location = new Point(x, y);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: keep out of Alt+Tab
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            int preference = Native.DWMWCP_ROUND;
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

            Native.MARGINS margins = new Native.MARGINS();
            margins.Left = 1;
            margins.Right = 1;
            margins.Top = 1;
            margins.Bottom = 1;
            Native.DwmExtendFrameIntoClientArea(Handle, ref margins);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Native.SetForegroundWindow(Handle);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            _activated = true;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_activated)
            {
                Dismiss();
            }
        }

        void Dismiss()
        {
            if (_closing)
            {
                return;
            }
            _closing = true;
            Selected = null;
            Close();
        }

        void Choose(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                return;
            }
            MenuItem item = _items[index];
            if (item.Separator || !item.Enabled)
            {
                return;
            }
            if (_closing)
            {
                return;
            }
            _closing = true;
            Selected = item;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush background = new SolidBrush(_theme.MenuBg))
            {
                graphics.FillRectangle(background, ClientRectangle);
            }

            using (GraphicsPath border = RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), _cornerRadius))
            using (Pen pen = new Pen(_theme.MenuBorder))
            {
                graphics.DrawPath(pen, border);
            }

            for (int i = 0; i < _items.Count; i++)
            {
                MenuItem item = _items[i];
                if (item.Separator)
                {
                    int lineY = _tops[i] + _separatorHeight / 2;
                    using (SolidBrush brush = new SolidBrush(_theme.Separator))
                    {
                        graphics.FillRectangle(brush, _pad + S(12), lineY, Width - 2 * (_pad + S(12)), Math.Max(1, S(1)));
                    }
                    continue;
                }

                Rectangle bounds = new Rectangle(_pad, _tops[i], Width - 2 * _pad, _itemHeight);
                if (i == _hover && item.Enabled)
                {
                    using (GraphicsPath hoverPath = RoundedRect(bounds, S(4)))
                    using (SolidBrush hoverBrush = new SolidBrush(_theme.Hover))
                    {
                        graphics.FillPath(hoverBrush, hoverPath);
                    }
                }

                Color textColor = item.Enabled ? _theme.Text : _theme.DisabledText;
                Color glyphColor = item.Enabled ? _theme.Icon : _theme.DisabledText;
                if (item.Checked)
                {
                    glyphColor = _theme.Accent;
                }

                string glyph = item.Checked ? Glyphs.Check : item.Glyph;
                if (!string.IsNullOrEmpty(glyph))
                {
                    Rectangle iconBounds = new Rectangle(
                        _iconLeft,
                        _tops[i] + (_itemHeight - _iconSize) / 2,
                        _iconSize,
                        _iconSize);
                    TextRenderer.DrawText(
                        graphics,
                        glyph,
                        _glyphFont,
                        iconBounds,
                        glyphColor,
                        TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding |
                        TextFormatFlags.SingleLine);
                }

                int textWidth = Width - _textLeft - _rightPad - _rightReserve;
                Rectangle textBounds = new Rectangle(_textLeft, _tops[i], textWidth, _itemHeight);
                TextRenderer.DrawText(
                    graphics,
                    item.Text,
                    _font,
                    textBounds,
                    textColor,
                    TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix |
                    TextFormatFlags.NoPadding);

                if (!string.IsNullOrEmpty(item.RightText))
                {
                    Rectangle rightBounds = new Rectangle(
                        _pad,
                        _tops[i],
                        Width - _pad - _rightPad,
                        _itemHeight);
                    TextRenderer.DrawText(
                        graphics,
                        item.RightText,
                        _font,
                        rightBounds,
                        _theme.SecondaryText,
                        TextFormatFlags.Right |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine |
                        TextFormatFlags.NoPrefix |
                        TextFormatFlags.NoPadding);
                }
            }
        }

        static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            return RoundedRect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius);
        }

        static GraphicsPath RoundedRect(RectangleF bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Max(1f, radius * 2f);
            if (bounds.Width <= diameter || bounds.Height <= diameter)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        int HitTest(Point point)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Separator || !_items[i].Enabled)
                {
                    continue;
                }
                if (new Rectangle(_pad, _tops[i], Width - 2 * _pad, _itemHeight).Contains(point))
                {
                    return i;
                }
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hover = HitTest(e.Location);
            if (hover != _hover)
            {
                _hover = hover;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                Dismiss();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            int index = HitTest(e.Location);
            if (index >= 0)
            {
                Choose(index);
            }
        }

        void MoveHover(int delta)
        {
            for (int step = 1; step <= _items.Count; step++)
            {
                int index = _hover + delta * step;
                if (index < 0)
                {
                    index = delta < 0 ? _items.Count - 1 : 0;
                }
                if (index >= _items.Count)
                {
                    index = delta > 0 ? 0 : _items.Count - 1;
                }
                if (!_items[index].Separator && _items[index].Enabled && index != _hover)
                {
                    _hover = index;
                    Invalidate();
                    return;
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    Dismiss();
                    return true;
                case Keys.Up:
                    MoveHover(-1);
                    return true;
                case Keys.Down:
                    MoveHover(1);
                    return true;
                case Keys.Home:
                    _hover = -1;
                    MoveHover(1);
                    return true;
                case Keys.End:
                    _hover = -1;
                    MoveHover(-1);
                    return true;
                case Keys.Enter:
                case Keys.Space:
                    if (_hover >= 0)
                    {
                        Choose(_hover);
                    }
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal static class Glyphs
    {
        public const string Check = "\uE73E";
        public const string Bluetooth = "\uE702";
        public const string Phone = "\uE8EA";
        public const string Refresh = "\uE72C";
        public const string Settings = "\uE713";
        public const string Power = "\uE7E8";
    }
}

