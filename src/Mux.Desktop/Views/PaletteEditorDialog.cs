namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Settings;
    using Mux.Core.Theming;
    using Mux.Desktop.I18n;

    /// <summary>
    /// Creates, edits, and deletes custom color palettes. Pick a palette to start from (built-in or custom),
    /// adjust the id, name, light/dark flag, and the fourteen color roles (each with a live swatch), then save to
    /// apply it everywhere: palettes live in the shared <c>settings.json</c>, so the TUI, web dashboard, and VS
    /// Code see them too. Returns the saved palette id via <c>ShowDialog&lt;string?&gt;</c>, or null when closed
    /// without saving.
    /// </summary>
    public sealed class PaletteEditorDialog : Window
    {
        #region Private-Members

        private readonly ComboBox _StartFrom = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _Id = new TextBox();
        private readonly TextBox _Name = new TextBox();
        private readonly CheckBox _Dark = new CheckBox();
        private readonly Dictionary<string, TextBox> _Colors = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        private readonly Dictionary<string, Border> _Swatches = new Dictionary<string, Border>(StringComparer.Ordinal);
        private readonly TextBlock _Error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        private readonly Button _Delete = new Button();
        private List<ColorPalette> _Palettes = new List<ColorPalette>();
        private ColorPalette? _Loaded;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the editor, starting from <paramref name="startFromId"/> (the active palette, typically).
        /// </summary>
        /// <param name="startFromId">The palette whose values prefill the form.</param>
        public PaletteEditorDialog(string? startFromId)
        {
            Title = Localizer.T("palette.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 560;
            Height = 760;
            MinWidth = 460;
            MinHeight = 480;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;
            _Error.Foreground = AppTheme.Current.Error;

            foreach (string role in ColorPaletteColors.RoleNames)
            {
                TextBox box = new TextBox { MinWidth = 140 };
                Border swatch = new Border { Width = 34, Height = 26, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = AppTheme.Current.Border };
                string key = role;
                box.TextChanged += (sender, args) => UpdateSwatch(key);
                _Colors[role] = box;
                _Swatches[role] = swatch;
            }

            ReloadPalettes(startFromId);
            _StartFrom.SelectionChanged += (sender, args) =>
            {
                int index = _StartFrom.SelectedIndex;
                if (index >= 0 && index < _Palettes.Count)
                {
                    Load(_Palettes[index]);
                }
            };

            Content = BuildContent();
        }

        #endregion

        #region Private-Methods

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            StackPanel form = new StackPanel { Margin = new Thickness(24, 20, 24, 20), Spacing = 10 };
            form.Children.Add(Field(Localizer.T("palette.startFrom"), _StartFrom));
            form.Children.Add(Field(Localizer.T("palette.id"), _Id).Tip(Localizer.T("palette.idTip")));
            form.Children.Add(Field(Localizer.T("palette.name"), _Name));
            _Dark.Content = Localizer.T("palette.dark");
            _Dark.Foreground = theme.Text;
            form.Children.Add(_Dark);

            form.Children.Add(new TextBlock { Text = Localizer.T("palette.colors"), Foreground = theme.Accent, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 8, 0, 0) });
            foreach (string role in ColorPaletteColors.RoleNames)
            {
                Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,44,*"), Margin = new Thickness(0, 2, 0, 2) };
                TextBlock label = new TextBlock { Text = role, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(label, 0);
                Border swatch = _Swatches[role];
                swatch.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(swatch, 1);
                TextBox box = _Colors[role];
                Grid.SetColumn(box, 2);
                row.Children.Add(label);
                row.Children.Add(swatch);
                row.Children.Add(box);
                form.Children.Add(row);
            }

            form.Children.Add(_Error);

            DockPanel buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };
            _Delete.Content = Localizer.T("palette.delete");
            _Delete.Click += (sender, args) => DeleteLoaded();
            DockPanel.SetDock(_Delete, Dock.Left);
            buttons.Children.Add(_Delete);

            Button save = new Button { Content = Localizer.T("palette.save"), Background = theme.AccentButton, Foreground = theme.AccentText, Margin = new Thickness(8, 0, 0, 0) };
            save.Click += (sender, args) => Save();
            DockPanel.SetDock(save, Dock.Right);
            buttons.Children.Add(save);

            Button close = new Button { Content = Localizer.T("palette.close") };
            close.Click += (sender, args) => Close(null);
            DockPanel.SetDock(close, Dock.Right);
            buttons.Children.Add(close);
            form.Children.Add(buttons);

            return new ScrollViewer { Content = form, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        }

        private static Control Field(string label, Control control)
        {
            StackPanel panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = label, Foreground = AppTheme.Current.Text, FontWeight = FontWeight.SemiBold });
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.Children.Add(control);
            return panel;
        }

        private void ReloadPalettes(string? selectId)
        {
            _Palettes = ColorPaletteCatalog.GetAll(SettingsLoader.LoadSettings().CustomPalettes);
            List<string> labels = new List<string>();
            int selected = 0;
            for (int i = 0; i < _Palettes.Count; i++)
            {
                ColorPalette palette = _Palettes[i];
                labels.Add(palette.BuiltIn ? palette.Name : palette.Name + " (" + Localizer.T("settings.theme.custom") + ")");
                if (string.Equals(palette.Id, ColorPaletteCatalog.NormalizeId(selectId), StringComparison.Ordinal))
                {
                    selected = i;
                }
            }

            _StartFrom.ItemsSource = labels;
            _StartFrom.SelectedIndex = selected;
            Load(_Palettes[selected]);
        }

        // A built-in palette is a starting point (blank id, "copy" name); a custom one is edited in place.
        private void Load(ColorPalette palette)
        {
            _Loaded = palette;
            _Id.Text = palette.BuiltIn ? string.Empty : palette.Id;
            _Name.Text = palette.BuiltIn ? palette.Name + " copy" : palette.Name;
            _Dark.IsChecked = palette.Dark;
            foreach (string role in ColorPaletteColors.RoleNames)
            {
                _Colors[role].Text = palette.Colors.Get(role) ?? string.Empty;
            }

            _Delete.IsEnabled = !palette.BuiltIn;
            _Error.Text = string.Empty;
        }

        private void UpdateSwatch(string role)
        {
            Border swatch = _Swatches[role];
            swatch.Background = ColorPaletteCatalog.TryNormalizeHex(_Colors[role].Text, out string hex)
                ? new SolidColorBrush(Color.Parse(hex))
                : Brushes.Transparent;
        }

        private void Save()
        {
            ColorPalette palette = new ColorPalette
            {
                Id = ColorPaletteCatalog.NormalizeId(_Id.Text),
                Name = (_Name.Text ?? string.Empty).Trim(),
                Dark = _Dark.IsChecked == true,
                Base = _Loaded != null && _Loaded.BuiltIn ? _Loaded.Id : _Loaded?.Base
            };

            foreach (string role in ColorPaletteColors.RoleNames)
            {
                string value = (_Colors[role].Text ?? string.Empty).Trim();
                palette.Colors.Set(role, value.Length == 0 ? null : value);
            }

            try
            {
                ColorPalette saved = ColorPaletteSettings.SaveCustom(palette, activate: true);
                Close(saved.Id);
            }
            catch (ArgumentException ex)
            {
                _Error.Text = ex.Message.Split(" (Parameter", StringSplitOptions.None)[0];
            }
            catch (Exception ex)
            {
                _Error.Text = ex.Message;
            }
        }

        private void DeleteLoaded()
        {
            if (_Loaded == null || _Loaded.BuiltIn)
            {
                return;
            }

            try
            {
                ColorPaletteSettings.DeleteCustom(_Loaded.Id);
                ReloadPalettes(ColorPaletteCatalog.DefaultId);
            }
            catch (Exception ex)
            {
                _Error.Text = ex.Message;
            }
        }

        #endregion
    }
}
