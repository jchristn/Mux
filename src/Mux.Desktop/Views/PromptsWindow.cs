namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Models;
    using Mux.Core.Settings;

    /// <summary>
    /// A manager for prompt profiles (parity with the TUI's <c>/prompt</c>): a sortable table with per-row
    /// actions (edit, set-active, delete) and a green <c>active</c> badge on the active profile. Persists
    /// through <see cref="SettingsLoader.SavePrompts"/>.
    /// </summary>
    public sealed class PromptsWindow : Window
    {
        private readonly List<PromptProfile> _Prompts;
        private readonly DataTableView<PromptProfile> _Table;

        /// <summary>
        /// Instantiate the prompts manager.
        /// </summary>
        /// <param name="onChanged">Invoked after prompts are saved; null to ignore.</param>
        public PromptsWindow(Action? onChanged)
        {
            _ = onChanged;
            _Prompts = SettingsLoader.LoadPrompts();

            AppTheme theme = AppTheme.Current;

            Title = "Prompt profiles";
            Icon = IconResources.LoadWindowIcon();
            Width = 860;
            Height = 560;
            MinWidth = 640;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _Table = new DataTableView<PromptProfile>(BuildColumns(), BuildActions, OnEdit);
            Content = BuildLayout(theme);
            Refresh();
        }

        private static List<TableColumn<PromptProfile>> BuildColumns()
        {
            return new List<TableColumn<PromptProfile>>
            {
                new TableColumn<PromptProfile>("Name", p => string.IsNullOrEmpty(p.Name) ? "(unnamed)" : p.Name, new GridLength(2, GridUnitType.Star), p => p.Name, p => p.IsActive ? "active" : null, tooltip: "The profile's name. The green “active” badge marks the profile currently driving the agent's persona."),
                new TableColumn<PromptProfile>("System prompt", p => Preview(p.SystemPrompt), new GridLength(4, GridUnitType.Star), tooltip: "A preview of this profile's system prompt (the persona and instructions sent to the model).")
            };
        }

        private IReadOnlyList<TableRowAction<PromptProfile>> BuildActions(PromptProfile prompt)
        {
            List<TableRowAction<PromptProfile>> actions = new List<TableRowAction<PromptProfile>>
            {
                new TableRowAction<PromptProfile>("Edit", p => OnEdit(p))
            };

            if (!prompt.IsActive)
            {
                actions.Add(new TableRowAction<PromptProfile>("Set as active", p => OnSetActive(p)));
            }

            actions.Add(new TableRowAction<PromptProfile>("Delete", p => OnDelete(p), destructive: true));
            return actions;
        }

        private static string Preview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "(empty)";
            }

            string collapsed = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return collapsed.Length > 140 ? collapsed.Substring(0, 140) + "…" : collapsed;
        }

        private Control BuildLayout(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            DockPanel header = new DockPanel();
            TextBlock title = new TextBlock { Text = "Prompt profiles", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            Button add = new Button { Content = "＋  Add profile", Background = theme.AccentButton, Foreground = theme.AccentText, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 6, 12, 6) };
            add.Tip("Add a new prompt profile (a named system/tools-disabled/compaction prompt set).");
            add.Click += (sender, args) => OnAdd();
            DockPanel.SetDock(add, Dock.Right);
            header.Children.Add(add);

            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            _Table.Margin = new Thickness(0, 14, 0, 0);
            root.Children.Add(_Table);
            return root;
        }

        private void Refresh()
        {
            _Table.SetRows(_Prompts);
        }

        private async void OnAdd()
        {
            PromptProfile prompt = new PromptProfile();
            if (await new PromptProfileFormDialog(prompt, isNew: true).ShowDialog<bool>(this))
            {
                _Prompts.Add(prompt);
                Normalize(prompt);
                Persist();
            }
        }

        private async void OnEdit(PromptProfile prompt)
        {
            if (await new PromptProfileFormDialog(prompt, isNew: false).ShowDialog<bool>(this))
            {
                Normalize(prompt);
                Persist();
            }
        }

        private async void OnDelete(PromptProfile prompt)
        {
            bool confirmed = await new ConfirmDialog("Delete prompt profile", "Delete \"" + prompt.Name + "\"?", "Delete", destructive: true).ShowDialog<bool>(this);
            if (confirmed)
            {
                _Prompts.Remove(prompt);
                Persist();
            }
        }

        private void OnSetActive(PromptProfile prompt)
        {
            foreach (PromptProfile other in _Prompts)
            {
                other.IsActive = ReferenceEquals(other, prompt);
            }

            Persist();
        }

        private void Normalize(PromptProfile justEdited)
        {
            if (!justEdited.IsActive)
            {
                return;
            }

            foreach (PromptProfile other in _Prompts)
            {
                if (!ReferenceEquals(other, justEdited))
                {
                    other.IsActive = false;
                }
            }
        }

        private void Persist()
        {
            try
            {
                SettingsLoader.SavePrompts(_Prompts);
            }
            catch (Exception)
            {
                // Best-effort.
            }

            Refresh();
        }
    }
}
