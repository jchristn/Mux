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
    using Mux.Desktop.Services;

    /// <summary>
    /// Lets the user select several conversations and delete them at once. Each conversation gets a checkbox;
    /// the "Delete multiple" button confirms "Are you sure you wish to delete {count} conversation(s)?" and
    /// then deletes the selected threads. Returns the deleted thread ids via
    /// <c>ShowDialog&lt;List&lt;string&gt;?&gt;</c> (null when nothing was deleted) so the shell can refresh.
    /// </summary>
    public sealed class BulkDeleteConversationsWindow : Window
    {
        private readonly IThreadService _Threads;
        private readonly StackPanel _List = new StackPanel { Spacing = 2 };
        private readonly List<CheckBox> _Boxes = new List<CheckBox>();
        private readonly TextBlock _Status;
        private readonly Button _DeleteButton;

        /// <summary>
        /// Instantiate the bulk-delete window.
        /// </summary>
        /// <param name="threads">The thread service used to list and delete conversations. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="threads"/> is null.</exception>
        public BulkDeleteConversationsWindow(IThreadService threads)
        {
            _Threads = threads ?? throw new ArgumentNullException(nameof(threads));

            AppTheme theme = AppTheme.Current;

            Title = "Delete conversations";
            Icon = IconResources.LoadWindowIcon();
            Width = 640;
            Height = 620;
            MinWidth = 460;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            _Status = new TextBlock { Text = "Loading…", Foreground = theme.Muted, FontSize = 12 };
            _DeleteButton = new Button { Content = "Delete multiple", Background = theme.Error, Foreground = Brushes.White, IsEnabled = false };
            _DeleteButton.Tip("Delete every checked conversation, after a confirmation.");
            _DeleteButton.Click += (sender, args) => OnDelete();

            Content = BuildContent(theme);
            _ = LoadAsync();
        }

        private Control BuildContent(AppTheme theme)
        {
            DockPanel root = new DockPanel { Margin = new Thickness(20) };

            StackPanel head = new StackPanel { Spacing = 4 };
            head.Children.Add(new TextBlock { Text = "Delete conversations", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text });
            head.Children.Add(_Status);

            StackPanel selectRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
            Button all = new Button { Content = "Select all", Padding = new Thickness(10, 4, 10, 4) };
            all.Tip("Check every conversation.");
            all.Click += (sender, args) => SetAll(true);
            selectRow.Children.Add(all);
            Button none = new Button { Content = "Select none", Padding = new Thickness(10, 4, 10, 4) };
            none.Tip("Uncheck every conversation.");
            none.Click += (sender, args) => SetAll(false);
            selectRow.Children.Add(none);
            head.Children.Add(selectRow);

            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            Button cancel = new Button { Content = "Cancel" };
            cancel.Tip("Close without deleting anything.");
            cancel.Click += (sender, args) => Close(null);
            buttons.Children.Add(cancel);
            buttons.Children.Add(_DeleteButton);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            root.Children.Add(new ScrollViewer { Content = _List, Margin = new Thickness(0, 12, 0, 0) });
            return root;
        }

        private async Task LoadAsync()
        {
            try
            {
                IReadOnlyList<ThreadSummary> threads = await _Threads.ListAsync(CancellationToken.None);
                _List.Children.Clear();
                _Boxes.Clear();

                foreach (ThreadSummary thread in threads)
                {
                    CheckBox box = new CheckBox { Content = DisplayTitle(thread), Tag = thread.Id, Padding = new Thickness(6, 3, 6, 3) };
                    box.IsCheckedChanged += (sender, args) => UpdateDeleteEnabled();
                    _Boxes.Add(box);
                    _List.Children.Add(box);
                }

                _Status.Text = threads.Count == 0 ? "You have no conversations." : threads.Count + " conversation" + (threads.Count == 1 ? string.Empty : "s") + ". Check the ones to delete.";
                UpdateDeleteEnabled();
            }
            catch (Exception exception)
            {
                _Status.Foreground = AppTheme.Current.Error;
                _Status.Text = "Could not load conversations: " + exception.Message;
            }
        }

        private void SetAll(bool value)
        {
            foreach (CheckBox box in _Boxes)
            {
                box.IsChecked = value;
            }
        }

        private void UpdateDeleteEnabled()
        {
            _DeleteButton.IsEnabled = SelectedIds().Count > 0;
        }

        private List<string> SelectedIds()
        {
            List<string> ids = new List<string>();
            foreach (CheckBox box in _Boxes)
            {
                if (box.IsChecked == true && box.Tag is string id)
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        private async void OnDelete()
        {
            List<string> ids = SelectedIds();
            if (ids.Count == 0)
            {
                return;
            }

            bool confirmed = await new ConfirmDialog(
                "Delete conversations",
                "Are you sure you wish to delete " + ids.Count + " conversation" + (ids.Count == 1 ? string.Empty : "s") + "?",
                "Delete",
                destructive: true).ShowDialog<bool>(this);
            if (!confirmed)
            {
                return;
            }

            List<string> deleted = new List<string>();
            foreach (string id in ids)
            {
                try
                {
                    await _Threads.DeleteAsync(id, CancellationToken.None);
                    deleted.Add(id);
                }
                catch (Exception)
                {
                    // Skip one that fails to delete; continue with the rest.
                }
            }

            Close(deleted.Count > 0 ? deleted : null);
        }

        private static string DisplayTitle(ThreadSummary thread)
        {
            return string.IsNullOrWhiteSpace(thread.Title) ? "Untitled conversation" : thread.Title;
        }
    }
}
