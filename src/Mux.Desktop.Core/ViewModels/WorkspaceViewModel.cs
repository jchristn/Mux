namespace Mux.Desktop.ViewModels
{
    using System;
    using System.Collections.ObjectModel;
    using CommunityToolkit.Mvvm.ComponentModel;

    /// <summary>
    /// View model for the tabbed workspace: the set of currently open conversation tabs and the active one.
    /// Opening a thread that already has a tab focuses it rather than duplicating; closing the active tab
    /// falls back to another open tab. Focus is exclusive — activating a tab deactivates the previously
    /// active one — which drives each tab's attention model (see <see cref="WorkspaceTabViewModel"/>).
    /// </summary>
    public sealed class WorkspaceViewModel : ObservableObject
    {
        private readonly ObservableCollection<WorkspaceTabViewModel> _Tabs = new ObservableCollection<WorkspaceTabViewModel>();
        private WorkspaceTabViewModel? _ActiveTab;

        /// <summary>The currently open tabs, in open order.</summary>
        public ObservableCollection<WorkspaceTabViewModel> Tabs
        {
            get => _Tabs;
        }

        /// <summary>The active (focused) tab, or null when no tabs are open.</summary>
        public WorkspaceTabViewModel? ActiveTab
        {
            get => _ActiveTab;
            private set => SetProperty(ref _ActiveTab, value);
        }

        /// <summary>Whether any tabs are open.</summary>
        public bool HasTabs
        {
            get => _Tabs.Count > 0;
        }

        /// <summary>
        /// Open a tab for a thread, or focus its existing tab when one is already open.
        /// </summary>
        /// <param name="id">The thread/session id. Required.</param>
        /// <param name="title">The tab title (used only when creating a new tab). Required.</param>
        /// <returns>The opened or focused tab.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public WorkspaceTabViewModel OpenTab(string id, string title)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(title);

            WorkspaceTabViewModel? existing = FindById(id);
            if (existing != null)
            {
                SelectTab(existing);
                return existing;
            }

            WorkspaceTabViewModel tab = new WorkspaceTabViewModel(id, title);
            _Tabs.Add(tab);
            SelectTab(tab);
            OnPropertyChanged(nameof(HasTabs));
            return tab;
        }

        /// <summary>
        /// Focus a tab, deactivating the previously active one. A tab not in the workspace is ignored.
        /// </summary>
        /// <param name="tab">The tab to focus. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tab"/> is null.</exception>
        public void SelectTab(WorkspaceTabViewModel tab)
        {
            ArgumentNullException.ThrowIfNull(tab);

            if (!_Tabs.Contains(tab) || ReferenceEquals(tab, _ActiveTab))
            {
                if (ReferenceEquals(tab, _ActiveTab))
                {
                    tab.Activate();
                }

                return;
            }

            _ActiveTab?.Deactivate();
            ActiveTab = tab;
            tab.Activate();
        }

        /// <summary>
        /// Close a tab. When the active tab is closed, the last remaining tab becomes active (or none).
        /// </summary>
        /// <param name="tab">The tab to close. Required.</param>
        /// <returns>True when the tab was open and removed; false when it was not in the workspace.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tab"/> is null.</exception>
        public bool CloseTab(WorkspaceTabViewModel tab)
        {
            ArgumentNullException.ThrowIfNull(tab);

            int index = _Tabs.IndexOf(tab);
            if (index < 0)
            {
                return false;
            }

            bool wasActive = ReferenceEquals(tab, _ActiveTab);
            _Tabs.Remove(tab);

            if (wasActive)
            {
                if (_Tabs.Count > 0)
                {
                    WorkspaceTabViewModel next = _Tabs[_Tabs.Count - 1];
                    ActiveTab = next;
                    next.Activate();
                }
                else
                {
                    ActiveTab = null;
                }
            }

            OnPropertyChanged(nameof(HasTabs));
            return true;
        }

        private WorkspaceTabViewModel? FindById(string id)
        {
            foreach (WorkspaceTabViewModel tab in _Tabs)
            {
                if (string.Equals(tab.Id, id, StringComparison.Ordinal))
                {
                    return tab;
                }
            }

            return null;
        }
    }
}
