namespace Mux.Desktop.Shell
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.Primitives;
    using Avalonia.Controls.Templates;
    using Avalonia.Input;
    using Avalonia.Input.Platform;
    using Avalonia.Interactivity;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Styling;
    using Avalonia.Threading;
    using Mux.Core.Agent;
    using Mux.Core.Checkpoints;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Tasks;
    using Mux.Core.Telemetry;
    using Mux.Core.Utility;
    using Mux.Core.Conversation;
    using Mux.Desktop.Conversation;
    using Mux.Desktop.I18n;
    using Mux.Desktop.Services;
    using Mux.Desktop.ViewModels;
    using Mux.Desktop.Views;

    /// <summary>
    /// The chat shell: a conversation sidebar, a header with a model picker, theme toggle, settings, and
    /// About, a streaming transcript (collapsible Thinking panel and tool cards, response bubbles, per-turn
    /// metrics, wait-state quips), and a composer. Colors follow the active <see cref="AppTheme"/> (mux-green
    /// accent, light or dark). Sending drives the mux agent in-process through <see cref="AgentLoopTurnRunner"/>.
    /// </summary>
    public sealed class MainWindow : Window
    {
        private const string UntitledTitle = "Untitled conversation";
        private const string Disclaimer = "AI can make mistakes. Verify answers.";

        private readonly ILocalizationService _Localization;
        private readonly IThreadService _Threads;
        private readonly SessionStore _Store;
        private readonly AgentLoopTurnRunner _Runner;
        private readonly string _ConfigDirectory;
        private readonly UsageQueryService? _UsageQuery;

        private AppTheme _Theme = AppTheme.Current;
        private StackPanel _ThreadListPanel = null!;
        private ComboBox _ModelPicker = null!;
        private TextBlock _ModelStatus = null!;
        private TextBlock _ContextIndicator = null!;
        private int _ModelValidationSeq;
        private TextBlock _TitleText = null!;
        private StackPanel _Transcript = null!;
        private ScrollViewer _TranscriptScroll = null!;
        private StackPanel _EmptyState = null!;
        private StackPanel _OverviewHost = null!;
        private TextBlock _QuipText = null!;
        private TextBox _Composer = null!;
        private Button _SendButton = null!;
        private Button _UndoButton = null!;
        private Button _RedoButton = null!;

        private CheckpointManager? _Checkpoints;
        private Task? _CheckpointProbe;
        private readonly WorkspaceViewModel _Workspace = new WorkspaceViewModel();
        private readonly Dictionary<string, StackPanel> _TabTranscripts = new Dictionary<string, StackPanel>(StringComparer.Ordinal);
        private Border _TabStripHost = null!;
        private ConversationService? _Conversation;
        private TextBlock? _StreamingBlock;
        private Border? _AssistantBorder;
        private Border? _AssistantContentHost;
        private StackPanel? _TaskPlanBody;
        private CollapsibleSection? _ThinkingSection;
        private TextBlock? _ThinkingText;
        private Border? _PendingBubble;
        private TextBlock? _PendingText;
        private DispatcherTimer? _QuipTimer;
        private int _QuipIndex;
        private readonly Dictionary<string, ToolCardView> _ToolCards = new Dictionary<string, ToolCardView>(StringComparer.Ordinal);
        private Stopwatch? _TurnStopwatch;
        private long? _TurnTtftMs;
        private int _LastEstimatedTokens;
        private CancellationTokenSource? _TurnCts;
        private string _CurrentThreadId = string.Empty;
        private string _ThemeMode = "dark";
        private bool _ThemeSubscribed;
        private readonly PromptHistory _PromptHistory = new PromptHistory();
        private bool _SuppressHistoryReset;
        private string _CurrentTitle = string.Empty;
        private bool _CurrentTitlePinned;
        private bool _TitleSummarized;

        private const int TitleSummaryThreshold = 250;
        private DateTime _CurrentCreatedUtc;
        private bool _SidebarCollapsed;
        private bool _AutoExpandThinking;
        private bool _ConversationsOpen = true;
        private bool _ManageOpen;
        private ColumnDefinition? _SidebarColumn;
        private Border? _SidebarHost;

        /// <summary>
        /// Instantiate the chat shell.
        /// </summary>
        /// <param name="localization">Localization service. Required.</param>
        /// <param name="threads">Thread service. Required.</param>
        /// <param name="store">Session store. Required.</param>
        /// <param name="usageRecorder">Usage recorder for the turn runner. Required (may be a no-op).</param>
        /// <param name="usageQuery">Usage query service for per-conversation statistics; null when telemetry is disabled.</param>
        /// <param name="configDirectory">Active mux config directory. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public MainWindow(
            ILocalizationService localization,
            IThreadService threads,
            SessionStore store,
            IUsageRecorder usageRecorder,
            UsageQueryService? usageQuery,
            string configDirectory)
        {
            ArgumentNullException.ThrowIfNull(localization);
            ArgumentNullException.ThrowIfNull(threads);
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(usageRecorder);
            ArgumentNullException.ThrowIfNull(configDirectory);

            _Localization = localization;
            _Threads = threads;
            _Store = store;
            _ConfigDirectory = configDirectory;
            _UsageQuery = usageQuery;
            _Runner = new AgentLoopTurnRunner(configDirectory, ApproveToolAsync, usageRecorder);

            Title = "mux";
            Icon = IconResources.LoadWindowIcon();
            Width = 1120;
            Height = 760;
            MinWidth = 820;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FlowDirection = localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            _PromptHistory.Load(new PromptHistoryStore(configDirectory).Load());

            // Apply the persisted theme + view preferences before the first layout so there is no flash of
            // the default and the sidebar opens in its remembered state.
            DesktopPreferences prefs = new DesktopPreferencesStore(configDirectory).Load();
            _SidebarCollapsed = prefs.SidebarCollapsed;
            _AutoExpandThinking = prefs.AutoExpandThinking;
            AppTheme.Set(ResolveThemeVariant(prefs.ThemeMode));
            _Theme = AppTheme.Current;

            Background = _Theme.Surface;
            Content = BuildLayout();
            PopulateModelPicker();

            AddHandler(InputElement.KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);

            // Probe for a git work tree in the background so the undo/redo buttons appear immediately in a
            // repository (they stay disabled until the first per-turn checkpoint is recorded).
            _ = EnsureCheckpointManagerAsync();
        }

        private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.K && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                e.Handled = true;
                OpenCommandPalette();
            }
        }

        private async void OpenCommandPalette()
        {
            List<PaletteCommand> commands = new List<PaletteCommand>
            {
                new PaletteCommand("New conversation", "Start a fresh chat", () => _ = NewChatAsync()),
                new PaletteCommand("Usage dashboard", "Open analytics", OpenUsageWindow),
                new PaletteCommand("Endpoints", "Manage model endpoints", OpenEndpointsWindow),
                new PaletteCommand("MCP servers", "Manage MCP servers", OpenMcpServersWindow),
                new PaletteCommand("Prompt profiles", "Manage prompt profiles", OpenPromptsWindow),
                new PaletteCommand("Skills", "Manage installed skills", OpenSkillsWindow),
                new PaletteCommand("Subagents", "Manage subagent definitions", OpenSubagentsWindow),
                new PaletteCommand("Model pricing", "Edit model pricing", OpenPricingWindow),
                new PaletteCommand("Web search providers", "Configure web search", OpenSearchProvidersWindow),
                new PaletteCommand("Plugins", "Manage hooks and custom commands", OpenPluginsWindow),
                new PaletteCommand("Keybindings", "Rebind keyboard shortcuts", OpenKeybindingsWindow),
                new PaletteCommand("Undo last turn", "Restore the working tree to before the last turn", () => _ = UndoLastTurnAsync()),
                new PaletteCommand("Redo last undo", "Reapply the most recently undone change", () => _ = RedoLastUndoAsync()),
                new PaletteCommand("Reasoning effort", "Set the current model's reasoning effort", OpenEffortPicker),
                new PaletteCommand("Settings", "Open settings", OpenSettingsWindow),
                new PaletteCommand("About", "About mux", OpenAboutWindow)
            };

            Action? chosen = await new CommandPaletteWindow(commands).ShowDialog<Action?>(this);
            chosen?.Invoke();
        }

        /// <summary>
        /// Load the thread list once the window is shown.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            _ = LoadThreadsAsync();
        }

        // ---- layout ------------------------------------------------------------------------------

        private Control BuildLayout()
        {
            _SidebarColumn = new ColumnDefinition(new GridLength(_SidebarCollapsed ? 56 : 280));
            Grid root = new Grid();
            root.ColumnDefinitions.Add(_SidebarColumn);
            root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            Control sidebar = BuildSidebar();
            Grid.SetColumn(sidebar, 0);
            root.Children.Add(sidebar);

            Control chat = BuildChatColumn();
            Grid.SetColumn(chat, 1);
            root.Children.Add(chat);

            return root;
        }

        private Control BuildSidebar()
        {
            _SidebarHost = new Border { Background = _Theme.SurfaceAlt };
            _SidebarHost.Child = BuildSidebarContent();
            return _SidebarHost;
        }

        private Control BuildSidebarContent()
        {
            DockPanel panel = new DockPanel { Margin = new Thickness(8) };
            bool expanded = !_SidebarCollapsed;
            bool conversationsExpanded = expanded && _ConversationsOpen;

            StackPanel top = new StackPanel { Spacing = 2 };
            top.Children.Add(NavItem(_SidebarCollapsed ? "»" : "«", null, _SidebarCollapsed ? "Expand the sidebar back to full width." : "Collapse the sidebar to just icons to make more room for the conversation.", ToggleSidebarCollapse, accent: false));
            top.Children.Add(NavItem("＋", _Localization.Get(StringKeys.NewConversation), "Start a new, empty conversation.", () => _ = NewChatAsync(), accent: true));
            if (!conversationsExpanded)
            {
                top.Children.Add(NavItem("🗂", "Conversations", "Show your saved conversations to switch between or manage them.", ToggleConversations, accent: false, chevron: "▸"));
            }

            DockPanel.SetDock(top, Dock.Top);
            panel.Children.Add(top);

            StackPanel bottom = new StackPanel { Spacing = 2 };
            bottom.Children.Add(NavItem("📊", "Usage", "Open the usage dashboard: token totals, cost, latency charts, and per-call history.", OpenUsageWindow, accent: false));
            if (_ManageOpen && expanded)
            {
                StackPanel manageGroup = new StackPanel { Spacing = 2 };
                manageGroup.Children.Add(NavItem("🛠", "Manage", "Hide the configuration managers.", ToggleManage, accent: false, chevron: "▾"));
                manageGroup.Children.Add(ManageDrawerItem("🔌", "Endpoints", "Add, edit, and choose the default model endpoint.", OpenEndpointsWindow));
                manageGroup.Children.Add(ManageDrawerItem("🧩", "MCP servers", "Manage MCP servers that extend the agent with external tools.", OpenMcpServersWindow));
                manageGroup.Children.Add(ManageDrawerItem("📝", "Prompts", "Manage prompt profiles (system, tools-disabled, and compaction prompts).", OpenPromptsWindow));
                manageGroup.Children.Add(ManageDrawerItem("✨", "Skills", "Install, edit, and enable user skills.", OpenSkillsWindow));
                manageGroup.Children.Add(ManageDrawerItem("🤖", "Subagents", "Define subagents the model can delegate scoped tasks to.", OpenSubagentsWindow));
                manageGroup.Children.Add(ManageDrawerItem("💲", "Pricing", "Edit per-model token rates used to compute usage cost.", OpenPricingWindow));
                manageGroup.Children.Add(ManageDrawerItem("🔎", "Search", "Configure external web-search providers.", OpenSearchProvidersWindow));
                manageGroup.Children.Add(ManageDrawerItem("🧰", "Plugins", "Manage lifecycle hooks and custom slash commands.", OpenPluginsWindow));
                manageGroup.Children.Add(ManageDrawerItem("⌨", "Keybindings", "Rebind or unbind keyboard shortcuts.", OpenKeybindingsWindow));
                bottom.Children.Add(HighlightBlock(manageGroup));
            }
            else
            {
                bottom.Children.Add(NavItem("🛠", "Manage", "Show the configuration managers: endpoints, MCP, prompts, skills, subagents, and pricing.", ToggleManage, accent: false, chevron: "▸"));
            }

            bottom.Children.Add(NavItem("⚙", "Settings", "Open application settings (approval, context, jobs, tools, skills, telemetry).", OpenSettingsWindow, accent: false));
            bottom.Children.Add(NavItem("ⓘ", "About", "About mux — version and project information.", OpenAboutWindow, accent: false));
            DockPanel.SetDock(bottom, Dock.Bottom);
            panel.Children.Add(bottom);

            if (conversationsExpanded)
            {
                DockPanel conversations = new DockPanel();
                Button conversationsToggle = NavItem("🗂", "Conversations", "Hide your saved conversations.", ToggleConversations, accent: false, chevron: "▾");
                DockPanel.SetDock(conversationsToggle, Dock.Top);
                conversations.Children.Add(conversationsToggle);

                Button bulkDelete = new Button
                {
                    Content = "🗑   Delete multiple",
                    Foreground = _Theme.Error,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(18, 4, 10, 4),
                    FontSize = 12
                };
                bulkDelete.Tip("Select several conversations and delete them all at once.");
                bulkDelete.Click += (sender, args) => _ = OpenBulkDeleteAsync();
                DockPanel.SetDock(bulkDelete, Dock.Top);
                conversations.Children.Add(bulkDelete);

                conversations.Children.Add(BuildThreadListControl());
                panel.Children.Add(HighlightBlock(conversations));
            }
            else
            {
                panel.Children.Add(new Panel());
            }

            return panel;
        }

        private Border HighlightBlock(Control child)
        {
            return new Border
            {
                Background = _Theme.Surface,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(0, 2, 0, 2),
                Margin = new Thickness(0, 2, 0, 2),
                Child = child
            };
        }

        private Control BuildThreadListControl()
        {
            // Conversations render as buttons styled exactly like the Manage drawer items, so their hover and
            // selection highlighting match, rather than a ListBox's distinct item chrome.
            _ThreadListPanel = new StackPanel { Spacing = 1, Margin = new Thickness(0, 4, 0, 0) };
            _ = LoadThreadsAsync();
            return new ScrollViewer { Content = _ThreadListPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }

        private Button ConversationButton(ThreadSummary item)
        {
            bool selected = string.Equals(item.Id, _CurrentThreadId, StringComparison.Ordinal);
            Button button = new Button
            {
                Content = new TextBlock { Text = DisplayTitle(item), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center },
                Tag = item.Id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = selected ? _Theme.AccentButton : Brushes.Transparent,
                Foreground = selected ? _Theme.AccentText : _Theme.Muted,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(18, 4, 10, 4),
                FontSize = 13
            };
            button.Tip("Open this conversation. Right-click to rename, export, or delete it.");

            ContextMenu menu = new ContextMenu();
            MenuItem rename = new MenuItem { Header = "Rename" };
            rename.Click += (sender, args) => _ = RenameThreadAsync(item.Id, item.Title);
            menu.Items.Add(rename);
            MenuItem export = new MenuItem { Header = "Export" };
            export.Click += (sender, args) => _ = ExportThreadAsync(item.Id, DisplayTitle(item));
            menu.Items.Add(export);
            menu.Items.Add(new Separator());
            MenuItem delete = new MenuItem { Header = "Delete", Foreground = _Theme.Error };
            delete.Click += (sender, args) => _ = DeleteThreadAsync(item.Id, DisplayTitle(item));
            menu.Items.Add(delete);
            button.ContextMenu = menu;

            button.Click += (sender, args) =>
            {
                if (!string.Equals(item.Id, _CurrentThreadId, StringComparison.Ordinal))
                {
                    _ = OpenThreadAsync(item.Id);
                }
            };
            return button;
        }

        private void RefreshThreadSelection()
        {
            if (_ThreadListPanel == null)
            {
                return;
            }

            foreach (Control child in _ThreadListPanel.Children)
            {
                if (child is Button button && button.Tag is string id)
                {
                    bool selected = string.Equals(id, _CurrentThreadId, StringComparison.Ordinal);
                    button.Background = selected ? _Theme.AccentButton : Brushes.Transparent;
                    button.Foreground = selected ? _Theme.AccentText : _Theme.Muted;
                }
            }
        }

        // A consistent nav row: a fixed-width, centre-aligned icon slot (so every icon shares one horizontal
        // centrepoint) followed by the label and an optional chevron sitting just after it (not a cavern).
        private Control BuildNavContent(string icon, string? label, string? chevron)
        {
            if (_SidebarCollapsed || string.IsNullOrEmpty(label))
            {
                return new TextBlock { Text = icon, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }

            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock { Text = icon, Width = 22, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            if (!string.IsNullOrEmpty(chevron))
            {
                row.Children.Add(new TextBlock { Text = chevron, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.65 });
            }

            return row;
        }

        private Button NavItem(string icon, string? label, string tooltip, Action onClick, bool accent, string? chevron = null)
        {
            Button button = new Button
            {
                Content = BuildNavContent(icon, label, chevron),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = _SidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                Background = accent ? _Theme.AccentButton : Brushes.Transparent,
                Foreground = accent ? _Theme.AccentText : _Theme.Text,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 2)
            };
            button.Tip(tooltip);
            button.Click += (sender, args) => onClick();
            return button;
        }

        private Button ManageDrawerItem(string icon, string label, string tooltip, Action onClick)
        {
            Button button = new Button
            {
                Content = BuildNavContent(icon, label, null),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                Foreground = _Theme.Muted,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(18, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 1),
                FontSize = 13
            };
            button.Tip(tooltip);
            button.Click += (sender, args) => onClick();
            return button;
        }

        private async Task OpenBulkDeleteAsync()
        {
            List<string>? deleted = await new BulkDeleteConversationsWindow(_Threads).ShowDialog<List<string>?>(this);
            if (deleted == null || deleted.Count == 0)
            {
                return;
            }

            if (deleted.Contains(_CurrentThreadId))
            {
                if (_Conversation != null)
                {
                    _Conversation.Event -= OnConversationEvent;
                    _Conversation = null;
                }

                _CurrentThreadId = string.Empty;
                _CurrentTitle = string.Empty;
                _Transcript.Children.Clear();
                _TitleText.Text = "mux";
                UpdateEmptyState();
            }

            await LoadThreadsAsync();
        }

        private void ToggleManage()
        {
            if (_SidebarCollapsed)
            {
                _SidebarCollapsed = false;
                _ManageOpen = true;
                if (_SidebarColumn != null)
                {
                    _SidebarColumn.Width = new GridLength(280);
                }
            }
            else
            {
                _ManageOpen = !_ManageOpen;
            }

            // Accordion: only one section is expanded at a time.
            if (_ManageOpen)
            {
                _ConversationsOpen = false;
            }

            if (_SidebarHost != null)
            {
                _SidebarHost.Child = BuildSidebarContent();
            }
        }

        private void ToggleSidebarCollapse()
        {
            _SidebarCollapsed = !_SidebarCollapsed;
            if (_SidebarColumn != null)
            {
                _SidebarColumn.Width = new GridLength(_SidebarCollapsed ? 56 : 280);
            }

            if (_SidebarHost != null)
            {
                _SidebarHost.Child = BuildSidebarContent();
            }

            SavePreferences();
        }

        private void ToggleConversations()
        {
            if (_SidebarCollapsed)
            {
                _SidebarCollapsed = false;
                _ConversationsOpen = true;
                if (_SidebarColumn != null)
                {
                    _SidebarColumn.Width = new GridLength(280);
                }
            }
            else
            {
                _ConversationsOpen = !_ConversationsOpen;
            }

            // Accordion: only one section is expanded at a time.
            if (_ConversationsOpen)
            {
                _ManageOpen = false;
            }

            if (_SidebarHost != null)
            {
                _SidebarHost.Child = BuildSidebarContent();
            }
        }

        private void OpenSettingsWindow()
        {
            _ = new SettingsWindow(SetThemeMode, _ThemeMode).ShowDialog(this);
        }

        private void OpenUsageWindow()
        {
            if (_UsageQuery == null)
            {
                AddNotice("Usage telemetry is disabled; enable it in Settings to see analytics.", isError: false);
                return;
            }

            _ = OpenUsageWindowAsync();
        }

        private async Task OpenUsageWindowAsync()
        {
            Dictionary<string, string> titles = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                IReadOnlyList<ThreadSummary> threads = await _Threads.ListAsync(CancellationToken.None);
                foreach (ThreadSummary thread in threads)
                {
                    titles[thread.Id] = DisplayTitle(thread.Title);
                }
            }
            catch (Exception)
            {
                // Fall back to raw session ids if the thread list cannot be read.
            }

            string? Resolve(string? sessionId)
            {
                return sessionId != null && titles.TryGetValue(sessionId, out string? title) ? title : null;
            }

            new UsageDashboardWindow(new UsageAnalyticsService(_UsageQuery!, true), Resolve).Show(this);
        }

        private void OpenEndpointsWindow()
        {
            _ = new EndpointsWindow(PopulateModelPicker).ShowDialog(this);
        }

        private void OpenMcpServersWindow()
        {
            _ = new McpServersWindow(null).ShowDialog(this);
        }

        private void OpenPromptsWindow()
        {
            _ = new PromptsWindow(null).ShowDialog(this);
        }

        private void OpenSkillsWindow()
        {
            _ = new SkillsWindow().ShowDialog(this);
        }

        private void OpenSubagentsWindow()
        {
            _ = new SubagentsWindow().ShowDialog(this);
        }

        private void OpenPricingWindow()
        {
            _ = new PricingWindow().ShowDialog(this);
        }

        private void OpenSearchProvidersWindow()
        {
            _ = new SearchProvidersWindow().ShowDialog(this);
        }

        private void OpenPluginsWindow()
        {
            _ = new PluginsWindow().ShowDialog(this);
        }

        private void OpenKeybindingsWindow()
        {
            _ = new KeybindingsWindow().ShowDialog(this);
        }

        private MenuFlyout BuildViewFlyout()
        {
            MenuFlyout flyout = new MenuFlyout();

            MenuItem sidebar = new MenuItem { Header = ViewItemHeader("Collapse sidebar", _SidebarCollapsed) };
            sidebar.Click += (sender, args) =>
            {
                ToggleSidebarCollapse();
                sidebar.Header = ViewItemHeader("Collapse sidebar", _SidebarCollapsed);
            };
            flyout.Items.Add(sidebar);

            MenuItem thinking = new MenuItem { Header = ViewItemHeader("Auto-expand thinking", _AutoExpandThinking) };
            thinking.Click += (sender, args) =>
            {
                _AutoExpandThinking = !_AutoExpandThinking;
                SavePreferences();
                thinking.Header = ViewItemHeader("Auto-expand thinking", _AutoExpandThinking);
            };
            flyout.Items.Add(thinking);

            // Refresh the checkmarks each time the menu opens so they stay correct even when the sidebar was
            // toggled elsewhere (the « » rail button), not just through this menu.
            flyout.Opened += (sender, args) =>
            {
                sidebar.Header = ViewItemHeader("Collapse sidebar", _SidebarCollapsed);
                thinking.Header = ViewItemHeader("Auto-expand thinking", _AutoExpandThinking);
            };

            return flyout;
        }

        private static string ViewItemHeader(string label, bool on)
        {
            return (on ? "✓   " : "      ") + label;
        }

        private Button HeaderGlyphButton(string glyph, string tip)
        {
            Button button = new Button
            {
                Content = glyph,
                Background = Brushes.Transparent,
                Foreground = _Theme.Text,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Tip(tip);
            return button;
        }

        private Task EnsureCheckpointManagerAsync()
        {
            // Probe exactly once and share the same task with every caller, so a turn that awaits the probe
            // always sees the completed result (not an in-flight probe kicked off elsewhere).
            return _CheckpointProbe ??= ProbeCheckpointsAsync();
        }

        private async Task ProbeCheckpointsAsync()
        {
            try
            {
                GitCheckpointService service = new GitCheckpointService(_Runner.WorkingDirectory);
                if (await service.IsRepositoryAsync(CancellationToken.None))
                {
                    _Checkpoints = new CheckpointManager(service);
                }
            }
            catch (Exception)
            {
                // git unavailable or probe failed; leave undo/redo disabled.
            }

            UpdateUndoRedoButtons();
        }

        private void UpdateUndoRedoButtons()
        {
            bool available = _Checkpoints != null;
            _UndoButton.IsVisible = available;
            _RedoButton.IsVisible = available;
            if (!available)
            {
                return;
            }

            _UndoButton.IsEnabled = _Checkpoints!.CanUndo;
            _RedoButton.IsEnabled = _Checkpoints!.CanRedo;
        }

        private async Task UndoLastTurnAsync()
        {
            await EnsureCheckpointManagerAsync();
            if (_Checkpoints == null)
            {
                AddNotice("Undo is unavailable (not a git repository).", isError: false);
                return;
            }

            if (_Conversation != null && _Conversation.IsBusy)
            {
                return;
            }

            try
            {
                Checkpoint? restored = await _Checkpoints.UndoAsync(CancellationToken.None);
                if (restored == null)
                {
                    AddNotice("Nothing to undo.", isError: false);
                }
                else
                {
                    AddNotice("↶ Undid \"" + restored.Label + "\" — working tree restored. Use /redo to reapply.", isError: false);
                }
            }
            catch (Exception ex)
            {
                AddNotice("Undo failed: " + ex.Message, isError: true);
            }

            UpdateUndoRedoButtons();
        }

        private async Task RedoLastUndoAsync()
        {
            await EnsureCheckpointManagerAsync();
            if (_Checkpoints == null)
            {
                AddNotice("Redo is unavailable (not a git repository).", isError: false);
                return;
            }

            if (_Conversation != null && _Conversation.IsBusy)
            {
                return;
            }

            try
            {
                Checkpoint? restored = await _Checkpoints.RedoAsync(CancellationToken.None);
                if (restored == null)
                {
                    AddNotice("Nothing to redo.", isError: false);
                }
                else
                {
                    AddNotice("↷ Redid \"" + restored.Label + "\" — working tree reapplied.", isError: false);
                }
            }
            catch (Exception ex)
            {
                AddNotice("Redo failed: " + ex.Message, isError: true);
            }

            UpdateUndoRedoButtons();
        }

        private async void OpenEffortPicker()
        {
            if (!(_ModelPicker.SelectedItem is EndpointConfig selected))
            {
                AddNotice("Select an endpoint first to change its reasoning effort.", isError: false);
                return;
            }

            List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
            EndpointConfig? target = endpoints.Find(e => string.Equals(e.Name, selected.Name, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                return;
            }

            if (await new ReasoningEffortDialog(target).ShowDialog<bool>(this))
            {
                try
                {
                    SettingsLoader.SaveEndpoints(endpoints);
                }
                catch (Exception)
                {
                    // Best-effort.
                }

                PopulateModelPicker();
            }
        }

        private void OpenAboutWindow()
        {
            _ = new AboutWindow(_Localization).ShowDialog(this);
        }

        private async Task NewChatAsync()
        {
            try
            {
                ThreadSummary created = await _Threads.CreateAsync(null, SelectedEndpointName(), SelectedModel(), CancellationToken.None);
                await OpenThreadAsync(created.Id);
            }
            catch (Exception)
            {
                // Best-effort.
            }
        }

        private void SetThemeMode(string mode)
        {
            bool dark = ResolveThemeVariant(mode);
            ApplyThemeDark(dark);
            SaveThemeMode();
        }

        private bool ResolveThemeVariant(string mode)
        {
            _ThemeMode = mode;

            if (string.Equals(mode, "system", StringComparison.OrdinalIgnoreCase))
            {
                if (Application.Current != null)
                {
                    // Let Avalonia follow the OS; we then mirror the resolved variant into our palette.
                    Application.Current.RequestedThemeVariant = ThemeVariant.Default;
                    if (!_ThemeSubscribed)
                    {
                        Application.Current.ActualThemeVariantChanged += OnActualThemeVariantChanged;
                        _ThemeSubscribed = true;
                    }
                }

                return ResolveSystemDark();
            }

            bool dark = string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase);
            if (Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            }

            return dark;
        }

        private void SaveThemeMode()
        {
            SavePreferences();
        }

        private void SavePreferences()
        {
            try
            {
                new DesktopPreferencesStore(_ConfigDirectory).Save(new DesktopPreferences
                {
                    ThemeMode = _ThemeMode,
                    SidebarCollapsed = _SidebarCollapsed,
                    AutoExpandThinking = _AutoExpandThinking
                });
            }
            catch (Exception)
            {
                // Best-effort; a failed write just means the choice is not remembered.
            }
        }

        private void OnActualThemeVariantChanged(object? sender, EventArgs e)
        {
            if (string.Equals(_ThemeMode, "system", StringComparison.OrdinalIgnoreCase))
            {
                ApplyThemeDark(ResolveSystemDark());
            }
        }

        private static bool ResolveSystemDark()
        {
            return Application.Current == null || Application.Current.ActualThemeVariant == ThemeVariant.Dark;
        }

        private void ApplyThemeDark(bool dark)
        {
            if (dark == _Theme.IsDark)
            {
                return;
            }

            AppTheme.Set(dark);
            RebuildContent();
        }

        private Control BuildChatColumn()
        {
            DockPanel column = new DockPanel();
            column.Children.Add(BuildHeader());
            column.Children.Add(BuildTabStrip());
            column.Children.Add(BuildComposer());
            column.Children.Add(BuildTranscriptArea());
            return column;
        }

        // ---- tabbed workspace --------------------------------------------------------------------

        private Control BuildTabStrip()
        {
            _TabStripHost = new Border
            {
                Background = _Theme.SurfaceAlt,
                BorderBrush = _Theme.Border,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            DockPanel.SetDock(_TabStripHost, Dock.Top);
            RenderTabStrip();
            return _TabStripHost;
        }

        private void RenderTabStrip()
        {
            if (_TabStripHost == null)
            {
                return;
            }

            if (!_Workspace.HasTabs)
            {
                _TabStripHost.IsVisible = false;
                _TabStripHost.Child = null;
                return;
            }

            _TabStripHost.IsVisible = true;

            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 5, 8, 5) };
            foreach (WorkspaceTabViewModel tab in _Workspace.Tabs)
            {
                row.Children.Add(BuildTabButton(tab));
            }

            _TabStripHost.Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = row
            };
        }

        private Control BuildTabButton(WorkspaceTabViewModel tab)
        {
            bool active = ReferenceEquals(tab, _Workspace.ActiveTab);

            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };

            content.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = StatusDotBrush(tab.Status),
                VerticalAlignment = VerticalAlignment.Center
            });

            string label = string.IsNullOrEmpty(tab.Title) ? "Untitled" : tab.Title;
            if (tab.UnreadCount > 0 && !active)
            {
                label += "  (" + tab.UnreadCount + ")";
            }

            content.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = active ? _Theme.AccentText : _Theme.Text,
                FontSize = 12,
                FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 200,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            Button close = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                Foreground = active ? _Theme.AccentText : _Theme.Muted,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(3, 0, 3, 0),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            close.Tip("Close this tab.");
            close.Click += async (sender, args) => await CloseTabAsync(tab);
            content.Children.Add(close);

            Border chrome = new Border
            {
                Background = active ? _Theme.AccentButton : Brushes.Transparent,
                BorderBrush = active ? _Theme.AccentButton : _Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 4, 6, 4),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = content
            };
            chrome.Tip(tab.Title);
            chrome.Tapped += async (sender, args) =>
            {
                if (!close.IsPointerOver)
                {
                    await SelectTabAsync(tab);
                }
            };

            return chrome;
        }

        private IBrush StatusDotBrush(TabStatus status)
        {
            switch (status)
            {
                case TabStatus.NeedsApproval:
                    return new SolidColorBrush(Color.Parse("#d1242f"));
                case TabStatus.Error:
                    return _Theme.Error;
                case TabStatus.Unread:
                    return new SolidColorBrush(Color.Parse("#0969da"));
                case TabStatus.Running:
                    return new SolidColorBrush(Color.Parse("#bf8700"));
                default:
                    return _Theme.Border;
            }
        }

        private WorkspaceTabViewModel OpenOrFocusTab(string id, string title)
        {
            WorkspaceTabViewModel tab = _Workspace.OpenTab(id, title);
            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.PropertyChanged += OnTabPropertyChanged;
            RenderTabStrip();
            return tab;
        }

        private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(RenderTabStrip);
        }

        private async Task SelectTabAsync(WorkspaceTabViewModel tab)
        {
            if (ReferenceEquals(tab, _Workspace.ActiveTab) && string.Equals(tab.Id, _CurrentThreadId, StringComparison.Ordinal))
            {
                return;
            }

            if (_Conversation != null && _Conversation.IsBusy)
            {
                AddNotice("Finish or stop the current turn before switching tabs.", isError: false);
                return;
            }

            await OpenThreadAsync(tab.Id);
        }

        private async Task CloseTabAsync(WorkspaceTabViewModel tab)
        {
            bool wasActive = ReferenceEquals(tab, _Workspace.ActiveTab);
            if (wasActive && _Conversation != null && _Conversation.IsBusy)
            {
                AddNotice("Finish or stop the current turn before closing this tab.", isError: false);
                return;
            }

            tab.PropertyChanged -= OnTabPropertyChanged;
            _Workspace.CloseTab(tab);
            _TabTranscripts.Remove(tab.Id);
            RenderTabStrip();

            if (!wasActive)
            {
                return;
            }

            if (_Workspace.ActiveTab != null)
            {
                await OpenThreadAsync(_Workspace.ActiveTab.Id);
            }
            else
            {
                ClearActiveConversation();
            }
        }

        private void ClearActiveConversation()
        {
            if (_Conversation != null)
            {
                _Conversation.Event -= OnConversationEvent;
                _Conversation = null;
            }

            _CurrentThreadId = string.Empty;
            _CurrentTitle = string.Empty;
            _TitleSummarized = false;
            ResetStreamingState();
            _Transcript = new StackPanel { Margin = new Thickness(24, 16, 24, 16), Spacing = 14 };
            _TranscriptScroll.Content = _Transcript;
            _TitleText.Text = "mux";
            _LastEstimatedTokens = 0;
            UpdateContextIndicator();
            UpdateEmptyState();
            RefreshThreadSelection();
        }

        private WorkspaceTabViewModel? FindTabById(string id)
        {
            foreach (WorkspaceTabViewModel tab in _Workspace.Tabs)
            {
                if (string.Equals(tab.Id, id, StringComparison.Ordinal))
                {
                    return tab;
                }
            }

            return null;
        }

        private void PruneClosedTabs(HashSet<string> liveIds)
        {
            List<WorkspaceTabViewModel> stale = new List<WorkspaceTabViewModel>();
            foreach (WorkspaceTabViewModel tab in _Workspace.Tabs)
            {
                if (!liveIds.Contains(tab.Id))
                {
                    stale.Add(tab);
                }
            }

            if (stale.Count == 0)
            {
                return;
            }

            foreach (WorkspaceTabViewModel tab in stale)
            {
                tab.PropertyChanged -= OnTabPropertyChanged;
                _Workspace.CloseTab(tab);
                _TabTranscripts.Remove(tab.Id);
            }

            RenderTabStrip();
        }

        private void SyncActiveTabTitle(string title)
        {
            WorkspaceTabViewModel? tab = _Workspace.ActiveTab;
            if (tab != null && string.Equals(tab.Id, _CurrentThreadId, StringComparison.Ordinal))
            {
                tab.Title = DisplayTitle(title);
                RenderTabStrip();
            }
        }

        private Control BuildHeader()
        {
            DockPanel header = new DockPanel { Height = 56, Margin = new Thickness(20, 0, 16, 0) };

            _TitleText = new TextBlock { Text = DisplayTitle(_CurrentTitle), FontWeight = FontWeight.SemiBold, FontSize = 15, Foreground = _Theme.Text, VerticalAlignment = VerticalAlignment.Center };
            if (string.IsNullOrEmpty(_CurrentThreadId))
            {
                _TitleText.Text = "mux";
            }
            _TitleText.Tip("The current conversation's title. It starts from your first message and is replaced with an AI summary once the conversation grows; rename it from the conversation list.");
            DockPanel.SetDock(_TitleText, Dock.Left);
            header.Children.Add(_TitleText);

            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

            _UndoButton = HeaderGlyphButton("↶", "Undo the last turn's file changes (restores the git working tree).");
            _UndoButton.Click += async (sender, args) => await UndoLastTurnAsync();
            right.Children.Add(_UndoButton);

            _RedoButton = HeaderGlyphButton("↷", "Redo the most recently undone change.");
            _RedoButton.Click += async (sender, args) => await RedoLastUndoAsync();
            right.Children.Add(_RedoButton);
            UpdateUndoRedoButtons();

            _ContextIndicator = new TextBlock { Text = string.Empty, FontSize = 12, Foreground = _Theme.Muted, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(_ContextIndicator);
            UpdateContextIndicator();

            _ModelStatus = new TextBlock { Text = string.Empty, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            _ModelStatus.Tip("Whether the selected endpoint responded when it was last checked (validated when you switch models).");
            right.Children.Add(_ModelStatus);

            _ModelPicker = new ComboBox { MinWidth = 200, BorderBrush = _Theme.Border };
            _ModelPicker.Tip("The endpoint and model this conversation sends to. Manage the list under Manage ▸ Endpoints.");
            _ModelPicker.ItemTemplate = new FuncDataTemplate<EndpointConfig>(
                (item, scope) => new TextBlock { Text = item != null ? item.Name : string.Empty },
                supportsRecycling: true);
            _ModelPicker.SelectionChanged += OnModelSelected;
            right.Children.Add(_ModelPicker);

            Button themeToggle = new Button
            {
                Content = _Theme.IsDark ? "☀" : "🌙",
                Background = Brushes.Transparent,
                Foreground = _Theme.Text,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center
            };
            themeToggle.Tip(_Theme.IsDark ? "Switch to the light theme." : "Switch to the dark theme.");
            themeToggle.Click += (sender, args) => SetThemeMode(_Theme.IsDark ? "light" : "dark");
            right.Children.Add(themeToggle);

            Button viewMenu = HeaderGlyphButton("☰", "View options: sidebar and thinking display.");
            viewMenu.Flyout = BuildViewFlyout();
            right.Children.Add(viewMenu);

            DockPanel.SetDock(right, Dock.Right);
            header.Children.Add(right);

            Border framed = new Border { BorderBrush = _Theme.Border, BorderThickness = new Thickness(0, 0, 0, 1), Child = header };
            DockPanel.SetDock(framed, Dock.Top);
            return framed;
        }

        private Control BuildComposer()
        {
            _Composer = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 160,
                PlaceholderText = "Message mux…  (Enter to send, Shift+Enter for a new line)",
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = _Theme.Text,
                BorderBrush = _Theme.Border
            };
            _Composer.Tip("Type your message. Enter sends; Shift+Enter, Ctrl+Enter, or Ctrl+J insert a new line. Up/Down at the edges recall previous prompts. Type /? for commands.");
            // Handle keys on the tunnel route so this runs BEFORE the TextBox's own Enter handling; otherwise
            // the TextBox inserts a newline and marks the event handled before we ever see plain Enter.
            _Composer.AddHandler(InputElement.KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
            // Any manual edit ends history navigation so the next Up starts from the edited draft.
            _Composer.TextChanged += (sender, args) =>
            {
                if (!_SuppressHistoryReset)
                {
                    _PromptHistory.ResetCursor();
                }
            };

            _SendButton = AccentButton("Send");
            _SendButton.Tip("Send your message (Enter). While the model is responding this becomes Stop to cancel the turn.");
            _SendButton.VerticalAlignment = VerticalAlignment.Bottom;
            _SendButton.Padding = new Thickness(18, 8, 18, 8);
            _SendButton.Margin = new Thickness(8, 0, 0, 0);
            _SendButton.Click += OnSend;

            DockPanel bar = new DockPanel();
            DockPanel.SetDock(_SendButton, Dock.Right);
            bar.Children.Add(_SendButton);
            bar.Children.Add(_Composer);

            TextBlock disclaimer = new TextBlock
            {
                Text = Disclaimer,
                Foreground = _Theme.Muted,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };

            StackPanel stack = new StackPanel { Margin = new Thickness(20, 12, 20, 12) };
            stack.Children.Add(bar);
            stack.Children.Add(disclaimer);

            Border framed = new Border { BorderBrush = _Theme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = stack };
            DockPanel.SetDock(framed, Dock.Bottom);
            return framed;
        }

        private Control BuildTranscriptArea()
        {
            _EmptyState = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 8 };
            _QuipText = new TextBlock
            {
                Text = WelcomeQuips.Next(),
                FontSize = 40,
                FontWeight = FontWeight.Bold,
                Foreground = _Theme.Text,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 640,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _EmptyState.Children.Add(_QuipText);
            _EmptyState.Children.Add(new TextBlock
            {
                Text = "Type a message below and press Enter, or start a new conversation.",
                Foreground = _Theme.Muted,
                MaxWidth = 420,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            _OverviewHost = new StackPanel { Spacing = 12, Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 520 };
            _EmptyState.Children.Add(_OverviewHost);
            _ = PopulateOverviewAsync();

            _Transcript = new StackPanel { Margin = new Thickness(24, 16, 24, 16), Spacing = 14 };
            _TranscriptScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _Transcript };

            Grid area = new Grid();
            area.Children.Add(_TranscriptScroll);
            area.Children.Add(_EmptyState);
            return area;
        }

        private Button AccentButton(string text)
        {
            return new Button
            {
                Content = text,
                Background = _Theme.AccentButton,
                Foreground = _Theme.AccentText,
                Padding = new Thickness(10, 8, 10, 8)
            };
        }

        // ---- theme -------------------------------------------------------------------------------

        private void RebuildContent()
        {
            _Theme = AppTheme.Current;
            ResetStreamingState();
            // Cached transcript panels belong to the old visual tree and old theme colors; drop them so each
            // tab re-renders fresh (RefreshAfterRebuild re-opens the current thread).
            _TabTranscripts.Clear();
            Background = _Theme.Surface;
            Content = BuildLayout();
            PopulateModelPicker();
            _ = RefreshAfterRebuild();
        }

        private async Task RefreshAfterRebuild()
        {
            await LoadThreadsAsync();
            if (!string.IsNullOrEmpty(_CurrentThreadId))
            {
                await OpenThreadAsync(_CurrentThreadId);
            }
            else
            {
                UpdateEmptyState();
            }
        }

        // ---- data loading ------------------------------------------------------------------------

        private void PopulateModelPicker()
        {
            try
            {
                List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
                _ModelPicker.ItemsSource = endpoints;

                EndpointConfig? preferred = null;
                foreach (EndpointConfig endpoint in endpoints)
                {
                    if (endpoint.IsDefault)
                    {
                        preferred = endpoint;
                        break;
                    }
                }

                if (preferred == null && endpoints.Count > 0)
                {
                    preferred = endpoints[0];
                }

                _ModelPicker.SelectedItem = preferred;
                if (preferred != null)
                {
                    _Runner.EndpointName = preferred.Name;
                }
            }
            catch (Exception)
            {
                // No endpoints configured; a send surfaces the error inline.
            }
        }

        private async Task LoadThreadsAsync()
        {
            if (_ThreadListPanel == null)
            {
                return;
            }

            try
            {
                IReadOnlyList<ThreadSummary> threads = await _Threads.ListAsync(CancellationToken.None);

                // Close any open tabs whose conversation no longer exists (e.g. after a bulk delete).
                HashSet<string> liveIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (ThreadSummary summary in threads)
                {
                    liveIds.Add(summary.Id);
                }

                PruneClosedTabs(liveIds);

                _ThreadListPanel.Children.Clear();
                if (threads.Count == 0)
                {
                    _ThreadListPanel.Children.Add(new TextBlock { Text = "No conversations yet.", Foreground = _Theme.Muted, FontSize = 12, Margin = new Thickness(18, 4, 10, 4) });
                    return;
                }

                foreach (ThreadSummary summary in threads)
                {
                    _ThreadListPanel.Children.Add(ConversationButton(summary));
                }
            }
            catch (Exception)
            {
                // Best-effort; the list stays as-is on failure.
            }
        }

        // ---- event handlers ----------------------------------------------------------------------

        private void OnModelSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (_ModelPicker.SelectedItem is EndpointConfig endpoint)
            {
                _Runner.EndpointName = endpoint.Name;
                UpdateContextIndicator();
                _ = ValidateModelAsync(endpoint);
            }
        }

        private async Task ValidateModelAsync(EndpointConfig endpoint)
        {
            int seq = ++_ModelValidationSeq;
            _ModelStatus.Foreground = _Theme.Muted;
            _ModelStatus.Text = "checking…";

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
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                result = await LlmClient.LoadModelAsync(endpoint, ignoreCert, cts.Token);
            }
            catch (Exception exception)
            {
                result = ModelLoadResult.Fail(exception.Message);
            }

            // Ignore a stale result if the user switched models again while this was in flight.
            if (seq != _ModelValidationSeq)
            {
                return;
            }

            if (result.Success)
            {
                _ModelStatus.Foreground = _Theme.Success;
                _ModelStatus.Text = "✓ ready";
                _ModelStatus.Tip("The selected endpoint responded when it was last checked.");
            }
            else if (result.Reachable)
            {
                // The backend answered (so the URL, credentials, and model routing work), but the lightweight
                // validation request itself did not succeed. Normal chats may still work — don't cry "unreachable".
                _ModelStatus.Foreground = new SolidColorBrush(Color.Parse("#bf8700"));
                _ModelStatus.Text = "⚠ reachable";
                _ModelStatus.Tip("The endpoint is reachable but the validation request did not succeed: " + (result.Error ?? "unknown error") + ". Normal chats may still work.");
            }
            else
            {
                _ModelStatus.Foreground = _Theme.Error;
                _ModelStatus.Text = "✗ unreachable";
                _ModelStatus.Tip("The selected endpoint did not respond: " + (result.Error ?? "unknown error"));
            }
        }

        private void OnComposerKeyDown(object? sender, KeyEventArgs e)
        {
            bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

            if (e.Key == Key.Enter)
            {
                if (ctrl || shift)
                {
                    e.Handled = true;
                    InsertComposerNewline();
                }
                else
                {
                    e.Handled = true;
                    _ = SendAsync();
                }
            }
            else if (e.Key == Key.J && ctrl)
            {
                e.Handled = true;
                InsertComposerNewline();
            }
            else if (e.Key == Key.Up && !ctrl && !shift && CaretAtStart())
            {
                if (_PromptHistory.TryPrevious(_Composer.Text ?? string.Empty, out string recalled))
                {
                    e.Handled = true;
                    SetComposerFromHistory(recalled);
                }
            }
            else if (e.Key == Key.Down && !ctrl && !shift && _PromptHistory.IsNavigating && CaretAtEnd())
            {
                if (_PromptHistory.TryNext(out string recalled))
                {
                    e.Handled = true;
                    SetComposerFromHistory(recalled);
                }
            }
        }

        private void RecordPrompt(string prompt)
        {
            _PromptHistory.Add(prompt);
            try
            {
                new PromptHistoryStore(_ConfigDirectory).Save(_PromptHistory.Entries);
            }
            catch (Exception)
            {
                // Best-effort; history recall just won't survive a restart.
            }
        }

        private bool CaretAtStart()
        {
            return _Composer.CaretIndex <= 0;
        }

        private bool CaretAtEnd()
        {
            return _Composer.CaretIndex >= (_Composer.Text ?? string.Empty).Length;
        }

        private void SetComposerFromHistory(string text)
        {
            _SuppressHistoryReset = true;
            _Composer.Text = text;
            _Composer.CaretIndex = text.Length;
            _SuppressHistoryReset = false;
        }

        private void OnSend(object? sender, RoutedEventArgs e)
        {
            if (_Conversation != null && _Conversation.IsBusy)
            {
                _TurnCts?.Cancel();
                return;
            }

            _ = SendAsync();
        }

        // ---- conversation flow -------------------------------------------------------------------

        private async Task OpenThreadAsync(string id)
        {
            SessionSnapshot? snapshot = await _Store.LoadAsync(id, CancellationToken.None);
            if (snapshot == null)
            {
                return;
            }

            _CurrentThreadId = snapshot.Id;
            RefreshThreadSelection();
            _Runner.SessionId = snapshot.Id;
            _LastEstimatedTokens = 0;
            UpdateContextIndicator();
            _CurrentTitle = snapshot.Title;
            _CurrentTitlePinned = snapshot.TitlePinned;
            // Treat an already-substantial conversation as already titled so we don't re-summarize on reopen;
            // a short one may still cross the threshold and get an AI title as it grows.
            _TitleSummarized = ConversationCharCount(snapshot.ConversationHistory) >= TitleSummaryThreshold;
            _CurrentCreatedUtc = snapshot.CreatedUtc == default ? DateTime.UtcNow : snapshot.CreatedUtc;
            _TitleText.Text = DisplayTitle(snapshot.Title);

            if (_Conversation != null)
            {
                _Conversation.Event -= OnConversationEvent;
            }

            _Conversation = new ConversationService(_Runner, snapshot.ConversationHistory);
            _Conversation.Event += OnConversationEvent;

            ResetStreamingState();

            // Each tab keeps its own rendered transcript so switching tabs is instant and preserves scroll,
            // rather than clearing and re-rendering from disk every time. Point _Transcript (the target of all
            // render helpers) at this thread's panel and show it.
            _Transcript = GetOrCreateTranscriptPanel(snapshot.Id, snapshot.ConversationHistory);
            _TranscriptScroll.Content = _Transcript;

            OpenOrFocusTab(snapshot.Id, snapshot.Title);
            UpdateEmptyState();
        }

        private StackPanel GetOrCreateTranscriptPanel(string id, IReadOnlyList<ConversationMessage> history)
        {
            if (_TabTranscripts.TryGetValue(id, out StackPanel? existing))
            {
                return existing;
            }

            StackPanel panel = new StackPanel { Margin = new Thickness(24, 16, 24, 16), Spacing = 14 };
            _TabTranscripts[id] = panel;

            // RenderPersistedMessage appends to _Transcript, so target the new panel while rendering history.
            _Transcript = panel;
            foreach (ConversationMessage message in history)
            {
                RenderPersistedMessage(message);
            }

            return panel;
        }

        private async Task CompactCurrentAsync()
        {
            if (_Conversation == null || _Conversation.History.Count == 0)
            {
                AddNotice("There is nothing to compact yet.", isError: false);
                return;
            }

            if (!(_ModelPicker.SelectedItem is EndpointConfig picked))
            {
                AddNotice("Select an endpoint first to compact the conversation.", isError: false);
                return;
            }

            MuxSettings settings = SettingsLoader.LoadSettings();
            string compactionPrompt = SettingsLoader.GetActivePromptProfile().CompactionPrompt ?? string.Empty;

            AddNotice("Compacting the conversation…", isError: false);

            CompactionResult result = await ConversationCompactor.CompactAsync(
                _Conversation.History,
                picked,
                compactionPrompt,
                settings.CompactionPreserveTurns,
                settings.IgnoreCertErrors,
                CancellationToken.None);

            if (!result.Compacted || result.History == null)
            {
                AddNotice(result.Message, isError: !result.Success);
                return;
            }

            _Conversation.Event -= OnConversationEvent;
            _Conversation = new ConversationService(_Runner, result.History);
            _Conversation.Event += OnConversationEvent;

            ResetStreamingState();
            _Transcript.Children.Clear();
            foreach (ConversationMessage message in result.History)
            {
                RenderPersistedMessage(message);
            }

            UpdateEmptyState();

            await PersistCurrentAsync();
            await LoadThreadsAsync();
            AddNotice(result.Message, isError: false);
        }

        private async Task SendAsync()
        {
            string prompt = (_Composer.Text ?? string.Empty).Trim();
            if (prompt.Length == 0)
            {
                return;
            }

            RecordPrompt(prompt);

            if (prompt.StartsWith("/", StringComparison.Ordinal))
            {
                _Composer.Text = string.Empty;
                HandleSlashCommand(prompt);
                return;
            }

            if (_Conversation != null && _Conversation.IsBusy)
            {
                return;
            }

            if (_Conversation == null)
            {
                ThreadSummary created = await _Threads.CreateAsync(null, SelectedEndpointName(), SelectedModel(), CancellationToken.None);
                await OpenThreadAsync(created.Id);
                if (_Conversation == null)
                {
                    return;
                }
            }

            _Composer.Text = string.Empty;
            ResetStreamingState();
            _TurnStopwatch = Stopwatch.StartNew();
            _TurnTtftMs = null;
            _TurnCts = new CancellationTokenSource();
            AddUserBubble(prompt);
            StartPendingIndicator();
            UpdateEmptyState();
            SetSending(true);

            await EnsureCheckpointManagerAsync();
            if (_Checkpoints != null)
            {
                try
                {
                    await _Checkpoints.RecordAsync(CheckpointLabel(prompt), _TurnCts.Token);
                    UpdateUndoRedoButtons();
                }
                catch (Exception)
                {
                    // Best-effort snapshot; never block the turn on checkpointing.
                }
            }

            try
            {
                TurnProjection projection = await _Conversation!.RunTurnAsync(prompt, _TurnCts.Token);
                if (projection.WasCancelled)
                {
                    AddNotice("(stopped)", isError: false);
                }
            }
            catch (Exception ex)
            {
                AddNotice("Error: " + ex.Message, isError: true);
            }
            finally
            {
                StopPendingIndicator();
                FinalizeAssistantBubble();
                SetSending(false);
                _TurnCts?.Dispose();
                _TurnCts = null;
                await PersistCurrentAsync();
                await LoadThreadsAsync();
            }
        }

        private static string CheckpointLabel(string prompt)
        {
            string trimmed = (prompt ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (trimmed.Length <= 60)
            {
                return trimmed.Length == 0 ? "turn" : trimmed;
            }

            return trimmed.Substring(0, 57) + "…";
        }

        private void SetSending(bool sending)
        {
            _SendButton.Content = sending ? "Stop" : "Send";
            _Workspace.ActiveTab?.NotifyBusy(sending);
        }

        private void OnConversationEvent(object? sender, AgentEvent agentEvent)
        {
            Dispatcher.UIThread.Post(() => ApplyEventToUi(agentEvent));
        }

        // ---- slash commands ----------------------------------------------------------------------

        private void HandleSlashCommand(string input)
        {
            string command = input.Trim();
            int space = command.IndexOf(' ');
            if (space > 0)
            {
                command = command.Substring(0, space);
            }

            command = command.ToLowerInvariant();

            switch (command)
            {
                case "/clear":
                    _Transcript.Children.Clear();
                    UpdateEmptyState();
                    break;
                case "/context":
                case "/stats":
                    _ = ShowStatsAsync();
                    break;
                case "/usage":
                    OpenUsageWindow();
                    break;
                case "/endpoints":
                case "/endpoint":
                case "/models":
                case "/model":
                    OpenEndpointsWindow();
                    break;
                case "/mcp":
                case "/mcps":
                    OpenMcpServersWindow();
                    break;
                case "/prompt":
                case "/prompts":
                    OpenPromptsWindow();
                    break;
                case "/skill":
                case "/skills":
                    OpenSkillsWindow();
                    break;
                case "/subagent":
                case "/subagents":
                case "/agents":
                    OpenSubagentsWindow();
                    break;
                case "/commands":
                case "/palette":
                    OpenCommandPalette();
                    break;
                case "/pricing":
                    OpenPricingWindow();
                    break;
                case "/search":
                case "/websearch":
                    OpenSearchProvidersWindow();
                    break;
                case "/plugin":
                case "/plugins":
                case "/hooks":
                    OpenPluginsWindow();
                    break;
                case "/keys":
                case "/keybindings":
                case "/shortcuts":
                    OpenKeybindingsWindow();
                    break;
                case "/undo":
                    _ = UndoLastTurnAsync();
                    break;
                case "/redo":
                    _ = RedoLastUndoAsync();
                    break;
                case "/effort":
                    OpenEffortPicker();
                    break;
                case "/compact":
                    _ = CompactCurrentAsync();
                    break;
                case "/new":
                    _ = NewChatAsync();
                    break;
                case "/help":
                case "/?":
                case "/menu":
                    ShowHelpMenu();
                    break;
                default:
                    AddNotice("Unknown command: " + command, isError: false);
                    ShowHelpMenu();
                    break;
            }
        }

        private async Task ShowStatsAsync()
        {
            if (_UsageQuery == null)
            {
                AddNotice("Usage telemetry is disabled; statistics are unavailable.", isError: false);
                return;
            }

            if (string.IsNullOrEmpty(_CurrentThreadId))
            {
                AddNotice("Open a conversation to see its statistics.", isError: false);
                return;
            }

            try
            {
                UsageSummary summary = await _UsageQuery.GetSummaryAsync(new UsageFilter { SessionId = _CurrentThreadId }, CancellationToken.None);
                new StatsWindow(summary.Metrics, CountUserTurns(), SelectedContextWindow(), _LastEstimatedTokens).Show(this);
            }
            catch (Exception ex)
            {
                AddNotice("Could not load statistics: " + ex.Message, isError: true);
            }
        }

        private void ShowHelpMenu()
        {
            StackPanel card = new StackPanel { Spacing = 4 };
            card.Children.Add(new TextBlock { Text = "Quick commands", FontWeight = FontWeight.SemiBold, Foreground = _Theme.Text });
            card.Children.Add(CommandRow("/clear", "Clear the transcript"));
            card.Children.Add(CommandRow("/context", "Show conversation statistics"));
            card.Children.Add(CommandRow("/compact", "Summarize older turns to free up context"));
            card.Children.Add(CommandRow("/undo", "Undo the last turn's file changes"));
            card.Children.Add(CommandRow("/redo", "Redo the last undone change"));
            card.Children.Add(CommandRow("/usage", "Open the usage dashboard"));
            card.Children.Add(CommandRow("/endpoints", "Manage model endpoints"));
            card.Children.Add(CommandRow("/mcp", "Manage MCP servers"));
            card.Children.Add(CommandRow("/prompt", "Manage prompt profiles"));
            card.Children.Add(CommandRow("/skills", "Manage installed skills"));
            card.Children.Add(CommandRow("/subagents", "Manage subagent definitions"));
            card.Children.Add(CommandRow("/pricing", "Edit model pricing"));
            card.Children.Add(CommandRow("/search", "Configure web search providers"));
            card.Children.Add(CommandRow("/plugins", "Manage hooks and custom commands"));
            card.Children.Add(CommandRow("/keys", "Rebind keyboard shortcuts"));
            card.Children.Add(CommandRow("/effort", "Set the model's reasoning effort"));
            card.Children.Add(CommandRow("/commands", "Open the command palette (Ctrl+K)"));
            card.Children.Add(CommandRow("/new", "Start a new conversation"));
            card.Children.Add(CommandRow("/help", "Show this menu"));

            _Transcript.Children.Add(new Border
            {
                Background = _Theme.SurfaceAlt,
                BorderBrush = _Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 60, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = card
            });
            _TranscriptScroll.ScrollToEnd();
            UpdateEmptyState();
        }

        private Control CommandRow(string command, string description)
        {
            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            content.Children.Add(new TextBlock { Text = command, Width = 90, Foreground = _Theme.Accent, FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = description, Foreground = _Theme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

            Button button = new Button
            {
                Content = content,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(4)
            };
            button.Click += (sender, args) => HandleSlashCommand(command);
            return button;
        }

        private int CountUserTurns()
        {
            if (_Conversation == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ConversationMessage message in _Conversation.History)
            {
                if (message.Role == RoleEnum.User)
                {
                    count++;
                }
            }

            return count;
        }

        private int SelectedContextWindow()
        {
            // _ModelPicker is assigned partway through BuildHeader, but UpdateContextIndicator() runs earlier
            // in the same pass, so guard against the picker not existing yet (and having no selection).
            return _ModelPicker?.SelectedItem is EndpointConfig endpoint ? endpoint.ContextWindow : 0;
        }

        private void UpdateContextIndicator()
        {
            if (_ContextIndicator == null)
            {
                return;
            }

            int window = SelectedContextWindow();
            if (_LastEstimatedTokens <= 0 || window <= 0)
            {
                _ContextIndicator.Text = string.Empty;
                return;
            }

            double fraction = Math.Min(1.0, (double)_LastEstimatedTokens / window);
            _ContextIndicator.Text = "ctx " + (fraction * 100).ToString("0") + "%";
            _ContextIndicator.Foreground = fraction >= 0.9 ? _Theme.Error : fraction >= 0.75 ? new SolidColorBrush(Color.Parse("#bf8700")) : _Theme.Muted;
            _ContextIndicator.Tip("Estimated context used: about " + _LastEstimatedTokens.ToString("N0") + " of " + window.ToString("N0") + " tokens (" + (fraction * 100).ToString("0") + "%). Use /compact to free up room.");
        }

        private void ApplyEventToUi(AgentEvent agentEvent)
        {
            switch (agentEvent)
            {
                case AssistantThinkingEvent thinking:
                    EnsureThinkingSection();
                    if (_ThinkingText != null)
                    {
                        _ThinkingText.Text += thinking.Text;
                    }
                    break;
                case AssistantTextEvent text:
                    if (_TurnTtftMs == null && _TurnStopwatch != null)
                    {
                        _TurnTtftMs = _TurnStopwatch.ElapsedMilliseconds;
                    }
                    StopPendingIndicator();
                    EnsureAssistantBubble();
                    if (_StreamingBlock != null)
                    {
                        _StreamingBlock.Text += text.Text;
                    }
                    _TranscriptScroll.ScrollToEnd();
                    break;
                case ContextCompactedEvent compacted:
                    AddNotice("🗜 Context automatically compacted (" + compacted.MessagesBefore + " → " + compacted.MessagesAfter + " messages) to stay within the model's window.", isError: false);
                    break;
                case TaskPlanUpdatedEvent plan:
                    RenderTaskPlan(plan);
                    break;
                case ToolCallProposedEvent proposed:
                    StopPendingIndicator();
                    AddToolCard(proposed.ToolCall);
                    break;
                case ToolCallCompletedEvent completed:
                    CompleteToolCard(completed);
                    break;
                case ErrorEvent error:
                    StopPendingIndicator();
                    AddNotice("Error: " + error.Message, isError: true);
                    break;
                case RunCompletedEvent runCompleted:
                    _LastEstimatedTokens = runCompleted.FinalEstimatedTokens;
                    FinalizeAssistantBubble();
                    AddTurnInfo(runCompleted);
                    UpdateContextIndicator();
                    break;
                default:
                    break;
            }
        }

        private void AddToolCard(ToolCall toolCall)
        {
            ToolCardView card = new ToolCardView(toolCall, _Theme);
            if (!string.IsNullOrEmpty(toolCall.Id))
            {
                _ToolCards[toolCall.Id] = card;
            }

            _Transcript.Children.Add(card.Root);
            _TranscriptScroll.ScrollToEnd();
            UpdateEmptyState();
        }

        private void CompleteToolCard(ToolCallCompletedEvent completed)
        {
            if (!string.IsNullOrEmpty(completed.ToolCallId) && _ToolCards.TryGetValue(completed.ToolCallId, out ToolCardView? card) && card != null)
            {
                card.Complete(completed.Result.Success, completed.ElapsedMs, completed.Result.Content);
            }
            else
            {
                AddNotice((completed.Result.Success ? "✓ " : "✗ ") + completed.ToolName + "  (" + completed.ElapsedMs + " ms)", isError: !completed.Result.Success);
            }
        }

        private void AddTurnInfo(RunCompletedEvent completed)
        {
            long total = _TurnStopwatch != null ? _TurnStopwatch.ElapsedMilliseconds : completed.DurationMs;
            long ttft = _TurnTtftMs ?? 0;
            long streaming = Math.Max(0, total - ttft);

            string tip = "Time to first token: " + ttft + " ms\n"
                + "Streaming: " + streaming + " ms\n"
                + "Total: " + FormatMs(total) + "\n"
                + "Tokens: input " + completed.InputTokens + " · output " + completed.OutputTokens + " · total " + completed.TotalTokens;

            TextBlock info = new TextBlock
            {
                Text = "ⓘ  " + FormatMs(total) + " · " + completed.TotalTokens + " tokens",
                Foreground = _Theme.Muted,
                FontSize = 11,
                Margin = new Thickness(2, -4, 0, 0)
            };
            ToolTip.SetTip(info, tip);

            _Transcript.Children.Add(info);
            _TranscriptScroll.ScrollToEnd();
        }

        private async Task PersistCurrentAsync()
        {
            if (_Conversation == null || string.IsNullOrEmpty(_CurrentThreadId))
            {
                return;
            }

            // Until the conversation is substantial, use a quick heuristic title (the first user message).
            if (!_CurrentTitlePinned && !_TitleSummarized)
            {
                string firstUser = FirstUserMessage(_Conversation.History);
                if (!string.IsNullOrWhiteSpace(firstUser))
                {
                    _CurrentTitle = SessionTitleHelper.Normalize(firstUser, SessionTitleHelper.DefaultTitle);
                    _TitleText.Text = DisplayTitle(_CurrentTitle);
                    SyncActiveTabTitle(_CurrentTitle);
                }
            }

            SessionSnapshot snapshot = new SessionSnapshot
            {
                Id = _CurrentThreadId,
                Title = _CurrentTitle,
                TitlePinned = _CurrentTitlePinned,
                CreatedUtc = _CurrentCreatedUtc,
                UpdatedUtc = DateTime.UtcNow,
                EndpointName = SelectedEndpointName() ?? string.Empty,
                Model = SelectedModel() ?? string.Empty,
                ConversationHistory = new List<ConversationMessage>(_Conversation.History)
            };

            try
            {
                await _Store.SaveAsync(snapshot, CancellationToken.None);
            }
            catch (Exception)
            {
                // Best-effort persistence.
            }

            // Once the conversation crosses the threshold, replace the heuristic title with an AI summary
            // of the whole conversation (once). This becomes the session name in the nav.
            if (!_CurrentTitlePinned && !_TitleSummarized && ConversationCharCount(_Conversation.History) >= TitleSummaryThreshold)
            {
                _TitleSummarized = true;
                _ = GenerateAndApplyTitleAsync(_CurrentThreadId, new List<ConversationMessage>(_Conversation.History));
            }
        }

        private static int ConversationCharCount(IReadOnlyList<ConversationMessage>? history)
        {
            if (history == null)
            {
                return 0;
            }

            int total = 0;
            foreach (ConversationMessage message in history)
            {
                if (!string.IsNullOrEmpty(message.Content))
                {
                    total += message.Content!.Length;
                }
            }

            return total;
        }

        private async Task GenerateAndApplyTitleAsync(string threadId, List<ConversationMessage> history)
        {
            string? title = await _Runner.GenerateTitleAsync(history, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            // Only apply while the same, unpinned conversation is still open.
            if (!string.Equals(threadId, _CurrentThreadId, StringComparison.Ordinal) || _CurrentTitlePinned)
            {
                return;
            }

            _CurrentTitle = title!;
            _TitleText.Text = DisplayTitle(title!);
            SyncActiveTabTitle(title!);

            // Persist the summarized title (PersistCurrentAsync no longer overwrites it since _TitleSummarized
            // is set) and refresh the nav so the session shows its new name.
            await PersistCurrentAsync();
            await LoadThreadsAsync();
        }

        // ---- thread actions ----------------------------------------------------------------------

        private async Task RenameThreadAsync(string id, string currentTitle)
        {
            string? result = await new InputDialog("Rename conversation", "New title:", currentTitle).ShowDialog<string?>(this);
            if (string.IsNullOrWhiteSpace(result))
            {
                return;
            }

            ThreadSummary? updated = await _Threads.RenameAsync(id, result, CancellationToken.None);
            if (updated != null && string.Equals(id, _CurrentThreadId, StringComparison.Ordinal))
            {
                _CurrentTitle = updated.Title;
                _CurrentTitlePinned = true;
                _TitleText.Text = DisplayTitle(updated.Title);
                SyncActiveTabTitle(updated.Title);
            }

            await LoadThreadsAsync();
        }

        private async Task DeleteThreadAsync(string id, string title)
        {
            bool confirmed = await new ConfirmDialog("Delete conversation", "Delete \"" + title + "\"? This cannot be undone.", "Delete", destructive: true).ShowDialog<bool>(this);
            if (!confirmed)
            {
                return;
            }

            await _Threads.DeleteAsync(id, CancellationToken.None);

            WorkspaceTabViewModel? tab = FindTabById(id);
            if (tab != null)
            {
                tab.PropertyChanged -= OnTabPropertyChanged;
                _Workspace.CloseTab(tab);
                _TabTranscripts.Remove(id);
                RenderTabStrip();
            }

            if (string.Equals(id, _CurrentThreadId, StringComparison.Ordinal))
            {
                if (_Workspace.ActiveTab != null)
                {
                    await OpenThreadAsync(_Workspace.ActiveTab.Id);
                }
                else
                {
                    ClearActiveConversation();
                }
            }

            await LoadThreadsAsync();
        }

        private async Task ExportThreadAsync(string id, string title)
        {
            string? content = await _Threads.ExportAsync(id, "md", CancellationToken.None);
            if (content == null)
            {
                return;
            }

            try
            {
                string exportsDir = Path.Combine(_ConfigDirectory, "exports");
                Directory.CreateDirectory(exportsDir);
                string path = Path.Combine(exportsDir, SafeFileName(title) + ".md");
                await File.WriteAllTextAsync(path, content);
                await new ConfirmDialog("Exported", "Saved to:\n" + path, "OK", destructive: false).ShowDialog<bool>(this);
            }
            catch (Exception ex)
            {
                await new ConfirmDialog("Export failed", ex.Message, "OK", destructive: false).ShowDialog<bool>(this);
            }
        }

        // ---- streaming/rendering helpers ---------------------------------------------------------

        private void ResetStreamingState()
        {
            StopPendingIndicator();
            _StreamingBlock = null;
            _AssistantBorder = null;
            _ThinkingSection = null;
            _ThinkingText = null;
            _TaskPlanBody = null;
            _ToolCards.Clear();
        }

        private void StartPendingIndicator()
        {
            _PendingText = new TextBlock { Text = "✳ " + ThinkingQuips.At(0), Foreground = _Theme.Muted, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap };
            _PendingBubble = new Border
            {
                Background = _Theme.AssistantBubble,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 60, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = _PendingText
            };
            _Transcript.Children.Add(_PendingBubble);
            _TranscriptScroll.ScrollToEnd();

            _QuipIndex = 0;
            _QuipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            _QuipTimer.Tick += OnQuipTick;
            _QuipTimer.Start();
        }

        private void OnQuipTick(object? sender, EventArgs e)
        {
            _QuipIndex++;
            if (_PendingText != null)
            {
                _PendingText.Text = "✳ " + ThinkingQuips.At(_QuipIndex);
            }
        }

        private void StopPendingIndicator()
        {
            if (_QuipTimer != null)
            {
                _QuipTimer.Stop();
                _QuipTimer.Tick -= OnQuipTick;
                _QuipTimer = null;
            }

            if (_PendingBubble != null)
            {
                _Transcript.Children.Remove(_PendingBubble);
                _PendingBubble = null;
                _PendingText = null;
            }
        }

        private void EnsureThinkingSection()
        {
            if (_ThinkingSection != null)
            {
                return;
            }

            _ThinkingText = new TextBlock { Text = string.Empty, Foreground = _Theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace") };
            _ThinkingSection = new CollapsibleSection("💭 Thinking", _Theme, expanded: _AutoExpandThinking);
            _ThinkingSection.Body.Children.Add(_ThinkingText);

            int index = _PendingBubble != null ? _Transcript.Children.IndexOf(_PendingBubble) : _Transcript.Children.Count;
            if (index < 0)
            {
                index = _Transcript.Children.Count;
            }

            _Transcript.Children.Insert(index, _ThinkingSection.Root);
        }

        private void EnsureAssistantBubble()
        {
            if (_StreamingBlock != null)
            {
                return;
            }

            _StreamingBlock = AddAssistantBubble();
        }

        private void RenderPersistedMessage(ConversationMessage message)
        {
            if (message.Role == RoleEnum.User && !string.IsNullOrEmpty(message.Content))
            {
                AddUserBubble(message.Content!);
            }
            else if (message.Role == RoleEnum.Assistant && !string.IsNullOrEmpty(message.Content))
            {
                AddAssistantMarkdownBubble(message.Content!);
            }
            else if (message.Role == RoleEnum.System && !string.IsNullOrEmpty(message.Content)
                && message.Content!.StartsWith(ConversationCompactor.SummaryPrefix, StringComparison.Ordinal))
            {
                AddNotice("🗜 Earlier conversation summarized to save context.", isError: false);
            }
        }

        private void AddUserBubble(string text)
        {
            SelectableTextBlock content = new SelectableTextBlock { Text = text, Foreground = _Theme.Text, TextWrapping = TextWrapping.Wrap };
            Border bubble = new Border
            {
                Background = _Theme.UserBubble,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(60, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 640,
                Child = content
            };
            _Transcript.Children.Add(bubble);
            _TranscriptScroll.ScrollToEnd();
        }

        private TextBlock AddAssistantBubble()
        {
            SelectableTextBlock content = new SelectableTextBlock { Text = string.Empty, Foreground = _Theme.Text, TextWrapping = TextWrapping.Wrap };
            Border host = new Border { Child = content };
            _AssistantContentHost = host;
            Border bubble = NewAssistantBorder(host, () => _StreamingBlock?.Text ?? content.Text ?? string.Empty);
            _AssistantBorder = bubble;
            _Transcript.Children.Add(bubble);
            _TranscriptScroll.ScrollToEnd();
            return content;
        }

        private void AddAssistantMarkdownBubble(string content)
        {
            Control body;
            try
            {
                body = MarkdownRenderer.Render(content, _Theme);
            }
            catch (Exception)
            {
                body = new SelectableTextBlock { Text = content, Foreground = _Theme.Text, TextWrapping = TextWrapping.Wrap };
            }

            _Transcript.Children.Add(NewAssistantBorder(new Border { Child = body }, () => content));
            _TranscriptScroll.ScrollToEnd();
        }

        private Border NewAssistantBorder(Control content, Func<string> rawTextProvider)
        {
            Button copy = new Button
            {
                Content = "⧉",
                Background = Brushes.Transparent,
                Foreground = _Theme.Muted,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 0, 4, 0),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -6, -6)
            };
            copy.Tip("Copy this response to the clipboard.");
            copy.Click += async (sender, args) =>
            {
                try
                {
                    IClipboard? clipboard = TopLevel.GetTopLevel(copy)?.Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(rawTextProvider() ?? string.Empty);
                        copy.Content = "✓";
                        await Task.Delay(1200);
                        copy.Content = "⧉";
                    }
                }
                catch (Exception)
                {
                    // Best-effort copy.
                }
            };

            Grid layout = new Grid();
            layout.Children.Add(content);
            layout.Children.Add(copy);

            return new Border
            {
                Background = _Theme.AssistantBubble,
                BorderBrush = _Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 60, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = layout
            };
        }

        // Replace the streamed plain-text block with rendered Markdown once the answer is complete.
        private void FinalizeAssistantBubble()
        {
            if (_AssistantContentHost != null && _StreamingBlock != null)
            {
                try
                {
                    // Replace only the content (not the whole bubble) so the copy icon stays in place.
                    _AssistantContentHost.Child = MarkdownRenderer.Render(_StreamingBlock.Text, _Theme);
                }
                catch (Exception)
                {
                    // Keep the raw, already-selectable streamed text if Markdown rendering ever fails.
                }
            }

            _AssistantBorder = null;
            _AssistantContentHost = null;
            _StreamingBlock = null;
        }

        private void AddNotice(string text, bool isError)
        {
            _Transcript.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = isError ? _Theme.Error : _Theme.Muted,
                FontSize = 12,
                FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
                TextWrapping = TextWrapping.Wrap
            });
            _TranscriptScroll.ScrollToEnd();
            UpdateEmptyState();
        }

        private void RenderTaskPlan(TaskPlanUpdatedEvent plan)
        {
            if (_TaskPlanBody == null)
            {
                _TaskPlanBody = new StackPanel { Spacing = 3 };
                Border host = new Border
                {
                    Background = _Theme.SurfaceAlt,
                    BorderBrush = _Theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 60, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = _TaskPlanBody
                };
                _Transcript.Children.Add(host);
            }

            _TaskPlanBody.Children.Clear();
            _TaskPlanBody.Children.Add(new TextBlock
            {
                Text = "📋 Tasks (" + plan.CompletedCount + "/" + plan.TotalCount + ")",
                FontWeight = FontWeight.SemiBold,
                Foreground = _Theme.Text,
                Margin = new Thickness(0, 0, 0, 2)
            });

            foreach (AgentTask task in plan.Tasks)
            {
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new TextBlock { Text = TaskGlyph(task.Status), Foreground = TaskColor(task.Status), Width = 16, VerticalAlignment = VerticalAlignment.Top });
                row.Children.Add(new TextBlock { Text = task.Title, Foreground = task.Status == AgentTaskStatusEnum.Completed ? _Theme.Muted : _Theme.Text, TextWrapping = TextWrapping.Wrap });
                _TaskPlanBody.Children.Add(row);
            }

            _TranscriptScroll.ScrollToEnd();
        }

        private static string TaskGlyph(AgentTaskStatusEnum status)
        {
            switch (status)
            {
                case AgentTaskStatusEnum.Completed:
                    return "✓";
                case AgentTaskStatusEnum.InProgress:
                    return "◐";
                case AgentTaskStatusEnum.Failed:
                    return "✗";
                case AgentTaskStatusEnum.Skipped:
                    return "–";
                default:
                    return "○";
            }
        }

        private IBrush TaskColor(AgentTaskStatusEnum status)
        {
            switch (status)
            {
                case AgentTaskStatusEnum.Completed:
                    return _Theme.Success;
                case AgentTaskStatusEnum.Failed:
                    return _Theme.Error;
                case AgentTaskStatusEnum.InProgress:
                    return _Theme.Accent;
                default:
                    return _Theme.Muted;
            }
        }

        private void UpdateEmptyState()
        {
            bool empty = _Transcript.Children.Count == 0;
            if (empty && !_EmptyState.IsVisible)
            {
                _ = PopulateOverviewAsync();
            }

            _EmptyState.IsVisible = empty;
        }

        private async Task PopulateOverviewAsync()
        {
            if (_OverviewHost == null)
            {
                return;
            }

            if (_QuipText != null)
            {
                _QuipText.Text = WelcomeQuips.Next();
            }

            _OverviewHost.Children.Clear();

            // Configuration snapshot.
            try
            {
                List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
                MuxSettings settings = SettingsLoader.LoadSettings();
                int mcpCount = SettingsLoader.LoadMcpServers().Count;

                EndpointConfig? def = null;
                foreach (EndpointConfig endpoint in endpoints)
                {
                    if (endpoint.IsDefault)
                    {
                        def = endpoint;
                        break;
                    }
                }

                if (def == null && endpoints.Count > 0)
                {
                    def = endpoints[0];
                }

                WrapPanel stats = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
                stats.Children.Add(StatPill("Endpoints", endpoints.Count.ToString(), "Configured model endpoints. Manage under Manage ▸ Endpoints."));
                stats.Children.Add(StatPill("Default model", def != null ? (string.IsNullOrEmpty(def.Model) ? def.Name : def.Model) : "none", "The endpoint new conversations use."));
                stats.Children.Add(StatPill("MCP servers", mcpCount.ToString(), "Configured MCP tool servers."));
                stats.Children.Add(StatPill("Skills", settings.SkillsEnabled ? "on" : "off", "Whether user skills are loaded."));
                stats.Children.Add(StatPill("Telemetry", settings.Telemetry.Enabled ? "on" : "off", "Whether usage analytics are recorded."));
                _OverviewHost.Children.Add(stats);
            }
            catch (Exception)
            {
                // Skip the config snapshot on any read failure.
            }

            // Recent conversations.
            try
            {
                IReadOnlyList<ThreadSummary> threads = await _Threads.ListAsync(CancellationToken.None);
                if (threads.Count > 0)
                {
                    StackPanel recent = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Stretch };
                    recent.Children.Add(new TextBlock { Text = "Recent conversations", FontWeight = FontWeight.SemiBold, Foreground = _Theme.Text, Margin = new Thickness(2, 6, 0, 2) });

                    int shown = 0;
                    foreach (ThreadSummary summary in threads)
                    {
                        if (shown >= 5)
                        {
                            break;
                        }

                        recent.Children.Add(RecentConversationRow(summary));
                        shown++;
                    }

                    _OverviewHost.Children.Add(recent);
                }
            }
            catch (Exception)
            {
                // Skip recent conversations on failure.
            }
        }

        private Control StatPill(string label, string value, string tip)
        {
            StackPanel content = new StackPanel { Spacing = 1 };
            content.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), Foreground = _Theme.Muted, FontSize = 10, FontWeight = FontWeight.SemiBold });
            content.Children.Add(new TextBlock { Text = value, Foreground = _Theme.Text, FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });

            return new Border
            {
                Background = _Theme.SurfaceAlt,
                BorderBrush = _Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 8, 8),
                MinWidth = 120,
                Child = content
            }.Tip(tip);
        }

        private Control RecentConversationRow(ThreadSummary summary)
        {
            Button button = new Button
            {
                Content = new TextBlock { Text = DisplayTitle(summary), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = _Theme.Muted, FontSize = 13 },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4, 8, 4)
            };
            button.Tip("Open this conversation.");
            button.Click += (sender, args) => _ = OpenThreadAsync(summary.Id);
            return button;
        }

        private void InsertComposerNewline()
        {
            string text = _Composer.Text ?? string.Empty;
            int caret = Math.Clamp(_Composer.CaretIndex, 0, text.Length);
            _Composer.Text = text.Substring(0, caret) + "\n" + text.Substring(caret);
            _Composer.CaretIndex = caret + 1;
        }

        // ---- small helpers -----------------------------------------------------------------------

        private static string DisplayTitle(ThreadSummary? summary)
        {
            return summary == null ? UntitledTitle : DisplayTitle(summary.Title);
        }

        private static string DisplayTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title) || string.Equals(title, SessionTitleHelper.DefaultTitle, StringComparison.Ordinal))
            {
                return UntitledTitle;
            }

            return title!;
        }

        private static string FormatMs(long milliseconds)
        {
            if (milliseconds < 1000)
            {
                return milliseconds + " ms";
            }

            return (milliseconds / 1000.0).ToString("0.0") + " s";
        }

        private string? SelectedEndpointName()
        {
            return _ModelPicker.SelectedItem is EndpointConfig endpoint ? endpoint.Name : null;
        }

        private string? SelectedModel()
        {
            return _ModelPicker.SelectedItem is EndpointConfig endpoint ? endpoint.Model : null;
        }

        private Task<string> ApproveToolAsync(ToolCall toolCall)
        {
            TaskCompletionSource<string> completion = new TaskCompletionSource<string>();

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    string result = await new ApprovalDialog(toolCall).ShowDialog<string>(this);
                    completion.SetResult(string.IsNullOrEmpty(result) ? "n" : result);
                }
                catch (Exception)
                {
                    completion.SetResult("n");
                }
            });

            return completion.Task;
        }

        private static string SafeFileName(string title)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string result = title;
            foreach (char c in invalid)
            {
                result = result.Replace(c, '_');
            }

            result = result.Trim();
            return result.Length == 0 ? "conversation" : result;
        }

        private static string FirstUserMessage(IReadOnlyList<ConversationMessage> history)
        {
            foreach (ConversationMessage message in history)
            {
                if (message.Role == RoleEnum.User && !string.IsNullOrWhiteSpace(message.Content))
                {
                    return message.Content!;
                }
            }

            return string.Empty;
        }
    }
}
