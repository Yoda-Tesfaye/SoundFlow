using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using Microsoft.Win32;

namespace SoundFlow
{
    /// <summary>Colors and fonts that follow the Windows 11 shell look.</summary>
    internal sealed class Theme
    {
        const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string DwmKey = @"Software\Microsoft\Windows\DWM";

        public bool Dark;
        public Color MenuBg;
        public Color MenuBorder;
        public Color Hover;
        public Color Text;
        public Color DisabledText;
        public Color SecondaryText;
        public Color Separator;
        public Color Icon;
        public Color Accent;
        public Color TrayGlyph;

        static HashSet<string> _families;
        static string _textFamily;
        static string _glyphFamily;

        public static string TextFamily
        {
            get
            {
                if (_textFamily == null)
                {
                    _textFamily = HasFamily("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
                }
                return _textFamily;
            }
        }

        public static string GlyphFamily
        {
            get
            {
                if (_glyphFamily == null)
                {
                    _glyphFamily = HasFamily("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
                }
                return _glyphFamily;
            }
        }

        static bool HasFamily(string name)
        {
            if (_families == null)
            {
                _families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (InstalledFontCollection fonts = new InstalledFontCollection())
                {
                    foreach (FontFamily family in fonts.Families)
                    {
                        _families.Add(family.Name);
                    }
                }
            }
            return _families.Contains(name);
        }

        public static Theme Load()
        {
            bool appsLight = ReadDword(PersonalizeKey, "AppsUseLightTheme", 1) != 0;
            bool systemLight = ReadDword(PersonalizeKey, "SystemUsesLightTheme", 1) != 0;

            int accentRaw = ReadDword(DwmKey, "AccentColor", unchecked((int)0xFFBD6C0F));
            Color accent = Color.FromArgb(
                accentRaw & 0xFF,
                (accentRaw >> 8) & 0xFF,
                (accentRaw >> 16) & 0xFF);

            Theme theme = new Theme();
            theme.Dark = !appsLight;
            theme.TrayGlyph = systemLight ? Color.FromArgb(26, 26, 26) : Color.White;

            if (theme.Dark)
            {
                theme.MenuBg = Color.FromArgb(43, 43, 43);
                theme.MenuBorder = Color.FromArgb(58, 58, 58);
                theme.Hover = Color.FromArgb(61, 61, 61);
                theme.Text = Color.White;
                theme.DisabledText = Color.FromArgb(138, 138, 138);
                theme.SecondaryText = Color.FromArgb(178, 178, 178);
                theme.Separator = Color.FromArgb(63, 63, 63);
                theme.Icon = Color.FromArgb(222, 222, 222);
                theme.Accent = ReadableAccent(accent, true);
            }
            else
            {
                theme.MenuBg = Color.FromArgb(249, 249, 249);
                theme.MenuBorder = Color.FromArgb(228, 228, 228);
                theme.Hover = Color.FromArgb(237, 237, 237);
                theme.Text = Color.FromArgb(27, 27, 27);
                theme.DisabledText = Color.FromArgb(154, 154, 154);
                theme.SecondaryText = Color.FromArgb(96, 96, 96);
                theme.Separator = Color.FromArgb(231, 231, 231);
                theme.Icon = Color.FromArgb(74, 74, 74);
                theme.Accent = ReadableAccent(accent, false);
            }

            return theme;
        }

        /// <summary>
        /// The shell accent colour can be very pale (or very dark). Clamp its lightness and
        /// keep some saturation so check marks stay readable on both menu backgrounds.
        /// </summary>
        static Color ReadableAccent(Color color, bool dark)
        {
            float h;
            float s;
            float l;
            ToHsl(color, out h, out s, out l);

            if (dark)
            {
                if (l < 0.68f)
                {
                    l = 0.68f;
                }
            }
            else
            {
                if (l > 0.38f)
                {
                    l = 0.38f;
                }
            }
            if (s < 0.45f)
            {
                s = 0.45f;
            }

            return FromHsl(h, s, l);
        }

        static void ToHsl(Color color, out float h, out float s, out float l)
        {
            float r = color.R / 255f;
            float g = color.G / 255f;
            float b = color.B / 255f;
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;

            l = (max + min) / 2f;
            if (delta < 0.0001f)
            {
                h = 0f;
                s = 0f;
                return;
            }

            s = l > 0.5f ? delta / (2f - max - min) : delta / (max + min);

            if (max == r)
            {
                h = (g - b) / delta + (g < b ? 6f : 0f);
            }
            else if (max == g)
            {
                h = (b - r) / delta + 2f;
            }
            else
            {
                h = (r - g) / delta + 4f;
            }
            h /= 6f;
        }

        static Color FromHsl(float h, float s, float l)
        {
            if (s < 0.0001f)
            {
                int gray = (int)Math.Round(l * 255f);
                return Color.FromArgb(gray, gray, gray);
            }

            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;
            return Color.FromArgb(
                (int)Math.Round(HueToRgb(p, q, h + 1f / 3f) * 255f),
                (int)Math.Round(HueToRgb(p, q, h) * 255f),
                (int)Math.Round(HueToRgb(p, q, h - 1f / 3f) * 255f));
        }

        static float HueToRgb(float p, float q, float t)
        {
            if (t < 0f)
            {
                t += 1f;
            }
            if (t > 1f)
            {
                t -= 1f;
            }
            if (t < 1f / 6f)
            {
                return p + (q - p) * 6f * t;
            }
            if (t < 0.5f)
            {
                return q;
            }
            if (t < 2f / 3f)
            {
                return p + (q - p) * (2f / 3f - t) * 6f;
            }
            return p;
        }

        static int ReadDword(string subKey, string name, int fallback)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey))
                {
                    if (key == null)
                    {
                        return fallback;
                    }
                    object value = key.GetValue(name);
                    if (value is int)
                    {
                        return (int)value;
                    }
                }
            }
            catch (Exception)
            {
            }
            return fallback;
        }
    }
}

