namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Automation;
    using Avalonia.Controls;
    using Avalonia.Controls.Shapes;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Desktop.I18n;

    /// <summary>
    /// Adds a show/hide toggle to a masked <see cref="TextBox"/> (one with a <see cref="TextBox.PasswordChar"/>):
    /// an eye button inside the box's right edge flips <see cref="TextBox.RevealPassword"/>, swaps to an eye-off
    /// icon, and updates its translated tooltip and accessible name. Focus returns to the box with the caret at the
    /// end, so typing continues where it left off.
    /// </summary>
    public static class PasswordReveal
    {
        // Lucide-style eye and eye-off outlines on a 24 x 24 grid, drawn with a 2-unit stroke.
        private const string EyeData = "M1,12 C1,12 5,5 12,5 C19,5 23,12 23,12 C23,12 19,19 12,19 C5,19 1,12 1,12 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12 Z";
        private const string EyeOffData = "M9.9,4.24 A9.1,9.1 0 0 1 12,4 C19,4 23,12 23,12 A18.5,18.5 0 0 1 20.84,15.19 M6.61,6.61 A13.5,13.5 0 0 0 1,12 C1,12 5,20 12,20 A9.7,9.7 0 0 0 17.39,18.39 M14.12,14.12 A3,3 0 1 1 9.88,9.88 M1,1 L23,23";

        /// <summary>
        /// Wire the toggle into <paramref name="box"/>.
        /// </summary>
        /// <param name="box">A masked text box. Required.</param>
        /// <param name="theme">The active theme, for the icon colors. Required.</param>
        /// <param name="isKey">True for API keys and tokens ("Show key"); false for account passwords ("Show password").</param>
        /// <returns>The same text box, so calls can be chained inline.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static TextBox Attach(TextBox box, AppTheme theme, bool isKey)
        {
            if (box is null) throw new ArgumentNullException(nameof(box));
            if (theme is null) throw new ArgumentNullException(nameof(theme));

            Path icon = new Path
            {
                Width = 24,
                Height = 24,
                Stroke = theme.Muted,
                StrokeThickness = 2,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round
            };

            Button button = new Button
            {
                Content = new Viewbox { Width = 16, Height = 16, Child = icon },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Tag = isKey
            };

            void Sync()
            {
                bool shown = box.RevealPassword;
                icon.Data = StreamGeometry.Parse(shown ? EyeOffData : EyeData);
                string label = Label(shown, isKey);
                button.Tip(label);
                AutomationProperties.SetName(button, label);
            }

            button.PointerEntered += (sender, args) => icon.Stroke = theme.Accent;
            button.PointerExited += (sender, args) => icon.Stroke = theme.Muted;
            button.Click += (sender, args) =>
            {
                box.RevealPassword = !box.RevealPassword;
                Sync();
                box.Focus();
                box.CaretIndex = box.Text?.Length ?? 0;
            };

            Sync();
            box.InnerRightContent = button;
            return box;
        }

        /// <summary>
        /// Hide the value again (for example after a sign-in clears the field). Safe on a box without the toggle.
        /// </summary>
        /// <param name="box">The text box.</param>
        public static void Conceal(TextBox box)
        {
            if (box is null || !box.RevealPassword) return;
            box.RevealPassword = false;
            if (box.InnerRightContent is Button button && button.Content is Viewbox viewbox && viewbox.Child is Path icon)
            {
                icon.Data = StreamGeometry.Parse(EyeData);
                string label = Label(false, button.Tag is bool isKey && isKey);
                button.Tip(label);
                AutomationProperties.SetName(button, label);
            }
        }

        /// <summary>
        /// The translated tooltip for the toggle's current state.
        /// </summary>
        /// <param name="shown">Whether the value is currently visible.</param>
        /// <param name="isKey">Whether the field holds a key or token rather than a password.</param>
        /// <returns>The label.</returns>
        public static string Label(bool shown, bool isKey)
        {
            return Localizer.T((shown ? "act.hide" : "act.show") + (isKey ? "Key" : "Password"));
        }
    }
}
