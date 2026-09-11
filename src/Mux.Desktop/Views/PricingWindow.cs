namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Settings;
    using Mux.Core.Telemetry;

    /// <summary>
    /// An editor for the model pricing table (parity row 40): a sortable table of per-model input/cached/
    /// output rates, with add, edit, and delete. Persists through <see cref="SettingsLoader.SavePricing"/>.
    /// </summary>
    public sealed class PricingWindow : Window
    {
        private readonly PricingTable _Table;
        private readonly List<PricingRow> _Rows = new List<PricingRow>();
        private readonly DataTableView<PricingRow> _View;

        /// <summary>
        /// Instantiate the pricing editor.
        /// </summary>
        public PricingWindow()
        {
            _Table = SettingsLoader.LoadPricing();
            foreach (KeyValuePair<string, ModelPricing> pair in _Table.Models)
            {
                _Rows.Add(new PricingRow(pair.Key, pair.Value));
            }

            AppTheme theme = AppTheme.Current;

            Title = "Model pricing";
            Icon = IconResources.LoadWindowIcon();
            Width = 900;
            Height = 600;
            MinWidth = 640;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _View = new DataTableView<PricingRow>(BuildColumns(), BuildActions, OnEdit);
            Content = BuildLayout(theme);
            Refresh();
        }

        private static List<TableColumn<PricingRow>> BuildColumns()
        {
            return new List<TableColumn<PricingRow>>
            {
                new TableColumn<PricingRow>("Model", r => r.Model, new GridLength(3, GridUnitType.Star), r => r.Model),
                new TableColumn<PricingRow>("Input $/Mtok", r => Money(r.Pricing.InputPerMTok), new GridLength(1.5, GridUnitType.Star), r => r.Pricing.InputPerMTok),
                new TableColumn<PricingRow>("Cached $/Mtok", r => Money(r.Pricing.CachedInputPerMTok), new GridLength(1.5, GridUnitType.Star), r => r.Pricing.CachedInputPerMTok),
                new TableColumn<PricingRow>("Output $/Mtok", r => Money(r.Pricing.OutputPerMTok), new GridLength(1.5, GridUnitType.Star), r => r.Pricing.OutputPerMTok)
            };
        }

        private IReadOnlyList<TableRowAction<PricingRow>> BuildActions(PricingRow row)
        {
            return new List<TableRowAction<PricingRow>>
            {
                new TableRowAction<PricingRow>("Edit", r => OnEdit(r)),
                new TableRowAction<PricingRow>("Delete", r => OnDelete(r), destructive: true)
            };
        }

        private static string Money(double value)
        {
            return "$" + value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private Control BuildLayout(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            DockPanel header = new DockPanel();
            TextBlock title = new TextBlock { Text = "Model pricing", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            Button add = new Button { Content = "＋  Add model", Background = theme.AccentButton, Foreground = theme.AccentText, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 6, 12, 6) };
            add.Click += (sender, args) => OnAdd();
            DockPanel.SetDock(add, Dock.Right);
            header.Children.Add(add);

            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            _View.Margin = new Thickness(0, 14, 0, 0);
            root.Children.Add(_View);
            return root;
        }

        private void Refresh()
        {
            _View.SetRows(_Rows);
        }

        private async void OnAdd()
        {
            ModelPricing pricing = new ModelPricing();
            PricingFormDialog dialog = new PricingFormDialog(string.Empty, pricing, isNew: true);
            if (await dialog.ShowDialog<bool>(this))
            {
                _Rows.Add(new PricingRow(dialog.ModelName, pricing));
                Persist();
            }
        }

        private async void OnEdit(PricingRow row)
        {
            PricingFormDialog dialog = new PricingFormDialog(row.Model, row.Pricing, isNew: false);
            if (await dialog.ShowDialog<bool>(this))
            {
                row.Model = dialog.ModelName;
                Persist();
            }
        }

        private async void OnDelete(PricingRow row)
        {
            bool confirmed = await new ConfirmDialog("Delete pricing", "Delete pricing for \"" + row.Model + "\"?", "Delete", destructive: true).ShowDialog<bool>(this);
            if (confirmed)
            {
                _Rows.Remove(row);
                Persist();
            }
        }

        private void Persist()
        {
            _Table.Models.Clear();
            foreach (PricingRow row in _Rows)
            {
                if (!string.IsNullOrWhiteSpace(row.Model))
                {
                    _Table.Models[row.Model] = row.Pricing;
                }
            }

            try
            {
                SettingsLoader.SavePricing(_Table);
            }
            catch (Exception)
            {
                // Best-effort.
            }

            Refresh();
        }

        private sealed class PricingRow
        {
            public PricingRow(string model, ModelPricing pricing)
            {
                Model = model ?? string.Empty;
                Pricing = pricing ?? new ModelPricing();
            }

            public string Model { get; set; }

            public ModelPricing Pricing { get; }
        }
    }
}
