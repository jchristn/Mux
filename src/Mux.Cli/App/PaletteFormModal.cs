namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Theming;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A modal form for creating or editing a custom color palette: id, name, light/dark, an optional base
    /// palette, and the fourteen color roles as hex values (prefilled from the palette being edited or the
    /// active palette). Enter validates and returns a <see cref="ColorPalette"/> via <see cref="Modal.Completion"/>;
    /// Escape cancels (returns null). Saving is the caller's job (<see cref="ColorPaletteSettings.SaveCustom"/>).
    /// </summary>
    public sealed class PaletteFormModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;
        private const int ContentWidth = 60;

        private readonly string _Title;
        private readonly List<ColorPalette> _OtherCustom;
        private readonly Form _Form;
        private readonly ScrollingFormHost _Host;
        private readonly TextField _Id;
        private readonly TextField _Name;
        private readonly Checkbox _Dark;
        private readonly TextField _Base;
        private readonly Dictionary<string, TextField> _Colors = new Dictionary<string, TextField>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="PaletteFormModal"/> class.
        /// </summary>
        /// <param name="title">The box title.</param>
        /// <param name="seed">The palette whose values prefill the form (the one being edited, or a starting point).</param>
        /// <param name="editing">True to edit <paramref name="seed"/> in place (its id is fixed).</param>
        /// <param name="customPalettes">The existing custom palettes, for validating the id and base.</param>
        public PaletteFormModal(string title, ColorPalette seed, bool editing, IEnumerable<ColorPalette>? customPalettes)
        {
            if (seed == null) throw new ArgumentNullException(nameof(seed));
            _Title = title ?? "Color palette";
            _OtherCustom = new List<ColorPalette>();
            if (customPalettes != null)
            {
                foreach (ColorPalette palette in customPalettes)
                {
                    if (palette != null && (!editing || palette.Id != seed.Id))
                    {
                        _OtherCustom.Add(palette);
                    }
                }
            }

            _Id = new TextField { Value = editing ? seed.Id : string.Empty };
            _Name = new TextField { Value = editing ? seed.Name : string.Empty };
            _Dark = new Checkbox("Dark palette (light text on a dark background)", seed.Dark);
            _Base = new TextField { Value = editing ? (seed.Base ?? string.Empty) : (seed.BuiltIn ? seed.Id : seed.Base ?? string.Empty) };

            _Form = new Form();
            _Form.Add("Id (kebab-case, e.g. my-palette)", _Id, ValidateId);
            _Form.Add("Name", _Name, () => _Name.Value.Trim().Length == 0 ? "Name is required." : null);
            _Form.Add("Dark", _Dark);
            _Form.Add("Base palette (fills blank colors; blank = mux / mux-light)", _Base, ValidateBase);
            if (editing)
            {
                _Form.SetFieldEnabled(0, false);
            }

            foreach (string role in ColorPaletteColors.RoleNames)
            {
                TextField field = new TextField { Value = seed.Colors.Get(role) ?? string.Empty };
                _Colors[role] = field;
                string label = role;
                _Form.Add(label + " (hex, blank = inherit)", field, () => ValidateHex(label, field.Value));
            }

            _Form.SubmitRequested += (sender, e) => Complete();
            _Host = new ScrollingFormHost(_Form);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                Close(null);
                return true;
            }

            if (key.Code == KeyCode.Enter && key.Modifiers == KeyModifiers.None)
            {
                _Form.Submit();
                return true;
            }

            return _Form.HandleKey(key);
        }

        /// <inheritdoc/>
        public override bool HandlePaste(string text)
        {
            _Form.HandlePaste(text);
            return true;
        }

        /// <inheritdoc/>
        public override bool HandleMouse(MouseEvent mouse)
        {
            _Host.HandleMouse(mouse);
            return true;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;
            int hintRows = 2;
            int contentWidth = Math.Max(8, Math.Min(ContentWidth, screenWidth - 2 - (2 * PadX)));
            int formHeight = _Host.MeasureHeight(contentWidth);
            int boxWidth = Math.Min(screenWidth, contentWidth + 2 + (2 * PadX));
            int maxContentHeight = Math.Max(1, screenHeight - hintRows - 2 - (2 * PadY));
            int visibleFormHeight = Math.Min(formHeight, maxContentHeight);
            int boxHeight = Math.Min(screenHeight, visibleFormHeight + hintRows + 2 + (2 * PadY));
            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(8)), _Title);

            int contentX = boxX + 1 + PadX;
            int firstRow = boxY + 1 + PadY;
            int usableHeight = boxHeight - 2 - (2 * PadY) - hintRows;
            if (usableHeight < 1)
            {
                return;
            }

            _Host.Render(surface, new Rect(contentX, firstRow, contentWidth, usableHeight));

            int hintRow = boxY + boxHeight - 2;
            string? errors = _Host.ErrorSummary;
            string hint = errors ?? "Tab/↑↓ move · Space toggle · Enter save · Esc cancel";
            Color hintColor = Color.FromPalette(errors != null ? (byte)9 : (byte)8);
            surface.DrawText(contentX, hintRow, hint.Length <= contentWidth ? hint : hint.Substring(0, Math.Max(0, contentWidth - 1)) + "…", CellStyle.Default.WithForeground(hintColor));
        }

        #endregion

        #region Private-Methods

        private ColorPalette BuildPalette()
        {
            ColorPalette palette = new ColorPalette
            {
                Id = ColorPaletteCatalog.NormalizeId(_Id.Value),
                Name = _Name.Value.Trim(),
                Dark = _Dark.Checked,
                Base = _Base.Value.Trim().Length == 0 ? null : ColorPaletteCatalog.NormalizeId(_Base.Value)
            };

            foreach (KeyValuePair<string, TextField> entry in _Colors)
            {
                string value = entry.Value.Value.Trim();
                palette.Colors.Set(entry.Key, value.Length == 0 ? null : value);
            }

            return palette;
        }

        private string? ValidateId()
        {
            ColorPalette probe = BuildPalette();
            probe.Name = "x";
            probe.Base = null;
            string? error = ColorPaletteCatalog.Validate(probe, _OtherCustom);
            if (error != null)
            {
                return error;
            }

            foreach (ColorPalette other in _OtherCustom)
            {
                if (other.Id == probe.Id)
                {
                    return "A custom palette with id '" + probe.Id + "' already exists.";
                }
            }

            return null;
        }

        // Only base-related problems belong under this field; the id field reports its own.
        private string? ValidateBase()
        {
            if (_Base.Value.Trim().Length == 0)
            {
                return null;
            }

            ColorPalette probe = BuildPalette();
            probe.Id = probe.Id.Length == 0 ? "draft" : probe.Id;
            probe.Name = "x";
            string? error = ColorPaletteCatalog.Validate(probe, _OtherCustom);
            bool aboutBase = error != null && (error.StartsWith("Base", StringComparison.Ordinal) || error.Contains("its base", StringComparison.Ordinal));
            return aboutBase ? error : null;
        }

        private static string? ValidateHex(string role, string value)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0 || ColorPaletteCatalog.TryNormalizeHex(trimmed, out _))
            {
                return null;
            }

            return role + " must be a hex color such as #1e293b.";
        }

        private void Complete()
        {
            ColorPalette palette = BuildPalette();
            Close(palette);
        }

        #endregion
    }
}
