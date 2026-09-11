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
    using Avalonia.Interactivity;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Styling;
    using Avalonia.Threading;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Telemetry;
    using Mux.Desktop.Conversation;
    using Mux.Desktop.I18n;
    using Mux.Desktop.Services;
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
        private ListBox _ThreadList = null!;
        private ComboBox _ModelPicker = null!;
        private TextBlock _TitleText = null!;
        private StackPanel _Transcript = null!;
        private ScrollViewer _TranscriptScroll = null!;
        private StackPanel _EmptyState = null!;
        private TextBox _Composer = null!;
        private Button _SendButton = null!;

        private ConversationService? _Conversation;
        private TextBlock? _StreamingBlock;
        private Border? _AssistantBorder;
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
        private string _CurrentTitle = string.Empty;
        private bool _CurrentTitlePinned;
        private DateTime _CurrentCreatedUtc;
        private bool _SuppressThreadSelection;
        private bool _SidebarCollapsed;
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

            Background = _Theme.Surface;
            Content = BuildLayout();
            PopulateModelPicker();

            AddHandler(InputElement.KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
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
            top.Children.Add(NavItem(_SidebarCollapsed ? "»" : "«", "Toggle sidebar", ToggleSidebarCollapse, accent: false));
            top.Children.Add(NavItem(_SidebarCollapsed ? "＋" : "＋   " + _Localization.Get(StringKeys.NewConversation), "New conversation", () => _ = NewChatAsync(), accent: true));
            if (!conversationsExpanded)
            {
                top.Children.Add(NavItem(_SidebarCollapsed ? "🗂" : "🗂   Conversations      ▸", "Conversations", ToggleConversations, accent: false));
            }

            DockPanel.SetDock(top, Dock.Top);
            panel.Children.Add(top);

            StackPanel bottom = new StackPanel { Spacing = 2 };
            bottom.Children.Add(NavItem(_SidebarCollapsed ? "📊" : "📊   Usage", "Usage", OpenUsageWindow, accent: false));
            if (_ManageOpen && expanded)
            {
                StackPanel manageGroup = new StackPanel { Spacing = 2 };
                manageGroup.Children.Add(NavItem("🛠   Manage           ▾", "Manage", ToggleManage, accent: false));
                manageGroup.Children.Add(ManageDrawerItem("🔌   Endpoints", "Endpoints", OpenEndpointsWindow));
                manageGroup.Children.Add(ManageDrawerItem("🧩   MCP servers", "MCP servers", OpenMcpServersWindow));
                manageGroup.Children.Add(ManageDrawerItem("📝   Prompts", "Prompt profiles", OpenPromptsWindow));
                manageGroup.Children.Add(ManageDrawerItem("✨   Skills", "Skills", OpenSkillsWindow));
                manageGroup.Children.Add(ManageDrawerItem("🤖   Subagents", "Subagents", OpenSubagentsWindow));
                bottom.Children.Add(HighlightBlock(manageGroup));
            }
            else
            {
                bottom.Children.Add(NavItem(_SidebarCollapsed ? "🛠" : "🛠   Manage           ▸", "Manage", ToggleManage, accent: false));
            }

            bottom.Children.Add(NavItem(_SidebarCollapsed ? "⚙" : "⚙   Settings", "Settings", OpenSettingsWindow, accent: false));
            bottom.Children.Add(NavItem(_SidebarCollapsed ? "ⓘ" : "ⓘ   About", "About", OpenAboutWindow, accent: false));
            DockPanel.SetDock(bottom, Dock.Bottom);
            panel.Children.Add(bottom);

            if (conversationsExpanded)
            {
                DockPanel conversations = new DockPanel();
                Button conversationsToggle = NavItem("🗂   Conversations      ▾", "Conversations", ToggleConversations, accent: false);
                DockPanel.SetDock(conversationsToggle, Dock.Top);
                conversations.Children.Add(conversationsToggle);
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
            _ThreadList = new ListBox { Background = Brushes.Transparent, Margin = new Thickness(6, 6, 0, 0) };
            _ThreadList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 4, 8, 4)),
                    new Setter(Layoutable.MinHeightProperty, 0.0)
                }
            });
            _ThreadList.ItemTemplate = new FuncDataTemplate<ThreadSummary>(BuildThreadRow, supportsRecycling: false);
            _ThreadList.SelectionChanged += OnThreadSelected;
            _ = LoadThreadsAsync();
            return _ThreadList;
        }

        private Button NavItem(string content, string tooltip, Action onClick, bool accent)
        {
            Button button = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = _SidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                Background = accent ? _Theme.AccentButton : Brushes.Transparent,
                Foreground = accent ? _Theme.AccentText : _Theme.Text,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 2)
            };
            ToolTip.SetTip(button, tooltip);
            button.Click += (sender, args) => onClick();
            return button;
        }

        private Button ManageDrawerItem(string content, string tooltip, Action onClick)
        {
            Button button = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                Foreground = _Theme.Muted,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(22, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 1),
                FontSize = 13
            };
            ToolTip.SetTip(button, tooltip);
            button.Click += (sender, args) => onClick();
            return button;
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

            if (_SidebarHost != null)
            {
                _SidebarHost.Child = BuildSidebarContent();
            }
        }

        private void OpenSettingsWindow()
        {
            _ = new SettingsWindow(SetThemeDark).ShowDialog(this);
        }

        private void OpenUsageWindow()
        {
            if (_UsageQuery == null)
            {
                AddNotice("Usage telemetry is disabled; enable it in Settings to see analytics.", isError: false);
                return;
            }

            new UsageDashboardWindow(new UsageAnalyticsService(_UsageQuery, true)).Show(this);
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

        private void SetThemeDark(bool dark)
        {
            if (dark == _Theme.IsDark)
            {
                return;
            }

            AppTheme.Set(dark);
            if (Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            }

            RebuildContent();
        }

        private Control BuildThreadRow(ThreadSummary? item, INameScope scope)
        {
            TextBlock label = new TextBlock
            {
                Text = DisplayTitle(item),
                Foreground = _Theme.Text,
                Margin = new Thickness(2, 3, 2, 3),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            if (item != null)
            {
                ContextMenu menu = new ContextMenu();

                MenuItem rename = new MenuItem { Header = "Rename" };
                rename.Click += (sender, args) => _ = RenameThreadAsync(item.Id, item.Title);
                menu.Items.Add(rename);

                MenuItem export = new MenuItem { Header = "Export" };
                export.Click += (sender, args) => _ = ExportThreadAsync(item.Id, DisplayTitle(item));
                menu.Items.Add(export);

                menu.Items.Add(new Separator());

                MenuItem delete = new MenuItem { Header = "Delete" };
                delete.Click += (sender, args) => _ = DeleteThreadAsync(item.Id, DisplayTitle(item));
                menu.Items.Add(delete);

                label.ContextMenu = menu;
            }

            return label;
        }

        private Control BuildChatColumn()
        {
            DockPanel column = new DockPanel();
            column.Children.Add(BuildHeader());
            column.Children.Add(BuildComposer());
            column.Children.Add(BuildTranscriptArea());
            return column;
        }

        private Control BuildHeader()
        {
            DockPanel header = new DockPanel { Height = 56, Margin = new Thickness(20, 0, 16, 0) };

            _TitleText = new TextBlock { Text = DisplayTitle(_CurrentTitle), FontWeight = FontWeight.SemiBold, FontSize = 15, Foreground = _Theme.Text, VerticalAlignment = VerticalAlignment.Center };
            if (string.IsNullOrEmpty(_CurrentThreadId))
            {
                _TitleText.Text = "mux";
            }
            DockPanel.SetDock(_TitleText, Dock.Left);
            header.Children.Add(_TitleText);

            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

            _ModelPicker = new ComboBox { MinWidth = 200, BorderBrush = _Theme.Border };
            _ModelPicker.ItemTemplate = new FuncDataTemplate<EndpointConfig>(
                (item, scope) => new TextBlock { Text = item != null ? item.Name : string.Empty },
                supportsRecycling: true);
            _ModelPicker.SelectionChanged += OnModelSelected;
            right.Children.Add(_ModelPicker);

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
            // Handle keys on the tunnel route so this runs BEFORE the TextBox's own Enter handling; otherwise
            // the TextBox inserts a newline and marks the event handled before we ever see plain Enter.
            _Composer.AddHandler(InputElement.KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);

            _SendButton = AccentButton("Send");
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
            _EmptyState.Children.Add(new TextBlock
            {
                Text = _Localization.Get(StringKeys.EmptyStateTitle),
                FontSize = 20,
                FontWeight = FontWeight.SemiBold,
                Foreground = _Theme.Text,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            _EmptyState.Children.Add(new TextBlock
            {
                Text = "Type a message below and press Enter, or start a new conversation.",
                Foreground = _Theme.Muted,
                MaxWidth = 420,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

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
            try
            {
                IReadOnlyList<ThreadSummary> threads = await _Threads.ListAsync(CancellationToken.None);
                _SuppressThreadSelection = true;
                _ThreadList.ItemsSource = threads;
                RestoreThreadSelection(threads);
                _SuppressThreadSelection = false;
            }
            catch (Exception)
            {
                _SuppressThreadSelection = false;
            }
        }

        private void RestoreThreadSelection(IReadOnlyList<ThreadSummary> threads)
        {
            foreach (ThreadSummary summary in threads)
            {
                if (string.Equals(summary.Id, _CurrentThreadId, StringComparison.Ordinal))
                {
                    _ThreadList.SelectedItem = summary;
                    return;
                }
            }
        }

        // ---- event handlers ----------------------------------------------------------------------

        private void OnThreadSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (_SuppressThreadSelection)
            {
                return;
            }

            if (_ThreadList.SelectedItem is ThreadSummary summary && !string.Equals(summary.Id, _CurrentThreadId, StringComparison.Ordinal))
            {
                _ = OpenThreadAsync(summary.Id);
            }
        }

        private void OnModelSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (_ModelPicker.SelectedItem is EndpointConfig endpoint)
            {
                _Runner.EndpointName = endpoint.Name;
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
            _Runner.SessionId = snapshot.Id;
            _LastEstimatedTokens = 0;
            _CurrentTitle = snapshot.Title;
            _CurrentTitlePinned = snapshot.TitlePinned;
            _CurrentCreatedUtc = snapshot.CreatedUtc == default ? DateTime.UtcNow : snapshot.CreatedUtc;
            _TitleText.Text = DisplayTitle(snapshot.Title);

            if (_Conversation != null)
            {
                _Conversation.Event -= OnConversationEvent;
            }

            _Conversation = new ConversationService(_Runner, snapshot.ConversationHistory);
            _Conversation.Event += OnConversationEvent;

            ResetStreamingState();
            _Transcript.Children.Clear();
            foreach (ConversationMessage message in snapshot.ConversationHistory)
            {
                RenderPersistedMessage(message);
            }

            UpdateEmptyState();
        }

        private async Task SendAsync()
        {
            string prompt = (_Composer.Text ?? string.Empty).Trim();
            if (prompt.Length == 0)
            {
                return;
            }

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

        private void SetSending(bool sending)
        {
            _SendButton.Content = sending ? "Stop" : "Send";
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
            card.Children.Add(CommandRow("/usage", "Open the usage dashboard"));
            card.Children.Add(CommandRow("/endpoints", "Manage model endpoints"));
            card.Children.Add(CommandRow("/mcp", "Manage MCP servers"));
            card.Children.Add(CommandRow("/prompt", "Manage prompt profiles"));
            card.Children.Add(CommandRow("/skills", "Manage installed skills"));
            card.Children.Add(CommandRow("/subagents", "Manage subagent definitions"));
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
            return _ModelPicker.SelectedItem is EndpointConfig endpoint ? endpoint.ContextWindow : 0;
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

            if (!_CurrentTitlePinned)
            {
                string firstUser = FirstUserMessage(_Conversation.History);
                if (!string.IsNullOrWhiteSpace(firstUser))
                {
                    _CurrentTitle = SessionTitleHelper.Normalize(firstUser, SessionTitleHelper.DefaultTitle);
                    _TitleText.Text = DisplayTitle(_CurrentTitle);
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

            if (string.Equals(id, _CurrentThreadId, StringComparison.Ordinal))
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
            _ThinkingSection = new CollapsibleSection("💭 Thinking", _Theme);
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
        }

        private void AddUserBubble(string text)
        {
            TextBlock content = new TextBlock { Text = text, Foreground = _Theme.Text, TextWrapping = TextWrapping.Wrap };
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
            TextBlock content = new TextBlock { Text = string.Empty, Foreground = _Theme.Text, TextWrapping = TextWrapping.Wrap };
            Border bubble = NewAssistantBorder(content);
            _AssistantBorder = bubble;
            _Transcript.Children.Add(bubble);
            _TranscriptScroll.ScrollToEnd();
            return content;
        }

        private void AddAssistantMarkdownBubble(string content)
        {
            _Transcript.Children.Add(NewAssistantBorder(MarkdownRenderer.Render(content, _Theme)));
            _TranscriptScroll.ScrollToEnd();
        }

        private Border NewAssistantBorder(Control child)
        {
            return new Border
            {
                Background = _Theme.AssistantBubble,
                BorderBrush = _Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 60, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = child
            };
        }

        // Replace the streamed plain-text block with rendered Markdown once the answer is complete.
        private void FinalizeAssistantBubble()
        {
            if (_AssistantBorder != null && _StreamingBlock != null)
            {
                _AssistantBorder.Child = MarkdownRenderer.Render(_StreamingBlock.Text, _Theme);
            }

            _AssistantBorder = null;
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

        private void UpdateEmptyState()
        {
            _EmptyState.IsVisible = _Transcript.Children.Count == 0;
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
