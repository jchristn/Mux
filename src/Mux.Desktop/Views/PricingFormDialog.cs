namespace Mux.Desktop.Views
{
    using System;
    using System.Globalization;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Telemetry;

    /// <summary>
    /// A form for creating or editing one model's pricing (input, cached-input, and output US dollars per
    /// million tokens). On Save it writes the rates back into the supplied <see cref="ModelPricing"/>, exposes
    /// the (possibly renamed) model key via <see cref="ModelName"/>, and returns true.
    /// </summary>
    public sealed class PricingFormDialog : Window
    {
        private readonly ModelPricing _Pricing;
        private readonly TextBox _Model = new TextBox();
        private readonly TextBox _Input = new TextBox();
        private readonly TextBox _Cached = new TextBox();
        private readonly TextBox _Output = new TextBox();
        private readonly TextBlock _Error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#cf222e")), FontSize = 12, TextWrapping = TextWrapping.Wrap };

        /// <summary>
        /// Instantiate the pricing form.
        /// </summary>
        /// <param name="model">The model key to edit (blank for a new row).</param>
        /// <param name="pricing">The pricing to edit (written back on Save). Required.</param>
        /// <param name="isNew">True when creating a new row (affects the title only).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pricing"/> is null.</exception>
        public PricingFormDialog(string model, ModelPricing pricing, bool isNew)
        {
            _Pricing = pricing ?? throw new ArgumentNullException(nameof(pricing));
            ModelName = model ?? string.Empty;

            Title = isNew ? "Add model pricing" : "Edit model pricing";
            Icon = IconResources.LoadWindowIcon();
            Width = 520;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            _Model.Text = ModelName;
            _Input.Text = pricing.InputPerMTok.ToString(CultureInfo.InvariantCulture);
            _Cached.Text = pricing.CachedInputPerMTok.ToString(CultureInfo.InvariantCulture);
            _Output.Text = pricing.OutputPerMTok.ToString(CultureInfo.InvariantCulture);

            Content = BuildContent();
        }

        /// <summary>The (possibly renamed) model key as edited.</summary>
        public string ModelName { get; private set; }

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            StackPanel form = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
            form.Children.Add(Field("Model", _Model));
            form.Children.Add(Field("Input $/Mtok", _Input));
            form.Children.Add(Field("Cached input $/Mtok", _Cached));
            form.Children.Add(Field("Output $/Mtok", _Output));
            form.Children.Add(_Error);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button cancel = new Button { Content = "Cancel" };
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = "Save", Background = theme.AccentButton, Foreground = theme.AccentText };
            ok.Click += (sender, args) => Ok();
            buttons.Children.Add(ok);
            form.Children.Add(buttons);

            return form;
        }

        private Control Field(string label, Control control)
        {
            StackPanel panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = label, Foreground = AppTheme.Current.Text, FontWeight = FontWeight.SemiBold });
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.Children.Add(control);
            return panel;
        }

        private void Ok()
        {
            string model = (_Model.Text ?? string.Empty).Trim();
            if (model.Length == 0)
            {
                _Error.Text = "Model is required.";
                return;
            }

            if (!TryParse(_Input.Text, out double input) || !TryParse(_Cached.Text, out double cached) || !TryParse(_Output.Text, out double output))
            {
                _Error.Text = "Rates must be non-negative numbers.";
                return;
            }

            _Pricing.InputPerMTok = input;
            _Pricing.CachedInputPerMTok = cached;
            _Pricing.OutputPerMTok = output;
            ModelName = model;
            Close(true);
        }

        private static bool TryParse(string? text, out double value)
        {
            return double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0;
        }
    }
}
