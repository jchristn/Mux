namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Skills;
    using Mux.Desktop.I18n;

    /// <summary>
    /// Sets or clears one skill's category override. Offers the canonical categories in a picker and accepts any
    /// kebab-case value typed in the text box; Save stores the override in <c>skills.json</c> through
    /// <see cref="SkillManager.SetCategory"/>, and Clear override removes it. Returns true when something changed.
    /// </summary>
    public sealed class SkillCategoryDialog : Window
    {
        #region Private-Members

        private readonly string _SkillsDirectory;
        private readonly string _SkillName;
        private readonly TextBox _Category = new TextBox { MinWidth = 320 };
        private readonly ComboBox _Known = new ComboBox { MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12, TextWrapping = TextWrapping.Wrap };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillCategoryDialog"/> class.
        /// </summary>
        /// <param name="skillsDirectory">The skills directory. Must not be null.</param>
        /// <param name="skillName">The skill id. Must not be null.</param>
        /// <param name="effectiveCategory">The skill's current effective category, shown as the placeholder.</param>
        /// <param name="overrideCategory">The current override, prefilled, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public SkillCategoryDialog(string skillsDirectory, string skillName, string effectiveCategory, string? overrideCategory)
        {
            _SkillsDirectory = skillsDirectory ?? throw new ArgumentNullException(nameof(skillsDirectory));
            _SkillName = skillName ?? throw new ArgumentNullException(nameof(skillName));
            AppTheme theme = AppTheme.Current;
            Title = Localizer.T("skill.category.title") + " · " + skillName;
            Icon = IconResources.LoadWindowIcon();
            Width = 520;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            List<string> options = new List<string> { Localizer.T("skill.category.pick") };
            options.AddRange(SkillCategories.Known);
            _Known.ItemsSource = options;
            _Known.SelectedIndex = 0;
            _Known.SelectionChanged += (object? sender, SelectionChangedEventArgs e) =>
            {
                if (_Known.SelectedIndex > 0 && _Known.SelectedItem is string picked)
                {
                    _Category.Text = picked;
                }
            };

            _Category.Text = overrideCategory ?? string.Empty;
            _Category.PlaceholderText = effectiveCategory;
            Content = BuildContent(theme);
        }

        #endregion

        #region Private-Methods

        private Control BuildContent(AppTheme theme)
        {
            StackPanel root = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
            root.Children.Add(new TextBlock { Text = Localizer.T("skill.category.label"), FontWeight = FontWeight.SemiBold, Foreground = theme.Text });
            root.Children.Add(_Known);
            root.Children.Add(_Category);
            root.Children.Add(new TextBlock { Text = Localizer.T("skill.category.help"), Foreground = theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            root.Children.Add(_Error);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            Button clear = new Button { Content = Localizer.T("skill.category.clear") };
            clear.Click += (object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Apply(null);
            buttons.Children.Add(clear);
            Button cancel = new Button { Content = Localizer.T("act.cancel") };
            cancel.Click += (object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(false);
            buttons.Children.Add(cancel);
            Button save = new Button { Content = Localizer.T("act.save"), Background = theme.AccentButton, Foreground = theme.AccentText };
            save.Click += (object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Apply(_Category.Text);
            buttons.Children.Add(save);
            root.Children.Add(buttons);
            return root;
        }

        private void Apply(string? category)
        {
            if (!SkillCategories.TryParse(category, out string? _, out string error))
            {
                _Error.Text = error;
                return;
            }

            try
            {
                new SkillManager(_SkillsDirectory).SetCategory(_SkillName, category);
                Close(true);
            }
            catch (Exception ex)
            {
                _Error.Text = ex.Message;
            }
        }

        #endregion
    }
}
