namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Models;

    /// <summary>
    /// A form for creating or editing a single prompt profile (system prompt, tools-disabled prompt, and
    /// compaction prompt). Pre-fills from the supplied <see cref="PromptProfile"/> and, on OK, writes the
    /// edited values back into that instance and returns true via <c>ShowDialog&lt;bool&gt;</c>.
    /// </summary>
    public sealed class PromptProfileFormDialog : Window
    {
        private readonly PromptProfile _Profile;
        private readonly TextBox _Name = new TextBox();
        private readonly CheckBox _IsActive = new CheckBox { Content = "Use as the active profile" };
        private readonly TextBox _SystemPrompt = MultilineBox();
        private readonly TextBox _ToolsDisabled = MultilineBox();
        private readonly TextBox _Compaction = MultilineBox();
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12 };

        /// <summary>
        /// Instantiate the prompt-profile form.
        /// </summary>
        /// <param name="profile">The profile to edit (pre-filled and written back on OK). Required.</param>
        /// <param name="isNew">True when creating a new profile (affects the title only).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> is null.</exception>
        public PromptProfileFormDialog(PromptProfile profile, bool isNew)
        {
            ArgumentNullException.ThrowIfNull(profile);
            _Profile = profile;

            Title = isNew ? "Add prompt profile" : "Edit prompt profile";
            Icon = IconResources.LoadWindowIcon();
            Width = 960;
            Height = 930;
            MinWidth = 560;
            MinHeight = 520;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            _Name.Text = profile.Name;
            _IsActive.IsChecked = profile.IsActive;
            _SystemPrompt.Text = profile.SystemPrompt;
            _ToolsDisabled.Text = profile.ToolsDisabledPrompt;
            _Compaction.Text = profile.CompactionPrompt;

            Content = BuildContent();
        }

        private static TextBox MultilineBox()
        {
            return new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 96,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace")
            };
        }

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            Button cancel = new Button { Content = "Cancel" };
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = "Save", Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Click += (sender, args) => Ok();
            buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            DockPanel.SetDock(_Error, Dock.Bottom);
            root.Children.Add(_Error);

            StackPanel form = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 14, 0) };
            form.Children.Add(Field("Name", _Name));
            form.Children.Add(_IsActive);
            form.Children.Add(Field("System prompt", _SystemPrompt));
            form.Children.Add(Field("Tools-disabled prompt", _ToolsDisabled));
            form.Children.Add(Field("Compaction prompt", _Compaction));

            root.Children.Add(new ScrollViewer { Content = form });
            return root;
        }

        private Control Field(string label, Control control)
        {
            StackPanel panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Foreground = AppTheme.Current.Text });
            panel.Children.Add(control);
            return panel;
        }

        private void Ok()
        {
            string name = (_Name.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                _Error.Text = "Name is required.";
                return;
            }

            _Profile.Name = name;
            _Profile.IsActive = _IsActive.IsChecked ?? false;
            _Profile.SystemPrompt = _SystemPrompt.Text ?? string.Empty;
            _Profile.ToolsDisabledPrompt = _ToolsDisabled.Text ?? string.Empty;
            _Profile.CompactionPrompt = _Compaction.Text ?? string.Empty;
            Close(true);
        }
    }
}
