namespace Mux.Desktop.Views
{
    using System;
    using System.Threading;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.Primitives;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Threading;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A modal that validates a single endpoint's model live: it opens showing "Validating…", runs the shared
    /// <see cref="LlmClient.LoadModelAsync(Mux.Core.Models.EndpointConfig, bool, System.Threading.CancellationToken)"/>
    /// health check (stream, stop at the first token), then updates to a
    /// ✓ ready / ⚠ reachable / ✗ unreachable result with the full, selectable error text so the user can read
    /// or copy exactly what the backend reported.
    /// </summary>
    public sealed class EndpointValidationWindow : Window
    {
        private readonly EndpointConfig _Endpoint;
        private readonly TextBlock _StatusLine;
        private readonly SelectableTextBlock _Detail;
        private readonly Button _Close;
        private CancellationTokenSource? _Cts;

        /// <summary>
        /// Instantiate the validation window for an endpoint and immediately begin validating.
        /// </summary>
        /// <param name="endpoint">The endpoint to validate. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public EndpointValidationWindow(EndpointConfig endpoint)
        {
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));

            AppTheme theme = AppTheme.Current;

            Title = Localizer.T("endpointVal.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 560;
            MinWidth = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            StackPanel panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };

            string label = string.IsNullOrWhiteSpace(endpoint.Model) ? endpoint.Name : endpoint.Model;
            panel.Children.Add(new TextBlock { Text = endpoint.Name, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = theme.Text });
            panel.Children.Add(new TextBlock { Text = endpoint.AdapterType + " · " + label + "  —  " + endpoint.BaseUrl, FontSize = 12, Foreground = theme.Muted, TextWrapping = TextWrapping.Wrap });

            _StatusLine = new TextBlock { Text = Localizer.T("endpointVal.validating"), FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = theme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            panel.Children.Add(_StatusLine);

            _Detail = new SelectableTextBlock
            {
                Text = Localizer.T("endpointVal.probing"),
                FontSize = 12,
                Foreground = theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace")
            };
            panel.Children.Add(new Border
            {
                Background = theme.SurfaceAlt,
                BorderBrush = theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Child = new ScrollViewer { MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _Detail }
            });

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button revalidate = new Button { Content = Localizer.T("endpointVal.revalidate") };
            revalidate.Tip(Localizer.T("endpointVal.revalidate.tip"));
            revalidate.Click += (sender, args) => Start();
            buttons.Children.Add(revalidate);

            _Close = new Button { Content = Localizer.T("act.close"), Background = theme.AccentButton, Foreground = theme.AccentText };
            _Close.Tip(Localizer.T("endpointVal.close.tip"));
            _Close.Click += (sender, args) => Close();
            buttons.Children.Add(_Close);
            panel.Children.Add(buttons);

            Content = panel;

            Opened += (sender, args) => Start();
            Closing += (sender, args) => _Cts?.Cancel();
        }

        private async void Start()
        {
            _Cts?.Cancel();
            _Cts = new CancellationTokenSource();
            CancellationToken token = _Cts.Token;

            AppTheme theme = AppTheme.Current;
            _StatusLine.Foreground = theme.Muted;
            _StatusLine.Text = Localizer.T("endpointVal.validating");
            _Detail.Foreground = theme.Muted;
            _Detail.Text = Localizer.T("endpointVal.probingStream");

            bool ignoreCert = false;
            try
            {
                ignoreCert = SettingsLoader.LoadSettings().IgnoreCertErrors;
            }
            catch (Exception)
            {
                // Use the default on any settings read failure.
            }

            ModelLoadResult result;
            try
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                result = await LlmClient.LoadModelAsync(_Endpoint, ignoreCert, timeout.Token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                result = ModelLoadResult.Fail(exception.Message);
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            Dispatcher.UIThread.Post(() => Render(result));
        }

        private void Render(ModelLoadResult result)
        {
            AppTheme theme = AppTheme.Current;
            string label = string.IsNullOrWhiteSpace(_Endpoint.Model) ? _Endpoint.Name : _Endpoint.Model;

            if (result.Success)
            {
                _StatusLine.Foreground = theme.Success;
                _StatusLine.Text = Localizer.T("endpointVal.ready");
                _Detail.Foreground = theme.Text;
                _Detail.Text = label + Localizer.T("endpointVal.readyDetail");
            }
            else if (result.Reachable)
            {
                _StatusLine.Foreground = new SolidColorBrush(Color.Parse("#bf8700"));
                _StatusLine.Text = Localizer.T("endpointVal.reachable");
                _Detail.Foreground = theme.Text;
                _Detail.Text = (result.Error ?? Localizer.T("endpointVal.unknownError"))
                    + "\n\n" + Localizer.T("endpointVal.reachableDetail");
            }
            else
            {
                _StatusLine.Foreground = theme.Error;
                _StatusLine.Text = Localizer.T("endpointVal.unreachable");
                _Detail.Foreground = theme.Text;
                _Detail.Text = (result.Error ?? Localizer.T("endpointVal.unknownError"))
                    + "\n\n" + Localizer.T("endpointVal.unreachableDetail");
            }
        }
    }
}
