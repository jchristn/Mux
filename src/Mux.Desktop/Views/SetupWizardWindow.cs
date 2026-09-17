namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Desktop.I18n;

    /// <summary>
    /// The first-run setup wizard for mux Desktop. Mirrors the TUI's guided flow: a welcome step, defining the
    /// first endpoint (reusing <see cref="EndpointFormDialog"/>), a live connectivity check (reusing
    /// <see cref="EndpointValidationWindow"/>), and a closing step that sends the user to the composer. The
    /// endpoint is persisted through <see cref="SettingsLoader"/> and set as the default when it is the first
    /// one configured. Finishing or explicitly skipping records <see cref="MuxSettings.SetupCompleted"/> so the
    /// wizard never nags again; a mid-wizard cancel leaves the flag unset so an unconfigured user is offered it
    /// again on the next launch. Shown modally over the main window; inspect <see cref="EndpointCreated"/> after
    /// it closes to know whether the model picker should refresh.
    /// </summary>
    public sealed class SetupWizardWindow : Window
    {
        private readonly ContentControl _StepHost;
        private bool _EndpointCreated;

        /// <summary>
        /// Instantiate the setup wizard on its welcome step.
        /// </summary>
        public SetupWizardWindow()
        {
            AppTheme theme = AppTheme.Current;

            Title = Localizer.T("setup.wizard.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 560;
            MinWidth = 460;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _StepHost = new ContentControl();
            Content = _StepHost;
            ShowWelcomeStep();
        }

        /// <summary>
        /// Gets whether the wizard created and saved a new endpoint. When true, the caller should refresh any
        /// model picker and focus the composer so the user can send a first message immediately.
        /// </summary>
        public bool EndpointCreated
        {
            get => _EndpointCreated;
        }

        // Renders the welcome step: an explanation and the "set up now" / "skip for now" choice.
        private void ShowWelcomeStep()
        {
            AppTheme theme = AppTheme.Current;

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };

            Button skip = new Button { Content = Localizer.T("setup.wizard.welcome.skip") };
            skip.Tip(Localizer.T("setup.wizard.welcome.skip.tip"));
            skip.Click += (sender, args) => OnSkip();
            buttons.Children.Add(skip);

            Button setUp = new Button { Content = Localizer.T("setup.wizard.welcome.setup"), Background = theme.AccentButton, Foreground = theme.AccentText };
            setUp.Tip(Localizer.T("setup.wizard.welcome.setup.tip"));
            setUp.Click += (sender, args) => OnSetUpNow();
            buttons.Children.Add(setUp);

            _StepHost.Content = BuildStep(
                "🚀",
                Localizer.T("setup.wizard.welcome.heading"),
                Localizer.T("setup.wizard.welcome.body"),
                buttons);
        }

        // Renders the closing step: confirmation and a button that returns focus to the composer.
        private void ShowFinishStep()
        {
            AppTheme theme = AppTheme.Current;

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button start = new Button { Content = Localizer.T("setup.wizard.finish.start"), Background = theme.AccentButton, Foreground = theme.AccentText };
            start.Tip(Localizer.T("setup.wizard.finish.start.tip"));
            start.Click += (sender, args) => OnFinish();
            buttons.Children.Add(start);

            _StepHost.Content = BuildStep(
                "✓",
                Localizer.T("setup.wizard.finish.heading"),
                Localizer.T("setup.wizard.finish.body"),
                buttons);
        }

        // Assembles one wizard step: a glyph + heading row, a wrapped body paragraph, and a button row.
        private Control BuildStep(string glyph, string heading, string body, Control buttons)
        {
            AppTheme theme = AppTheme.Current;
            StackPanel panel = new StackPanel { Margin = new Thickness(28), Spacing = 16 };

            StackPanel headingRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            headingRow.Children.Add(new TextBlock { Text = glyph, FontSize = 22, Foreground = theme.Accent, VerticalAlignment = VerticalAlignment.Center });
            headingRow.Children.Add(new TextBlock { Text = heading, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(headingRow);

            panel.Children.Add(new TextBlock { Text = body, FontSize = 13, Foreground = theme.Muted, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
            panel.Children.Add(buttons);
            return panel;
        }

        // Skip: remember the choice so the wizard is not shown automatically again, then close.
        private void OnSkip()
        {
            MarkSetupComplete();
            Close();
        }

        // Finish: the endpoint is already saved; record completion and close so the caller focuses the composer.
        private void OnFinish()
        {
            MarkSetupComplete();
            Close();
        }

        // Define the first endpoint (reusing the endpoint form), persist it, then validate connectivity. A
        // cancelled form ends the wizard WITHOUT marking setup complete, so it is offered again next launch.
        private async void OnSetUpNow()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = string.Empty,
                AdapterType = AdapterTypeEnum.Ollama,
                BaseUrl = "http://localhost:11434",
                Model = string.Empty,
                MaxTokens = 8192,
                Temperature = 0.1,
                ContextWindow = 32768
            };

            bool saved = await new EndpointFormDialog(endpoint, isNew: true).ShowDialog<bool>(this);
            if (!saved)
            {
                Close();
                return;
            }

            SaveEndpoint(endpoint);
            _EndpointCreated = true;

            await new EndpointValidationWindow(endpoint).ShowDialog(this);
            ShowFinishStep();
        }

        // Persists the new endpoint through the shared settings loader. When it is the first endpoint, or when
        // the user marked it default in the form, it becomes the sole default. Best-effort: a save failure is
        // swallowed here (the endpoints window surfaces persistence errors on its own).
        private void SaveEndpoint(EndpointConfig endpoint)
        {
            try
            {
                List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
                if (endpoints.Count == 0)
                {
                    endpoint.IsDefault = true;
                }

                endpoints.Add(endpoint);

                if (endpoint.IsDefault)
                {
                    foreach (EndpointConfig other in endpoints)
                    {
                        other.IsDefault = ReferenceEquals(other, endpoint);
                    }
                }

                SettingsLoader.SaveEndpoints(endpoints);
            }
            catch (Exception)
            {
                // Best-effort; a failed write simply means the endpoint is not persisted.
            }
        }

        // Records SetupCompleted = true so the wizard is not shown automatically again. Best-effort: a settings
        // read/write failure is ignored so it never blocks closing the wizard.
        private void MarkSetupComplete()
        {
            try
            {
                MuxSettings settings = SettingsLoader.LoadSettings();
                if (!settings.SetupCompleted)
                {
                    settings.SetupCompleted = true;
                    SettingsLoader.SaveSettings(settings);
                }
            }
            catch (Exception)
            {
                // Best-effort; the wizard may simply be offered again next launch.
            }
        }
    }
}
