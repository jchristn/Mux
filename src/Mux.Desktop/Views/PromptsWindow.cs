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
    using Mux.Desktop.I18n;

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

            Title = Localizer.T("prompt.title");
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
                new TableColumn<PromptProfile>(Localizer.T("col.name"), p => string.IsNullOrEmpty(p.Name) ? Localizer.T("prompt.unnamed") : p.Name, new GridLength(2, GridUnitType.Star), p => p.Name, p => p.IsActive ? Localizer.T("prompt.active") : null, tooltip: Localizer.T("prompt.col.name.tip")),
                new TableColumn<PromptProfile>(Localizer.T("prompt.systemPrompt"), p => Preview(p.SystemPrompt), new GridLength(4, GridUnitType.Star), tooltip: Localizer.T("prompt.col.systemPrompt.tip"))
            };
        }

        private IReadOnlyList<TableRowAction<PromptProfile>> BuildActions(PromptProfile prompt)
        {
            List<TableRowAction<PromptProfile>> actions = new List<TableRowAction<PromptProfile>>
            {
                new TableRowAction<PromptProfile>(Localizer.T("act.edit"), p => OnEdit(p))
            };

            if (!prompt.IsActive)
            {
                actions.Add(new TableRowAction<PromptProfile>(Localizer.T("prompt.setActive"), p => OnSetActive(p)));
            }

            actions.Add(new TableRowAction<PromptProfile>(Localizer.T("act.delete"), p => OnDelete(p), destructive: true));
            return actions;
        }

        private static string Preview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Localizer.T("prompt.empty");
            }

            string collapsed = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return collapsed.Length > 140 ? collapsed.Substring(0, 140) + "…" : collapsed;
        }

        private Control BuildLayout(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            DockPanel header = new DockPanel();
            TextBlock title = new TextBlock { Text = Localizer.T("prompt.title"), FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            Button add = new Button { Content = "＋  " + Localizer.T("prompt.add"), Background = theme.AccentButton, Foreground = theme.AccentText, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 6, 12, 6) };
            add.Tip(Localizer.T("prompt.add.tip"));
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
            bool confirmed = await new ConfirmDialog(Localizer.T("prompt.delete.title"), string.Format(Localizer.T("prompt.delete.confirm"), prompt.Name), Localizer.T("act.delete"), destructive: true).ShowDialog<bool>(this);
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
