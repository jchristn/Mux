namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.Primitives;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Models;
    using Mux.Core.Settings;

    /// <summary>
    /// A grouped, form-based editor over the mux settings — general, context and compaction, jobs and
    /// concurrency, tools and timeouts, skills, and telemetry. Reads and writes <c>settings.json</c> through
    /// <see cref="SettingsLoader"/>. The action buttons sit outside the scrollable form.
    /// </summary>
    public sealed class SettingsWindow : Window
    {
        private readonly MuxSettings _Settings;
        private readonly Action<bool>? _OnThemeSelected;

        private readonly ComboBox _ThemeSelector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly ComboBox _ApprovalPolicy = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _MaxIterations = new TextBox();
        private readonly TextBox _SystemPromptPath = new TextBox();
        private readonly TextBox _MaxTokenBudget = new TextBox();

        private readonly CheckBox _AutoCompact = new CheckBox { Content = "Automatically compact long conversations" };
        private readonly ComboBox _CompactionStrategy = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _CompactionPreserveTurns = new TextBox();
        private readonly TextBox _ContextWarningThreshold = new TextBox();
        private readonly TextBox _ContextSafetyMargin = new TextBox();
        private readonly TextBox _TokenEstimationRatio = new TextBox();

        private readonly TextBox _MaxConcurrency = new TextBox();
        private readonly ComboBox _EnqueueBehavior = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly CheckBox _TaskPlanning = new CheckBox { Content = "Enable background task planning" };
        private readonly CheckBox _TaskParallelism = new CheckBox { Content = "Allow ready tasks to fan out to parallel jobs" };

        private readonly TextBox _ToolTimeout = new TextBox();
        private readonly TextBox _ProcessTimeout = new TextBox();
        private readonly CheckBox _IgnoreCertErrors = new CheckBox { Content = "Ignore TLS certificate errors (intercepting proxies)" };
        private readonly CheckBox _ShowBoundaryLines = new CheckBox { Content = "Show boundary lines in the interactive shell" };

        private readonly CheckBox _SkillsEnabled = new CheckBox { Content = "Load user skills" };
        private readonly TextBox _SkillRefreshInterval = new TextBox();
        private readonly TextBox _SkillsDirectory = new TextBox();

        private readonly CheckBox _Telemetry = new CheckBox { Content = "Record usage telemetry" };

        private readonly TextBlock _Status = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#2f855a")), VerticalAlignment = VerticalAlignment.Center };

        /// <summary>
        /// Instantiate the settings window, loading current values.
        /// </summary>
        /// <param name="onThemeSelected">Invoked when the theme selector changes (dark = true).</param>
        public SettingsWindow(Action<bool>? onThemeSelected = null)
        {
            Title = "Settings";
            Icon = IconResources.LoadWindowIcon();
            Width = 760;
            Height = 760;
            MinWidth = 560;
            MinHeight = 480;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            _OnThemeSelected = onThemeSelected;
            _Settings = SettingsLoader.LoadSettings();

            _ThemeSelector.ItemsSource = new List<string> { "Light", "Dark" };
            _ThemeSelector.SelectedItem = AppTheme.Current.IsDark ? "Dark" : "Light";
            _ThemeSelector.SelectionChanged += (sender, args) =>
            {
                if (_ThemeSelector.SelectedItem is string variant)
                {
                    _OnThemeSelected?.Invoke(string.Equals(variant, "Dark", StringComparison.Ordinal));
                }
            };

            _ApprovalPolicy.ItemsSource = new List<string> { "ask", "auto", "deny" };
            _ApprovalPolicy.SelectedItem = Normalize(_Settings.DefaultApprovalPolicy, "ask", "auto", "deny");
            _CompactionStrategy.ItemsSource = new List<string> { "summary", "trim" };
            _CompactionStrategy.SelectedItem = Normalize(_Settings.CompactionStrategy, "summary", "trim");
            _EnqueueBehavior.ItemsSource = new List<string> { "ask", "run_now", "queue_after", "add_to_focused" };
            _EnqueueBehavior.SelectedItem = Normalize(_Settings.DefaultEnqueueBehavior, "ask", "run_now", "queue_after", "add_to_focused");

            _MaxIterations.Text = _Settings.MaxAgentIterations.ToString(CultureInfo.InvariantCulture);
            _SystemPromptPath.Text = _Settings.SystemPromptPath ?? string.Empty;
            _MaxTokenBudget.Text = _Settings.MaxTokenBudget?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _AutoCompact.IsChecked = _Settings.AutoCompactEnabled;
            _CompactionPreserveTurns.Text = _Settings.CompactionPreserveTurns.ToString(CultureInfo.InvariantCulture);
            _ContextWarningThreshold.Text = _Settings.ContextWarningThresholdPercent.ToString(CultureInfo.InvariantCulture);
            _ContextSafetyMargin.Text = _Settings.ContextWindowSafetyMarginPercent.ToString(CultureInfo.InvariantCulture);
            _TokenEstimationRatio.Text = _Settings.TokenEstimationRatio.ToString(CultureInfo.InvariantCulture);
            _MaxConcurrency.Text = _Settings.MaxConcurrency.ToString(CultureInfo.InvariantCulture);
            _TaskPlanning.IsChecked = _Settings.TaskPlanningEnabled;
            _TaskParallelism.IsChecked = _Settings.TaskParallelismEnabled;
            _ToolTimeout.Text = _Settings.ToolTimeoutMs.ToString(CultureInfo.InvariantCulture);
            _ProcessTimeout.Text = _Settings.ProcessTimeoutMs.ToString(CultureInfo.InvariantCulture);
            _IgnoreCertErrors.IsChecked = _Settings.IgnoreCertErrors;
            _ShowBoundaryLines.IsChecked = _Settings.ShowBoundaryLines;
            _SkillsEnabled.IsChecked = _Settings.SkillsEnabled;
            _SkillRefreshInterval.Text = _Settings.SkillRefreshIntervalSeconds.ToString(CultureInfo.InvariantCulture);
            _SkillsDirectory.Text = _Settings.SkillsDirectory ?? string.Empty;
            _Telemetry.IsChecked = _Settings.Telemetry.Enabled;

            Content = BuildContent();
        }

        private Control BuildContent()
        {
            AppTheme theme = AppTheme.Current;
            DockPanel root = new DockPanel { Margin = new Thickness(24) };

            TextBlock title = new TextBlock { Text = "Settings", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text };
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(_Status);
            Button close = new Button { Content = "Close" };
            close.Tip("Close this window. Unsaved changes are discarded.");
            close.Click += (sender, args) => Close();
            buttons.Children.Add(close);
            Button save = new Button { Content = "Save", Background = theme.AccentButton, Foreground = theme.AccentText };
            save.Tip("Write these settings to settings.json.");
            save.Click += (sender, args) => Save();
            buttons.Children.Add(save);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            StackPanel form = new StackPanel { Spacing = 8, Margin = new Thickness(0, 14, 14, 0) };

            form.Children.Add(Section("General"));
            form.Children.Add(LabeledRow("Theme", _ThemeSelector, "Light or dark appearance for the whole app; applies immediately."));
            form.Children.Add(LabeledRow("Default approval policy", _ApprovalPolicy, "Stored default for how tool calls are approved (ask / auto / deny). The desktop app still auto-runs only read-only tools."));
            form.Children.Add(LabeledRow("Max agent iterations (1–100)", _MaxIterations, "Hard cap on agent-loop turns before a run is forced to stop."));
            form.Children.Add(LabeledRow("Max token budget (blank = off)", _MaxTokenBudget, "Optional ceiling on estimated working-context tokens per run; the run stops cleanly if exceeded."));
            form.Children.Add(LabeledRow("System prompt path (blank = default)", _SystemPromptPath, "Path to a custom system-prompt file. Blank uses the built-in persona."));

            form.Children.Add(Section("Context & compaction"));
            form.Children.Add(_AutoCompact.Tip("Automatically summarize or trim old history before a run when the prompt would overflow the context window."));
            form.Children.Add(LabeledRow("Compaction strategy", _CompactionStrategy, "How history is shrunk: “summary” replaces old turns with a summary; “trim” drops the oldest turns."));
            form.Children.Add(LabeledRow("Preserve recent turns (1–10)", _CompactionPreserveTurns, "How many of the most recent user-led turns compaction always keeps verbatim."));
            form.Children.Add(LabeledRow("Context warning threshold % (50–95)", _ContextWarningThreshold, "The share of the usable input budget at which mux starts warning about context pressure."));
            form.Children.Add(LabeledRow("Context safety margin % (5–50)", _ContextSafetyMargin, "Portion of the context window held in reserve so responses have room to generate."));
            form.Children.Add(LabeledRow("Token estimation ratio (2.0–6.0)", _TokenEstimationRatio, "Characters-per-token estimate used for quick token counts before a call."));

            form.Children.Add(Section("Jobs & concurrency"));
            form.Children.Add(LabeledRow("Max concurrency (1–32)", _MaxConcurrency, "The most jobs allowed to run at the same time."));
            form.Children.Add(LabeledRow("Default enqueue behavior", _EnqueueBehavior, "What happens when you submit while a job is busy: ask, run now, queue after, or add to the focused job."));
            form.Children.Add(_TaskPlanning.Tip("Let the model break a job into a tracked plan of tasks with the plan/update-task tools."));
            form.Children.Add(_TaskParallelism.Tip("Allow dependency-ready tasks to fan out as their own concurrent jobs (needs task planning on)."));

            form.Children.Add(Section("Tools & network"));
            form.Children.Add(LabeledRow("Tool timeout (ms, 1000–300000)", _ToolTimeout, "How long a single tool call may run before it is cancelled."));
            form.Children.Add(LabeledRow("Process timeout (ms, 1000–600000)", _ProcessTimeout, "How long a spawned command process may run before it is killed."));
            form.Children.Add(_IgnoreCertErrors.Tip("Skip TLS certificate validation for mux's own network calls — only for enterprise proxies that intercept TLS."));
            form.Children.Add(_ShowBoundaryLines.Tip("Draw dark-grey boundary rules in the interactive terminal shell (TUI only)."));

            form.Children.Add(Section("Skills"));
            form.Children.Add(_SkillsEnabled.Tip("Load user-authored skills and expose them to the model."));
            form.Children.Add(LabeledRow("Skill refresh interval (s, min 5)", _SkillRefreshInterval, "How often the shell re-scans the skills directory for changes."));
            form.Children.Add(LabeledRow("Skills directory (blank = default)", _SkillsDirectory, "Override the skills folder (e.g. a shared library). Blank uses ~/.mux/skills."));

            form.Children.Add(Section("Telemetry"));
            form.Children.Add(_Telemetry.Tip("Record token/latency/cost history to a local SQLite database that powers the Usage dashboard."));

            form.Children.Add(new TextBlock
            {
                Text = "The desktop app runs tools under an auto-safe policy regardless of the stored default: "
                    + "read-only tools run automatically; tools that modify files or run commands always prompt.",
                Foreground = theme.Muted,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });

            root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            return root;
        }

        private static Control Section(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
                Foreground = AppTheme.Current.Accent,
                Margin = new Thickness(0, 12, 0, 2)
            };
        }

        private static Control LabeledRow(string label, Control control, string tip)
        {
            DockPanel row = new DockPanel();
            TextBlock text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 280, Foreground = AppTheme.Current.Text };
            DockPanel.SetDock(text, Dock.Left);
            row.Children.Add(text);
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            row.Children.Add(control);
            return row.Tip(tip);
        }

        private void Save()
        {
            _Settings.DefaultApprovalPolicy = _ApprovalPolicy.SelectedItem as string ?? _Settings.DefaultApprovalPolicy;
            _Settings.CompactionStrategy = _CompactionStrategy.SelectedItem as string ?? _Settings.CompactionStrategy;
            _Settings.DefaultEnqueueBehavior = _EnqueueBehavior.SelectedItem as string ?? _Settings.DefaultEnqueueBehavior;

            AssignInt(_MaxIterations, value => _Settings.MaxAgentIterations = value);
            AssignInt(_CompactionPreserveTurns, value => _Settings.CompactionPreserveTurns = value);
            AssignInt(_ContextWarningThreshold, value => _Settings.ContextWarningThresholdPercent = value);
            AssignInt(_ContextSafetyMargin, value => _Settings.ContextWindowSafetyMarginPercent = value);
            AssignInt(_MaxConcurrency, value => _Settings.MaxConcurrency = value);
            AssignInt(_ToolTimeout, value => _Settings.ToolTimeoutMs = value);
            AssignInt(_ProcessTimeout, value => _Settings.ProcessTimeoutMs = value);
            AssignInt(_SkillRefreshInterval, value => _Settings.SkillRefreshIntervalSeconds = value);

            if (double.TryParse(_TokenEstimationRatio.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio))
            {
                _Settings.TokenEstimationRatio = ratio;
            }

            string budgetText = (_MaxTokenBudget.Text ?? string.Empty).Trim();
            _Settings.MaxTokenBudget = budgetText.Length > 0 && int.TryParse(budgetText, out int budget) ? budget : (int?)null;

            _Settings.SystemPromptPath = string.IsNullOrWhiteSpace(_SystemPromptPath.Text) ? null : _SystemPromptPath.Text.Trim();
            _Settings.SkillsDirectory = _SkillsDirectory.Text;

            _Settings.AutoCompactEnabled = _AutoCompact.IsChecked ?? _Settings.AutoCompactEnabled;
            _Settings.TaskPlanningEnabled = _TaskPlanning.IsChecked ?? _Settings.TaskPlanningEnabled;
            _Settings.TaskParallelismEnabled = _TaskParallelism.IsChecked ?? _Settings.TaskParallelismEnabled;
            _Settings.IgnoreCertErrors = _IgnoreCertErrors.IsChecked ?? _Settings.IgnoreCertErrors;
            _Settings.ShowBoundaryLines = _ShowBoundaryLines.IsChecked ?? _Settings.ShowBoundaryLines;
            _Settings.SkillsEnabled = _SkillsEnabled.IsChecked ?? _Settings.SkillsEnabled;
            _Settings.Telemetry.Enabled = _Telemetry.IsChecked ?? _Settings.Telemetry.Enabled;

            try
            {
                SettingsLoader.SaveSettings(_Settings);
                _Status.Foreground = new SolidColorBrush(Color.Parse("#2f855a"));
                _Status.Text = "Saved.";
                SyncBack();
            }
            catch (Exception ex)
            {
                _Status.Foreground = new SolidColorBrush(Color.Parse("#c0392b"));
                _Status.Text = "Save failed: " + ex.Message;
            }
        }

        private void SyncBack()
        {
            // Reflect any clamping the settings model applied.
            _MaxIterations.Text = _Settings.MaxAgentIterations.ToString(CultureInfo.InvariantCulture);
            _CompactionPreserveTurns.Text = _Settings.CompactionPreserveTurns.ToString(CultureInfo.InvariantCulture);
            _ContextWarningThreshold.Text = _Settings.ContextWarningThresholdPercent.ToString(CultureInfo.InvariantCulture);
            _ContextSafetyMargin.Text = _Settings.ContextWindowSafetyMarginPercent.ToString(CultureInfo.InvariantCulture);
            _MaxConcurrency.Text = _Settings.MaxConcurrency.ToString(CultureInfo.InvariantCulture);
            _ToolTimeout.Text = _Settings.ToolTimeoutMs.ToString(CultureInfo.InvariantCulture);
            _ProcessTimeout.Text = _Settings.ProcessTimeoutMs.ToString(CultureInfo.InvariantCulture);
            _SkillRefreshInterval.Text = _Settings.SkillRefreshIntervalSeconds.ToString(CultureInfo.InvariantCulture);
            _TokenEstimationRatio.Text = _Settings.TokenEstimationRatio.ToString(CultureInfo.InvariantCulture);
        }

        private static void AssignInt(TextBox box, Action<int> assign)
        {
            if (int.TryParse((box.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                assign(value);
            }
        }

        private static string Normalize(string? value, params string[] allowed)
        {
            string candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
            foreach (string option in allowed)
            {
                if (string.Equals(option, candidate, StringComparison.Ordinal))
                {
                    return option;
                }
            }

            return allowed[0];
        }
    }
}
