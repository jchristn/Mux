namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Settings;
    using Mux.Desktop.Services;

    /// <summary>
    /// The keybindings editor (parity row 30): a sortable table of the shared command catalog showing each
    /// command's effective key chord, with rebind / unbind / reset-to-default row actions. Edits layer over the
    /// defaults through a <see cref="KeybindingEditorModel"/> and persist to <c>keybindings.json</c> via
    /// <see cref="SettingsLoader.SaveKeybindings"/>, so the overrides apply to the TUI as well.
    /// </summary>
    public sealed class KeybindingsWindow : Window
    {
        private readonly KeybindingEditorModel _Model;
        private readonly DataTableView<KeybindingCommand> _Table;

        /// <summary>
        /// Instantiate the keybindings editor.
        /// </summary>
        public KeybindingsWindow()
        {
            _Model = new KeybindingEditorModel(SettingsLoader.LoadKeybindings());

            AppTheme theme = AppTheme.Current;

            Title = "Keybindings";
            Icon = IconResources.LoadWindowIcon();
            Width = 820;
            Height = 640;
            MinWidth = 620;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _Table = new DataTableView<KeybindingCommand>(BuildColumns(), BuildActions, OnRebind);
            Content = BuildLayout(theme);
            Refresh();
        }

        private List<TableColumn<KeybindingCommand>> BuildColumns()
        {
            return new List<TableColumn<KeybindingCommand>>
            {
                new TableColumn<KeybindingCommand>("Command", c => c.Title, new GridLength(3, GridUnitType.Star), c => c.Title, tooltip: "The command this chord runs."),
                new TableColumn<KeybindingCommand>("Category", c => c.Category, new GridLength(1.4, GridUnitType.Star), c => c.Category, tooltip: "The command's group."),
                new TableColumn<KeybindingCommand>("Chord", c => ChordText(c.Id), new GridLength(2, GridUnitType.Star), c => ChordText(c.Id), badge: c => _Model.IsOverridden(c.Id) ? "custom" : null, tooltip: "The effective key chord; a \"custom\" badge marks an override.")
            };
        }

        private IReadOnlyList<TableRowAction<KeybindingCommand>> BuildActions(KeybindingCommand command)
        {
            return new List<TableRowAction<KeybindingCommand>>
            {
                new TableRowAction<KeybindingCommand>("Rebind…", c => OnRebind(c)),
                new TableRowAction<KeybindingCommand>("Unbind", c => OnUnbind(c)),
                new TableRowAction<KeybindingCommand>("Reset to default", c => OnReset(c))
            };
        }

        private Control BuildLayout(AppTheme theme)
        {
            Grid root = new Grid
            {
                Margin = new Thickness(20),
                RowDefinitions = new RowDefinitions("Auto,*")
            };

            DockPanel header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            Button resetAll = new Button { Content = "Reset all", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(12, 6, 12, 6) };
            resetAll.Tip("Clear every override and restore the shipped defaults.");
            resetAll.Click += (sender, args) => OnResetAll();
            DockPanel.SetDock(resetAll, Dock.Right);
            header.Children.Add(resetAll);

            StackPanel text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = "Keyboard shortcuts", FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = theme.Text });
            text.Children.Add(new TextBlock { Text = "Rebind or unbind commands by id. Overrides are saved to keybindings.json and shared with the terminal.", FontSize = 12, Foreground = theme.Muted, TextWrapping = TextWrapping.Wrap });
            header.Children.Add(text);

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            Grid.SetRow(_Table, 1);
            root.Children.Add(_Table);

            return root;
        }

        private string ChordText(string id)
        {
            string? chord = _Model.EffectiveChord(id);
            return string.IsNullOrEmpty(chord) ? "—" : chord;
        }

        private void Refresh()
        {
            _Table.SetRows(KeybindingCatalog.Commands);
        }

        private async void OnRebind(KeybindingCommand command)
        {
            string? chord = await new KeyChordCaptureDialog(command.Title, _Model.EffectiveChord(command.Id)).ShowDialog<string?>(this);
            if (chord == null)
            {
                return;
            }

            string? conflictId = _Model.FindConflict(command.Id, chord);
            if (conflictId != null)
            {
                KeybindingCommand? other = KeybindingCatalog.Find(conflictId);
                bool proceed = await new ConfirmDialog(
                    "Chord already in use",
                    chord + " is already bound to \"" + (other?.Title ?? conflictId) + "\". Rebind anyway? Both commands will share this chord.",
                    "Rebind",
                    destructive: false).ShowDialog<bool>(this);
                if (!proceed)
                {
                    return;
                }
            }

            _Model.SetChord(command.Id, chord);
            Persist();
        }

        private void OnUnbind(KeybindingCommand command)
        {
            _Model.Unbind(command.Id);
            Persist();
        }

        private void OnReset(KeybindingCommand command)
        {
            _Model.Reset(command.Id);
            Persist();
        }

        private async void OnResetAll()
        {
            if (!await new ConfirmDialog("Reset all keybindings", "Clear every override and restore the shipped defaults?", "Reset all", destructive: true).ShowDialog<bool>(this))
            {
                return;
            }

            foreach (KeybindingCommand command in KeybindingCatalog.Commands)
            {
                _Model.Reset(command.Id);
            }

            Persist();
        }

        private void Persist()
        {
            try
            {
                SettingsLoader.SaveKeybindings(new Dictionary<string, string?>(_Model.Overrides, StringComparer.Ordinal));
            }
            catch (Exception)
            {
                // Best-effort persistence.
            }

            Refresh();
        }
    }
}
