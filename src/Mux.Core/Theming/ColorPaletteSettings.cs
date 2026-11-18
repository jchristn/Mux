namespace Mux.Core.Theming
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Models;
    using Mux.Core.Settings;

    /// <summary>
    /// Reads and writes the palette choice and custom palettes in <c>settings.json</c>, so every surface that
    /// changes them goes through the same validation and lands in the same file.
    /// </summary>
    public static class ColorPaletteSettings
    {
        #region Public-Methods

        /// <summary>
        /// The palette id the TUI should render: <c>colorPalette</c> when set (with <c>system</c> read as the
        /// default, since a terminal cannot follow the OS), otherwise the legacy <c>tuiTheme</c> mapped to a
        /// palette.
        /// </summary>
        /// <param name="settings">The loaded settings.</param>
        /// <returns>A palette id.</returns>
        public static string EffectiveTuiId(MuxSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!string.IsNullOrEmpty(settings.ColorPalette))
            {
                return settings.ColorPalette == ColorPaletteCatalog.SystemId ? ColorPaletteCatalog.DefaultId : settings.ColorPalette!;
            }

            return ColorPaletteCatalog.FromLegacyTheme(settings.TuiTheme, fromTui: true);
        }

        /// <summary>
        /// Sets the active palette and saves settings. The id must be a known palette or <c>system</c>.
        /// </summary>
        /// <param name="id">The palette id.</param>
        /// <returns>The saved id.</returns>
        /// <exception cref="ArgumentException">Thrown when the id names no palette.</exception>
        public static string SetActive(string id)
        {
            MuxSettings settings = SettingsLoader.LoadSettings();
            string normalized = ColorPaletteCatalog.NormalizeId(id);
            if (normalized != ColorPaletteCatalog.SystemId && ColorPaletteCatalog.Find(normalized, settings.CustomPalettes) == null)
            {
                throw new ArgumentException("Unknown palette '" + id + "'.", nameof(id));
            }

            settings.ColorPalette = normalized;
            SettingsLoader.SaveSettings(settings);
            return normalized;
        }

        /// <summary>
        /// Adds or replaces a custom palette (matched by id) after validating it, and saves settings. Colors are
        /// stored normalized to <c>#rrggbb</c>; unset colors stay unset so they keep following the base.
        /// </summary>
        /// <param name="palette">The palette to save.</param>
        /// <param name="activate">True to also make it the active palette.</param>
        /// <returns>The saved palette, resolved (every color filled in).</returns>
        /// <exception cref="ArgumentException">Thrown when the palette is invalid.</exception>
        public static ColorPalette SaveCustom(ColorPalette palette, bool activate)
        {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            MuxSettings settings = SettingsLoader.LoadSettings();
            string id = ColorPaletteCatalog.NormalizeId(palette.Id);

            List<ColorPalette> others = new List<ColorPalette>();
            foreach (ColorPalette existing in settings.CustomPalettes)
            {
                if (existing != null && ColorPaletteCatalog.NormalizeId(existing.Id) != id)
                {
                    others.Add(existing);
                }
            }

            string? error = ColorPaletteCatalog.Validate(palette, others);
            if (error != null)
            {
                throw new ArgumentException(error, nameof(palette));
            }

            ColorPalette stored = new ColorPalette
            {
                Id = id,
                Name = palette.Name.Trim(),
                Dark = palette.Dark,
                Base = string.IsNullOrWhiteSpace(palette.Base) ? null : ColorPaletteCatalog.NormalizeId(palette.Base),
                Colors = new ColorPaletteColors()
            };

            foreach (string role in ColorPaletteColors.RoleNames)
            {
                if (ColorPaletteCatalog.TryNormalizeHex(palette.Colors?.Get(role), out string normalized))
                {
                    stored.Colors.Set(role, normalized);
                }
            }

            int index = settings.CustomPalettes.FindIndex(p => p != null && ColorPaletteCatalog.NormalizeId(p.Id) == id);
            if (index >= 0)
            {
                settings.CustomPalettes[index] = stored;
            }
            else
            {
                settings.CustomPalettes.Add(stored);
            }

            if (activate)
            {
                settings.ColorPalette = id;
            }

            SettingsLoader.SaveSettings(settings);
            return ColorPaletteCatalog.Find(id, settings.CustomPalettes) ?? stored;
        }

        /// <summary>
        /// Removes a custom palette and saves settings. When it was active, the active palette falls back to the
        /// default. Custom palettes that used it as their base keep working, falling back to the default base.
        /// </summary>
        /// <param name="id">The custom palette id.</param>
        /// <returns>True when a palette was removed.</returns>
        public static bool DeleteCustom(string id)
        {
            MuxSettings settings = SettingsLoader.LoadSettings();
            string normalized = ColorPaletteCatalog.NormalizeId(id);
            int removed = settings.CustomPalettes.RemoveAll(p => p != null && ColorPaletteCatalog.NormalizeId(p.Id) == normalized);
            if (removed == 0)
            {
                return false;
            }

            foreach (ColorPalette other in settings.CustomPalettes)
            {
                if (other != null && ColorPaletteCatalog.NormalizeId(other.Base) == normalized)
                {
                    other.Base = null;
                }
            }

            if (settings.ColorPalette == normalized)
            {
                settings.ColorPalette = ColorPaletteCatalog.DefaultId;
            }

            SettingsLoader.SaveSettings(settings);
            return true;
        }

        #endregion
    }
}
