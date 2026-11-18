namespace Mux.Core.Theming
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A named color palette shared by every mux surface (TUI, desktop, web dashboard, VS Code). Built-in
    /// palettes come from <see cref="ColorPaletteCatalog"/>; custom palettes live in <c>settings.json</c>
    /// under <c>customPalettes</c> and may inherit any color they leave out from a <see cref="Base"/> palette.
    /// </summary>
    public sealed class ColorPalette
    {
        #region Public-Members

        /// <summary>
        /// The stable identifier, lowercase kebab-case (for example <c>midnight</c>). Saved as the active
        /// palette in <c>settings.json</c> (<c>colorPalette</c>).
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The display name shown in palette pickers.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// True for a dark palette (light text on a dark background). Surfaces use it to pick the matching
        /// light or dark variant of controls the palette does not color directly (scrollbars, native widgets).
        /// </summary>
        [JsonPropertyName("dark")]
        public bool Dark { get; set; } = true;

        /// <summary>
        /// For a custom palette: the id of the palette that supplies every color left null in
        /// <see cref="Colors"/>. Defaults to <c>mux</c> for a dark palette and <c>mux-light</c> for a light one.
        /// </summary>
        [JsonPropertyName("base")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Base { get; set; }

        /// <summary>
        /// The palette's colors.
        /// </summary>
        [JsonPropertyName("colors")]
        public ColorPaletteColors Colors { get; set; } = new ColorPaletteColors();

        /// <summary>
        /// True for a palette that ships with mux (not saved to settings).
        /// </summary>
        [JsonIgnore]
        public bool BuiltIn { get; internal set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns a deep copy of this palette.
        /// </summary>
        /// <returns>A new instance with the same values.</returns>
        public ColorPalette Clone()
        {
            return new ColorPalette
            {
                Id = Id,
                Name = Name,
                Dark = Dark,
                Base = Base,
                Colors = (Colors ?? new ColorPaletteColors()).Clone(),
                BuiltIn = BuiltIn
            };
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.IsNullOrEmpty(Name) ? Id : Name;
        }

        #endregion
    }
}
