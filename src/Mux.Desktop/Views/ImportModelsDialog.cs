namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Threading;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Utility;

    /// <summary>
    /// Discovers the models advertised by an endpoint's backend (parity row 15 — "Import models from Ollama",
    /// generalized to any endpoint via <see cref="EndpointModelLister"/>) and lets the user multi-select which
    /// to add. Models already configured are shown but unchecked. Returns the selected model ids via
    /// <c>ShowDialog&lt;List&lt;string&gt;?&gt;</c> (null when cancelled).
    /// </summary>
    public sealed class ImportModelsDialog : Window
    {
        private readonly EndpointConfig _Source;
        private readonly HashSet<string> _Existing;
        private readonly StackPanel _List = new StackPanel { Spacing = 4 };
        private readonly List<CheckBox> _Boxes = new List<CheckBox>();
        private readonly TextBlock _Status;
        private readonly Button _Import;

        /// <summary>
        /// Instantiate and start discovering models.
        /// </summary>
        /// <param name="source">The endpoint whose backend is queried. Required.</param>
        /// <param name="existingModels">Model ids already configured (marked and left unchecked).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
        public ImportModelsDialog(EndpointConfig source, IEnumerable<string> existingModels)
        {
            _Source = source ?? throw new ArgumentNullException(nameof(source));
            _Existing = new HashSet<string>(existingModels ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            AppTheme theme = AppTheme.Current;

            Title = "Import models";
            Icon = IconResources.LoadWindowIcon();
            Width = 560;
            Height = 600;
            MinWidth = 460;
            MinHeight = 400;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _Status = new TextBlock { Text = "Querying " + source.Name + "…", Foreground = theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            _Import = new Button { Content = "Import selected", Background = theme.AccentButton, Foreground = theme.AccentText, IsEnabled = false };
            _Import.Tip("Create a new endpoint for each checked model, cloning this endpoint's connection settings.");
            _Import.Click += (sender, args) => OnImport();

            Content = BuildContent(theme);
            _ = LoadAsync();
        }

        private Control BuildContent(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            StackPanel head = new StackPanel { Spacing = 4 };
            head.Children.Add(new TextBlock { Text = "Import models from " + _Source.Name, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = theme.Text });
            head.Children.Add(_Status);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            Button cancel = new Button { Content = "Cancel" };
            cancel.Tip("Close without importing.");
            cancel.Click += (sender, args) => Close(null);
            buttons.Children.Add(cancel);
            buttons.Children.Add(_Import);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            root.Children.Add(new ScrollViewer { Content = _List, Margin = new Thickness(0, 12, 0, 0) });
            return root;
        }

        private async Task LoadAsync()
        {
            bool ignoreCert = false;
            try
            {
                ignoreCert = SettingsLoader.LoadSettings().IgnoreCertErrors;
            }
            catch (Exception)
            {
                // Use the default on any settings read failure.
            }

            EndpointModelListResult result;
            try
            {
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                result = await EndpointModelLister.ListModelsAsync(_Source, ignoreCert, cts.Token);
            }
            catch (Exception exception)
            {
                result = EndpointModelListResult.Failed("error", exception.Message);
            }

            Dispatcher.UIThread.Post(() => Render(result));
        }

        private void Render(EndpointModelListResult result)
        {
            AppTheme theme = AppTheme.Current;
            _List.Children.Clear();
            _Boxes.Clear();

            if (!result.Success)
            {
                _Status.Foreground = theme.Error;
                _Status.Text = "Could not list models: " + result.ErrorMessage + " (" + result.ErrorCode + ")";
                return;
            }

            List<string> models = new List<string>(result.Models);
            models.Sort(StringComparer.OrdinalIgnoreCase);

            int fresh = 0;
            foreach (string model in models)
            {
                bool exists = _Existing.Contains(model);
                if (!exists)
                {
                    fresh++;
                }

                CheckBox box = new CheckBox
                {
                    Content = exists ? model + "   (already configured)" : model,
                    IsChecked = !exists,
                    Tag = model,
                    Foreground = exists ? theme.Muted : theme.Text
                };
                box.IsCheckedChanged += (sender, args) => UpdateImportEnabled();
                _Boxes.Add(box);
                _List.Children.Add(box);
            }

            if (models.Count == 0)
            {
                _Status.Text = "The backend reported no models.";
                return;
            }

            _Status.Foreground = theme.Muted;
            _Status.Text = "Found " + models.Count + " model" + (models.Count == 1 ? string.Empty : "s") + " · " + fresh + " new. Check the ones to add.";
            UpdateImportEnabled();
        }

        private void UpdateImportEnabled()
        {
            foreach (CheckBox box in _Boxes)
            {
                if (box.IsChecked == true)
                {
                    _Import.IsEnabled = true;
                    return;
                }
            }

            _Import.IsEnabled = false;
        }

        private void OnImport()
        {
            List<string> selected = new List<string>();
            foreach (CheckBox box in _Boxes)
            {
                if (box.IsChecked == true && box.Tag is string model)
                {
                    selected.Add(model);
                }
            }

            Close(selected.Count > 0 ? selected : null);
        }
    }
}
