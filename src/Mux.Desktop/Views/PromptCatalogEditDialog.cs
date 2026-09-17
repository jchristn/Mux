namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Prompting;
    using Mux.Desktop.Prompting;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A form for editing one global-scoped operational-prompt override. Pre-fills from the row's current
    /// effective text; on OK it validates that every required placeholder is preserved (via
    /// <see cref="PromptResolver.TryValidateOverride"/>) and, when valid, exposes the edited text through
    /// <see cref="Content"/> and returns true via <c>ShowDialog&lt;bool&gt;</c>. Persistence is the caller's
    /// responsibility.
    /// </summary>
    public sealed class PromptCatalogEditDialog : Window
    {
        private readonly PromptCatalogRow _Row;
        private readonly TextBox _Content = MultilineBox();
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12 };

        /// <summary>
        /// Initializes the catalog-override editor.
        /// </summary>
        /// <param name="row">The catalog row to edit (its effective text pre-fills the editor). Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="row"/> is null.</exception>
        public PromptCatalogEditDialog(PromptCatalogRow row)
        {
            ArgumentNullException.ThrowIfNull(row);
            _Row = row;
            Content = row.Effective;

            Title = Localizer.T(StringKeys.PromptCatalogEdit) + " · " + row.DisplayName;
            Icon = IconResources.LoadWindowIcon();
            Width = 900;
            Height = 640;
            MinWidth = 520;
            MinHeight = 400;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            _Content.Text = row.Effective;
            base.Content = BuildContent();
        }

        /// <summary>
        /// Gets the edited override text. Valid only after the dialog returns true.
        /// </summary>
        public new string Content { get; private set; }

        private static TextBox MultilineBox()
        {
            return new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 220,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace")
            };
        }

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            Button cancel = new Button { Content = Localizer.T("act.cancel") };
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = Localizer.T("act.save"), Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Click += (sender, args) => Ok();
            buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            DockPanel.SetDock(_Error, Dock.Bottom);
            root.Children.Add(_Error);

            StackPanel form = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 14, 0) };
            form.Children.Add(new TextBlock { Text = _Row.DisplayName, FontWeight = FontWeight.SemiBold, FontSize = 15, Foreground = theme.Text });
            form.Children.Add(new TextBlock { Text = _Row.Description, Foreground = theme.Muted, TextWrapping = TextWrapping.Wrap });
            form.Children.Add(new TextBlock { Text = _Row.Key, FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"), Foreground = theme.Muted, FontSize = 12 });
            if (_Row.Placeholders.Count > 0)
            {
                form.Children.Add(new TextBlock { Text = Localizer.T(StringKeys.PromptCatalogPlaceholders) + string.Join(", ", _Row.Placeholders), Foreground = theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            }

            form.Children.Add(_Content);

            root.Children.Add(new ScrollViewer { Content = form });
            return root;
        }

        private void Ok()
        {
            string text = _Content.Text ?? string.Empty;
            if (!PromptResolver.TryValidateOverride(_Row.Key, text, out IReadOnlyList<string> missing))
            {
                _Error.Text = Localizer.T(StringKeys.PromptCatalogMissingPlaceholders) + string.Join(", ", missing);
                return;
            }

            Content = text;
            Close(true);
        }
    }
}
