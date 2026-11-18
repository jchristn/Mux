namespace Mux.Core.Theming
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;

    /// <summary>
    /// The built-in color palettes and the rules shared by every surface for resolving, validating, and
    /// mixing palettes. The active palette is <c>colorPalette</c> in <c>settings.json</c>; custom palettes are
    /// <c>customPalettes</c>. Every built-in palette meets WCAG contrast floors: text 7:1 on the background
    /// and raised surfaces, muted text and the accent 4.5:1, text on the accent and on a selection 4.5:1, and
    /// the cursor and status colors 3:1 (checked by the test suite).
    /// </summary>
    public static class ColorPaletteCatalog
    {
        #region Public-Members

        /// <summary>
        /// The default palette id (Mux, dark).
        /// </summary>
        public const string DefaultId = "mux";

        /// <summary>
        /// The light counterpart of the default palette.
        /// </summary>
        public const string LightId = "mux-light";

        /// <summary>
        /// The accessibility palette (white on black, yellow accent).
        /// </summary>
        public const string HighContrastId = "high-contrast";

        /// <summary>
        /// A pseudo-palette for graphical surfaces: follow the operating system's light or dark preference,
        /// resolving to <see cref="DefaultId"/> or <see cref="LightId"/>. The TUI treats it as the default.
        /// </summary>
        public const string SystemId = "system";

        /// <summary>
        /// The longest accepted custom palette id.
        /// </summary>
        public const int MaxIdLength = 48;

        /// <summary>
        /// The built-in palettes, in picker order.
        /// </summary>
        public static IReadOnlyList<ColorPalette> BuiltIn => _BuiltIn;

        #endregion

        #region Private-Members

        private static readonly Regex _IdPattern = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

        private static readonly ColorPalette[] _BuiltIn =
        {
            Define("mux", "Mux", true, "#080d1a #111827 #1e293b #334155 #f1f5f9 #94a3b8 #4ade80 #052e16 #4ade80 #14532d #22c55e #fbbf24 #f87171 #60a5fa"),
            Define("mux-light", "Mux Light", false, "#f8fafc #ffffff #f1f5f9 #cbd5e1 #1e293b #5b6b80 #15803d #ffffff #15803d #bbf7d0 #15803d #b45309 #c81e1e #1d4ed8"),
            Define("midnight", "Midnight", true, "#0b1020 #121a2e #1b2540 #2c3a5e #e6ecff #8f9bc2 #7aa2ff #0b1020 #7aa2ff #26345c #5fd7a7 #f2c46d #ff7a90 #6fd3ff"),
            Define("ocean", "Ocean", true, "#071a24 #0c2330 #12303f #1f4a5e #e0f4fb #86aebd #38bdf8 #03202e #38bdf8 #13405a #4ade80 #fcd34d #fb7185 #67e8f9"),
            Define("aurora", "Aurora", true, "#0f1419 #151c23 #1d2730 #2e3c48 #e6f1f5 #8fa5b2 #4fd6c9 #04211e #b48cff #233d4a #7ee081 #ffd166 #ff7b8a #b48cff"),
            Define("forest", "Forest", true, "#0e1512 #141e1a #1c2923 #2d4238 #e3f1ea #8fae9f #5ee6a8 #062016 #5ee6a8 #1f4434 #5ee6a8 #e9c46a #f28482 #74c0fc"),
            Define("ember", "Ember", true, "#17120f #201915 #2b221c #463629 #f5e9dc #b39d89 #ff8a3d #1a0f07 #ff8a3d #4a2e1b #9ccf6b #ffc857 #ff6b5b #7cb7ff"),
            Define("rosewood", "Rosewood", true, "#1a1016 #22151d #2d1c27 #48303f #f6e6ee #b796a8 #ff7aa8 #2a0a18 #ff7aa8 #4a2238 #8fd694 #ffc978 #ff6b6b #8ab4ff"),
            Define("lavender", "Lavender Dusk", true, "#15121f #1c1829 #262036 #3b3354 #ece8fb #a49cc4 #b9a3ff #1a1033 #b9a3ff #362c5c #8be0b5 #f5cf7a #ff8fa3 #8ec5ff"),
            Define("graphite", "Graphite", true, "#111111 #181818 #222222 #3a3a3a #ececec #9a9a9a #f5b942 #1d1400 #f5b942 #3a3324 #8bd17c #f5b942 #ff6e6e #79b8ff"),
            Define("glacier", "Glacier", false, "#f4f8fb #ffffff #e8f0f6 #c9d8e4 #14263a #52667a #0b6bcb #ffffff #0b6bcb #cfe3f7 #1a7f4b #a15c00 #c5283d #0b6bcb"),
            Define("sandstone", "Sandstone", false, "#f8f3ea #fffcf6 #efe6d6 #dccdb4 #2e261c #6e604c #b4532a #ffffff #b4532a #f0d9c4 #3f7d3a #9a6200 #b3261e #2f6690"),
            Define("meadow", "Meadow", false, "#f3f8f1 #ffffff #e5efe1 #c7d9c0 #1c2a1d #566a57 #2e7d32 #ffffff #2e7d32 #cde8c6 #2e7d32 #9a6700 #c62828 #1565c0"),
            Define("high-contrast", "High Contrast", true, "#000000 #000000 #0a0a0a #ffffff #ffffff #e6e6e6 #ffff00 #000000 #ffff00 #333300 #80ff80 #ffd000 #ff8080 #80c8ff")
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when <paramref name="id"/> names a built-in palette or the <see cref="SystemId"/> pseudo-palette.
        /// </summary>
        /// <param name="id">The id to test (case-insensitive).</param>
        /// <returns>True for a reserved id.</returns>
        public static bool IsReservedId(string? id)
        {
            string normalized = NormalizeId(id);
            if (normalized == SystemId)
            {
                return true;
            }

            foreach (ColorPalette palette in _BuiltIn)
            {
                if (palette.Id == normalized)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Lowercases and trims a palette id.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>The normalized id, or an empty string for null.</returns>
        public static string NormalizeId(string? id)
        {
            return (id ?? string.Empty).Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Every palette a picker should offer: the built-ins in order, then each valid custom palette with its
        /// inherited colors filled in. Invalid custom entries (bad id, duplicate, unknown base) are skipped.
        /// </summary>
        /// <param name="custom">The custom palettes from settings, or null.</param>
        /// <returns>Fully resolved palettes (every color set).</returns>
        public static List<ColorPalette> GetAll(IEnumerable<ColorPalette>? custom)
        {
            List<ColorPalette> result = new List<ColorPalette>();
            foreach (ColorPalette palette in _BuiltIn)
            {
                result.Add(palette.Clone());
            }

            if (custom == null)
            {
                return result;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<ColorPalette> customList = new List<ColorPalette>(custom);
            foreach (ColorPalette entry in customList)
            {
                if (entry == null)
                {
                    continue;
                }

                string id = NormalizeId(entry.Id);
                if (!_IdPattern.IsMatch(id) || id.Length > MaxIdLength || IsReservedId(id) || !seen.Add(id))
                {
                    continue;
                }

                ColorPalette? resolved = ResolveCustom(entry, customList, new HashSet<string>(StringComparer.Ordinal));
                if (resolved != null)
                {
                    result.Add(resolved);
                }
            }

            return result;
        }

        /// <summary>
        /// Finds a palette by id among the built-ins and the custom palettes, fully resolved.
        /// </summary>
        /// <param name="id">The palette id (case-insensitive).</param>
        /// <param name="custom">The custom palettes from settings, or null.</param>
        /// <returns>The palette, or null when no palette has that id (including <see cref="SystemId"/>).</returns>
        public static ColorPalette? Find(string? id, IEnumerable<ColorPalette>? custom)
        {
            string normalized = NormalizeId(id);
            if (normalized.Length == 0)
            {
                return null;
            }

            foreach (ColorPalette palette in GetAll(custom))
            {
                if (palette.Id == normalized)
                {
                    return palette;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves the palette to render: <see cref="SystemId"/> follows <paramref name="systemPrefersDark"/>,
        /// a known id returns that palette, and anything else (null, blank, unknown) returns the default.
        /// </summary>
        /// <param name="id">The requested palette id.</param>
        /// <param name="custom">The custom palettes from settings, or null.</param>
        /// <param name="systemPrefersDark">The operating system's preference, used only for <see cref="SystemId"/>.</param>
        /// <returns>A fully resolved palette; never null.</returns>
        public static ColorPalette Resolve(string? id, IEnumerable<ColorPalette>? custom, bool systemPrefersDark = true)
        {
            string normalized = NormalizeId(id);
            if (normalized == SystemId)
            {
                return Find(systemPrefersDark ? DefaultId : LightId, null)!;
            }

            return Find(normalized, custom) ?? Find(DefaultId, null)!;
        }

        /// <summary>
        /// Maps a legacy theme name to a palette id: the TUI's <c>tuiTheme</c> (<c>mux</c>, <c>dark</c>,
        /// <c>light</c>, <c>highcontrast</c>) and the desktop app's theme mode (<c>system</c>, <c>dark</c>,
        /// <c>light</c>, <c>highcontrast</c>). The TUI's old <c>dark</c> preset becomes Graphite, its closest
        /// palette; the desktop's <c>dark</c> mode was the Mux palette itself.
        /// </summary>
        /// <param name="legacy">The legacy theme name.</param>
        /// <param name="fromTui">True when the name comes from <c>tuiTheme</c>.</param>
        /// <returns>The palette id (or <see cref="SystemId"/>); unknown names return <see cref="DefaultId"/>.</returns>
        public static string FromLegacyTheme(string? legacy, bool fromTui)
        {
            switch (NormalizeId(legacy))
            {
                case "light": return LightId;
                case "highcontrast":
                case "high-contrast": return HighContrastId;
                case "dark": return fromTui ? "graphite" : DefaultId;
                case "system": return fromTui ? DefaultId : SystemId;
                default: return DefaultId;
            }
        }

        /// <summary>
        /// Validates a custom palette before it is saved. The id must be lowercase kebab-case, at most
        /// <see cref="MaxIdLength"/> characters, and not a built-in id or <see cref="SystemId"/>; the name is
        /// required; every color that is set must be a hex color; and the base, when given, must name another
        /// palette (built-in or custom, not itself).
        /// </summary>
        /// <param name="candidate">The palette to validate.</param>
        /// <param name="custom">The other custom palettes (used to resolve a custom base), or null.</param>
        /// <returns>An error message, or null when valid.</returns>
        public static string? Validate(ColorPalette? candidate, IEnumerable<ColorPalette>? custom)
        {
            if (candidate == null)
            {
                return "A palette is required.";
            }

            string id = NormalizeId(candidate.Id);
            if (id.Length == 0)
            {
                return "Palette id is required.";
            }

            if (id.Length > MaxIdLength || !_IdPattern.IsMatch(id))
            {
                return "Palette id must be lowercase letters, digits, and single hyphens (kebab-case), at most " + MaxIdLength + " characters.";
            }

            if (IsReservedId(id))
            {
                return "'" + id + "' is a built-in palette id; choose another id.";
            }

            if (string.IsNullOrWhiteSpace(candidate.Name))
            {
                return "Palette name is required.";
            }

            ColorPaletteColors colors = candidate.Colors ?? new ColorPaletteColors();
            foreach (string role in ColorPaletteColors.RoleNames)
            {
                string? value = colors.Get(role);
                if (!string.IsNullOrWhiteSpace(value) && !TryNormalizeHex(value, out _))
                {
                    return "Color '" + role + "' must be a hex color such as #1e293b (got '" + value + "').";
                }
            }

            if (!string.IsNullOrWhiteSpace(candidate.Base))
            {
                string baseId = NormalizeId(candidate.Base);
                if (baseId == id)
                {
                    return "A palette cannot use itself as its base.";
                }

                bool known = IsReservedId(baseId) && baseId != SystemId;
                if (!known && custom != null)
                {
                    foreach (ColorPalette other in custom)
                    {
                        if (other != null && NormalizeId(other.Id) == baseId)
                        {
                            known = true;
                            break;
                        }
                    }
                }

                if (!known)
                {
                    return "Base palette '" + candidate.Base + "' does not exist.";
                }
            }

            return null;
        }

        /// <summary>
        /// Normalizes a hex color to lowercase <c>#rrggbb</c>. Accepts <c>#rgb</c>, <c>#rrggbb</c>, and either
        /// form without the leading <c>#</c>.
        /// </summary>
        /// <param name="value">The color text.</param>
        /// <param name="normalized">The normalized color, or an empty string on failure.</param>
        /// <returns>True when the value is a hex color.</returns>
        public static bool TryNormalizeHex(string? value, out string normalized)
        {
            normalized = string.Empty;
            string text = (value ?? string.Empty).Trim();
            if (text.StartsWith("#", StringComparison.Ordinal))
            {
                text = text.Substring(1);
            }

            if (text.Length == 3)
            {
                text = new string(new[] { text[0], text[0], text[1], text[1], text[2], text[2] });
            }

            if (text.Length != 6)
            {
                return false;
            }

            foreach (char c in text)
            {
                if (!Uri.IsHexDigit(c))
                {
                    return false;
                }
            }

            normalized = "#" + text.ToLowerInvariant();
            return true;
        }

        /// <summary>
        /// Parses a hex color into its red, green, and blue channels.
        /// </summary>
        /// <param name="hex">The color (any form <see cref="TryNormalizeHex"/> accepts).</param>
        /// <param name="r">Red.</param>
        /// <param name="g">Green.</param>
        /// <param name="b">Blue.</param>
        /// <returns>True when parsed.</returns>
        public static bool TryParseRgb(string? hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (!TryNormalizeHex(hex, out string normalized))
            {
                return false;
            }

            r = byte.Parse(normalized.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            g = byte.Parse(normalized.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            b = byte.Parse(normalized.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>
        /// Blends two colors in sRGB: <paramref name="amount"/> 0 returns <paramref name="from"/>, 1 returns
        /// <paramref name="to"/>. Surfaces use it to derive secondary shades (hover, soft accent) from a palette.
        /// </summary>
        /// <param name="from">The starting color.</param>
        /// <param name="to">The target color.</param>
        /// <param name="amount">The blend amount, clamped to 0–1.</param>
        /// <returns>The blended color as <c>#rrggbb</c>; <paramref name="from"/> when either color is invalid.</returns>
        public static string Mix(string from, string to, double amount)
        {
            if (!TryParseRgb(from, out byte r1, out byte g1, out byte b1) || !TryParseRgb(to, out byte r2, out byte g2, out byte b2))
            {
                return from;
            }

            double t = Math.Clamp(amount, 0.0, 1.0);
            int r = (int)Math.Round(r1 + ((r2 - r1) * t));
            int g = (int)Math.Round(g1 + ((g2 - g1) * t));
            int b = (int)Math.Round(b1 + ((b2 - b1) * t));
            return "#" + r.ToString("x2", CultureInfo.InvariantCulture) + g.ToString("x2", CultureInfo.InvariantCulture) + b.ToString("x2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The WCAG 2 contrast ratio between two colors (1 to 21).
        /// </summary>
        /// <param name="first">One color.</param>
        /// <param name="second">The other color.</param>
        /// <returns>The ratio, or 1 when either color is invalid.</returns>
        public static double ContrastRatio(string first, string second)
        {
            if (!TryParseRgb(first, out byte r1, out byte g1, out byte b1) || !TryParseRgb(second, out byte r2, out byte g2, out byte b2))
            {
                return 1.0;
            }

            double l1 = RelativeLuminance(r1, g1, b1);
            double l2 = RelativeLuminance(r2, g2, b2);
            double lighter = Math.Max(l1, l2);
            double darker = Math.Min(l1, l2);
            return (lighter + 0.05) / (darker + 0.05);
        }

        #endregion

        #region Private-Methods

        private static ColorPalette Define(string id, string name, bool dark, string colors)
        {
            string[] values = colors.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != ColorPaletteColors.RoleNames.Count)
            {
                throw new InvalidOperationException("Palette '" + id + "' must define " + ColorPaletteColors.RoleNames.Count + " colors.");
            }

            ColorPaletteColors set = new ColorPaletteColors();
            for (int i = 0; i < values.Length; i++)
            {
                set.Set(ColorPaletteColors.RoleNames[i], values[i]);
            }

            return new ColorPalette { Id = id, Name = name, Dark = dark, Colors = set, BuiltIn = true };
        }

        private static ColorPalette? ResolveCustom(ColorPalette entry, List<ColorPalette> customList, HashSet<string> visiting)
        {
            string id = NormalizeId(entry.Id);
            if (!visiting.Add(id))
            {
                return null; // a base cycle
            }

            string baseId = string.IsNullOrWhiteSpace(entry.Base) ? (entry.Dark ? DefaultId : LightId) : NormalizeId(entry.Base);
            ColorPalette? basePalette = null;
            foreach (ColorPalette builtIn in _BuiltIn)
            {
                if (builtIn.Id == baseId)
                {
                    basePalette = builtIn;
                    break;
                }
            }

            if (basePalette == null)
            {
                foreach (ColorPalette other in customList)
                {
                    if (other != null && NormalizeId(other.Id) == baseId && baseId != id)
                    {
                        basePalette = ResolveCustom(other, customList, visiting);
                        break;
                    }
                }
            }

            if (basePalette == null)
            {
                return null;
            }

            ColorPaletteColors source = entry.Colors ?? new ColorPaletteColors();
            ColorPaletteColors merged = new ColorPaletteColors();
            foreach (string role in ColorPaletteColors.RoleNames)
            {
                string? own = source.Get(role);
                merged.Set(role, TryNormalizeHex(own, out string normalized) ? normalized : basePalette.Colors.Get(role));
            }

            return new ColorPalette
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(entry.Name) ? id : entry.Name.Trim(),
                Dark = entry.Dark,
                Base = baseId,
                Colors = merged,
                BuiltIn = false
            };
        }

        private static double RelativeLuminance(byte r, byte g, byte b)
        {
            return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));
        }

        private static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        #endregion
    }
}
