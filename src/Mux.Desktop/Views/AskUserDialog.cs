namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.Primitives;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Interaction;

    /// <summary>
    /// A dialog for the model's <c>ask_user</c> tool: the question, 2 to 4 options (radio buttons, or check boxes when
    /// several may be chosen), and an "Other" box for a typed answer. Returns an <see cref="AskUserResponse"/> through
    /// <c>ShowDialog&lt;AskUserResponse&gt;</c>; closing the dialog without answering dismisses the question.
    /// </summary>
    public sealed class AskUserDialog : Window
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the dialog for a question.
        /// </summary>
        /// <param name="request">The question. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public AskUserDialog(AskUserRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            Title = "The agent has a question";
            Icon = IconResources.LoadWindowIcon();
            Width = 520;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            StackPanel panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = request.Question, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, FontSize = 15 });

            List<ToggleButton> toggles = new List<ToggleButton>();
            foreach (AskUserOption option in request.Options)
            {
                string label = string.IsNullOrWhiteSpace(option.Description) ? option.Label : option.Label + ": " + option.Description;
                ToggleButton toggle = request.MultiSelect
                    ? new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, Tag = option.Label }
                    : new RadioButton { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, Tag = option.Label, GroupName = "ask-user" };
                toggles.Add(toggle);
                panel.Children.Add(toggle);
            }

            TextBox other = new TextBox { PlaceholderText = "Other: type your own answer (optional)", AcceptsReturn = false };
            panel.Children.Add(other);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button skip = new Button { Content = "Skip" };
            skip.Click += (sender, args) => Close(AskUserResponse.Dismiss());
            buttons.Children.Add(skip);
            Button answer = new Button { Content = "Answer", Background = AppTheme.Current.AccentButton, Foreground = AppTheme.Current.AccentText };
            answer.Click += (sender, args) =>
            {
                AskUserResponse response = new AskUserResponse { OtherText = string.IsNullOrWhiteSpace(other.Text) ? null : other.Text!.Trim() };
                foreach (ToggleButton toggle in toggles)
                {
                    if (toggle.IsChecked == true && toggle.Tag is string label)
                    {
                        response.Selected.Add(label);
                    }
                }

                Close(response.Selected.Count == 0 && response.OtherText == null ? AskUserResponse.Dismiss() : response);
            };
            buttons.Children.Add(answer);
            panel.Children.Add(buttons);
            Content = panel;
        }

        #endregion
    }
}
