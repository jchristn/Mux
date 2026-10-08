namespace Mux.Desktop.Styling
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.Primitives;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Styling;

    /// <summary>
    /// Mirrors the active <see cref="AppTheme"/> palette into the Fluent theme so stock controls (buttons, text
    /// boxes, combo boxes, check boxes, tooltips, scroll bars) match the app instead of rendering as plain Fluent
    /// defaults. The approach follows Armada Harbor: Fluent's own resource keys are overridden at application
    /// level, and a small set of global styles sets corner radii, padding, and persistent scroll bars. Call
    /// <see cref="Apply"/> once at startup and again whenever the palette changes. Must be called on the UI thread.
    /// </summary>
    public static class MuxThemeStyles
    {
        #region Private-Members

        private const string StyleMarker = "mux-theme-styles";

        #endregion

        #region Public-Members

        /// <summary>
        /// The style class for a scroll viewer that should keep its content clear of the vertical scroll bar.
        /// Fluent draws the scroll bar over the right edge of the content, so form fields would otherwise be
        /// overlapped by it.
        /// </summary>
        public const string GutterClass = "gutter";

        /// <summary>
        /// The style class for a raised, bordered card section.
        /// </summary>
        public const string CardClass = "card";

        /// <summary>
        /// The width, in pixels, reserved to the right of content inside a <see cref="GutterClass"/> scroll viewer.
        /// </summary>
        public const double ScrollGutter = 16;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Applies the current palette to the application's resources and installs (or replaces) the global
        /// control styles.
        /// </summary>
        /// <param name="application">The application. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="application"/> is null.</exception>
        public static void Apply(Application application)
        {
            ArgumentNullException.ThrowIfNull(application);

            AppTheme theme = AppTheme.Current;
            ApplyResources(application.Resources, theme);

            for (int i = application.Styles.Count - 1; i >= 0; i--)
            {
                if (application.Styles[i] is Styles existing && existing.Resources.ContainsKey(StyleMarker))
                {
                    application.Styles.RemoveAt(i);
                }
            }

            application.Styles.Add(BuildStyles(theme));
        }

        #endregion

        #region Private-Methods

        private static void ApplyResources(IResourceDictionary resources, AppTheme theme)
        {
            Color accent = ColorOf(theme.AccentButton);
            Color accentHover = Shade(accent, theme.IsDark ? 1.12 : 0.88);
            Color accentPressed = Shade(accent, theme.IsDark ? 0.9 : 0.78);

            resources["ControlCornerRadius"] = new CornerRadius(6);
            resources["OverlayCornerRadius"] = new CornerRadius(8);

            resources["SystemAccentColor"] = accent;
            resources["SystemAccentColorDark1"] = Shade(accent, 0.88);
            resources["SystemAccentColorDark2"] = Shade(accent, 0.76);
            resources["SystemAccentColorDark3"] = Shade(accent, 0.64);
            resources["SystemAccentColorLight1"] = Shade(accent, 1.12);
            resources["SystemAccentColorLight2"] = Shade(accent, 1.24);
            resources["SystemAccentColorLight3"] = Shade(accent, 1.36);

            resources["AccentButtonBackground"] = theme.AccentButton;
            resources["AccentButtonBackgroundPointerOver"] = new SolidColorBrush(accentHover);
            resources["AccentButtonBackgroundPressed"] = new SolidColorBrush(accentPressed);
            resources["AccentButtonForeground"] = theme.AccentText;
            resources["AccentButtonForegroundPointerOver"] = theme.AccentText;
            resources["AccentButtonForegroundPressed"] = theme.AccentText;

            resources["ButtonBackground"] = theme.SurfaceAlt;
            resources["ButtonBackgroundPointerOver"] = theme.Hover;
            resources["ButtonBackgroundPressed"] = theme.Border;
            resources["ButtonForeground"] = theme.Text;
            resources["ButtonForegroundPointerOver"] = theme.Text;
            resources["ButtonForegroundPressed"] = theme.Text;
            resources["ButtonBorderBrush"] = theme.InputBorder;
            resources["ButtonBorderBrushPointerOver"] = theme.Muted;
            resources["ButtonBorderBrushPressed"] = theme.Muted;
            resources["ButtonBackgroundDisabled"] = theme.Hover;
            resources["ButtonBorderBrushDisabled"] = theme.Border;
            resources["ButtonForegroundDisabled"] = theme.Muted;

            resources["ToggleButtonBackground"] = theme.SurfaceAlt;
            resources["ToggleButtonBackgroundPointerOver"] = theme.Hover;
            resources["ToggleButtonBackgroundPressed"] = theme.Border;
            resources["ToggleButtonForeground"] = theme.Text;
            resources["ToggleButtonForegroundPointerOver"] = theme.Text;
            resources["ToggleButtonBorderBrush"] = theme.InputBorder;
            resources["ToggleButtonBorderBrushPointerOver"] = theme.Muted;
            resources["ToggleButtonBackgroundChecked"] = theme.AccentButton;
            resources["ToggleButtonBackgroundCheckedPointerOver"] = new SolidColorBrush(accentHover);
            resources["ToggleButtonForegroundChecked"] = theme.AccentText;
            resources["RepeatButtonBackgroundDisabled"] = theme.Hover;

            resources["TextControlBackground"] = theme.SurfaceAlt;
            resources["TextControlBackgroundPointerOver"] = theme.SurfaceAlt;
            resources["TextControlBackgroundFocused"] = theme.SurfaceAlt;
            resources["TextControlForeground"] = theme.Text;
            resources["TextControlForegroundPointerOver"] = theme.Text;
            resources["TextControlForegroundFocused"] = theme.Text;
            resources["TextControlPlaceholderForeground"] = theme.Muted;
            resources["TextControlPlaceholderForegroundPointerOver"] = theme.Muted;
            resources["TextControlPlaceholderForegroundFocused"] = theme.Muted;
            resources["TextControlBorderBrush"] = theme.InputBorder;
            resources["TextControlBorderBrushPointerOver"] = theme.Muted;
            resources["TextControlBorderBrushFocused"] = theme.Accent;
            resources["TextControlBackgroundDisabled"] = theme.Hover;
            resources["TextControlBorderBrushDisabled"] = theme.Border;
            resources["TextControlForegroundDisabled"] = theme.Muted;
            resources["TextControlSelectionHighlightColor"] = theme.AccentButton;

            resources["ComboBoxBackground"] = theme.SurfaceAlt;
            resources["ComboBoxBackgroundPointerOver"] = theme.Hover;
            resources["ComboBoxBackgroundPressed"] = theme.Border;
            resources["ComboBoxForeground"] = theme.Text;
            resources["ComboBoxBorderBrush"] = theme.InputBorder;
            resources["ComboBoxBorderBrushPointerOver"] = theme.Muted;
            resources["ComboBoxBackgroundDisabled"] = theme.Hover;
            resources["ComboBoxBorderBrushDisabled"] = theme.Border;
            resources["ComboBoxDropDownBackground"] = theme.SurfaceAlt;
            resources["ComboBoxDropDownBorderBrush"] = theme.Border;
            resources["ComboBoxItemBackgroundPointerOver"] = theme.Hover;
            resources["ComboBoxItemBackgroundSelected"] = theme.AccentSoft;
            resources["ComboBoxItemBackgroundSelectedPointerOver"] = theme.AccentSoft;

            resources["CheckBoxCheckBackgroundFillChecked"] = theme.AccentButton;
            resources["CheckBoxCheckBackgroundFillCheckedPointerOver"] = new SolidColorBrush(accentHover);
            resources["CheckBoxCheckBackgroundStrokeChecked"] = theme.AccentButton;
            resources["CheckBoxCheckBackgroundStrokeUnchecked"] = theme.InputBorder;
            resources["CheckBoxCheckBackgroundStrokeUncheckedPointerOver"] = theme.Muted;
            resources["CheckBoxCheckGlyphForegroundChecked"] = theme.AccentText;
            resources["CheckBoxForegroundUnchecked"] = theme.Text;
            resources["CheckBoxForegroundChecked"] = theme.Text;

            resources["ListBoxItemBackgroundPointerOver"] = theme.Hover;
            resources["ListBoxItemBackgroundSelected"] = theme.AccentSoft;
            resources["ListBoxItemBackgroundSelectedPointerOver"] = theme.AccentSoft;

            resources["TabItemHeaderSelectedPipeFill"] = theme.Accent;
            resources["TabItemHeaderForegroundSelected"] = theme.Text;
            resources["TabItemHeaderForegroundUnselected"] = theme.Muted;
            resources["TabItemHeaderForegroundUnselectedPointerOver"] = theme.Text;

            resources["ToolTipBackground"] = theme.SurfaceAlt;
            resources["ToolTipForeground"] = theme.Text;
            resources["ToolTipBorderBrush"] = theme.Border;

            resources["MenuFlyoutPresenterBackground"] = theme.SurfaceAlt;
            resources["MenuFlyoutPresenterBorderBrush"] = theme.Border;
            resources["MenuFlyoutItemBackgroundPointerOver"] = theme.Hover;
            resources["FlyoutPresenterBackground"] = theme.SurfaceAlt;
            resources["FlyoutBorderThemeBrush"] = theme.Border;
        }

        private static Styles BuildStyles(AppTheme theme)
        {
            Styles styles = new Styles();
            styles.Resources[StyleMarker] = true;

            // Fluent collapses scroll bars to a hairline until hovered, which is hard to grab; keep them full
            // width everywhere, as Armada Harbor does. Text boxes and list boxes pass the attached value through.
            styles.Add(Style(x => x.OfType<ScrollViewer>(), new Setter(ScrollViewer.AllowAutoHideProperty, false)));
            styles.Add(Style(x => x.OfType<TextBox>(), new Setter(ScrollViewer.AllowAutoHideProperty, false)));
            styles.Add(Style(x => x.OfType<ListBox>(), new Setter(ScrollViewer.AllowAutoHideProperty, false)));

            // A gutter scroll viewer reserves room for its scroll bar so it never sits on top of form fields.
            styles.Add(Style(
                x => x.OfType<ScrollViewer>().Class(GutterClass),
                new Setter(ScrollViewer.PaddingProperty, new Thickness(0, 0, ScrollGutter, 0))));

            // Inputs: a consistent height, roomier padding, and the shared corner radius.
            styles.Add(Style(
                x => x.OfType<TextBox>(),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 7, 10, 7)),
                new Setter(Layoutable.MinHeightProperty, 34.0)));
            styles.Add(Style(
                x => x.OfType<ComboBox>(),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(Layoutable.MinHeightProperty, 34.0)));

            // Buttons: the shared radius and a little more horizontal room.
            styles.Add(Style(
                x => x.OfType<Button>(),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(14, 6, 14, 6))));

            // A raised section: a bordered card a step above the window background.
            styles.Add(Style(
                x => x.OfType<Border>().Class(CardClass),
                new Setter(Border.BackgroundProperty, theme.SurfaceAlt),
                new Setter(Border.BorderBrushProperty, theme.Border),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(8)),
                new Setter(Decorator.PaddingProperty, new Thickness(16, 14, 16, 14))));

            // Tooltips read as cards rather than Fluent's default dark slab.
            styles.Add(Style(
                x => x.OfType<ToolTip>(),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1))));

            return styles;
        }

        private static Style Style(Func<Selector?, Selector> selector, params Setter[] setters)
        {
            Style style = new Style(selector);
            foreach (Setter setter in setters)
            {
                style.Setters.Add(setter);
            }

            return style;
        }

        private static Color ColorOf(IBrush brush)
        {
            return brush is ISolidColorBrush solid ? solid.Color : Colors.Green;
        }

        private static Color Shade(Color color, double factor)
        {
            return Color.FromArgb(
                color.A,
                (byte)Math.Clamp(color.R * factor, 0, 255),
                (byte)Math.Clamp(color.G * factor, 0, 255),
                (byte)Math.Clamp(color.B * factor, 0, 255));
        }

        #endregion
    }
}
