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
    using Mux.Desktop.I18n;

    /// <summary>
    /// A grouped, form-based editor over the mux settings — general, context and compaction, jobs and
    /// concurrency, tools and timeouts, skills, and telemetry. Reads and writes <c>settings.json</c> through
    /// <see cref="SettingsLoader"/>. The action buttons sit outside the scrollable form. All labels resolve
    /// through the ambient <see cref="Localizer"/>; dropdowns show localized labels but persist canonical
    /// values via a parallel value array indexed by selection.
    /// </summary>
    public sealed class SettingsWindow : Window
    {
        private static readonly string[] ThemeValues = { "system", "light", "dark" };
        private static readonly string[] ApprovalValues = { "ask", "auto", "deny" };
        private static readonly string[] CompactionValues = { "summary", "trim" };
        private static readonly string[] EnqueueValues = { "ask", "run_now", "queue_after", "add_to_focused" };

        private readonly MuxSettings _Settings;
        private readonly Action<string>? _OnThemeMode;

        private readonly ComboBox _ThemeSelector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly ComboBox _ApprovalPolicy = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _MaxIterations = new TextBox();
        private readonly TextBox _SystemPromptPath = new TextBox();
        private readonly TextBox _MaxTokenBudget = new TextBox();

        private readonly CheckBox _AutoCompact = new CheckBox();
        private readonly ComboBox _CompactionStrategy = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _CompactionPreserveTurns = new TextBox();
        private readonly TextBox _ContextWarningThreshold = new TextBox();
        private readonly TextBox _ContextSafetyMargin = new TextBox();
        private readonly TextBox _TokenEstimationRatio = new TextBox();

        private readonly TextBox _MaxConcurrency = new TextBox();
        private readonly ComboBox _EnqueueBehavior = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly CheckBox _TaskPlanning = new CheckBox();
        private readonly CheckBox _TaskParallelism = new CheckBox();

        private readonly TextBox _ToolTimeout = new TextBox();
        private readonly TextBox _ProcessTimeout = new TextBox();
        private readonly CheckBox _IgnoreCertErrors = new CheckBox();
        private readonly CheckBox _ShowBoundaryLines = new CheckBox();

        private readonly CheckBox _SkillsEnabled = new CheckBox();
        private readonly TextBox _SkillRefreshInterval = new TextBox();
        private readonly TextBox _SkillsDirectory = new TextBox();

        private readonly CheckBox _Telemetry = new CheckBox();

        private readonly TextBlock _Status = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#2f855a")), VerticalAlignment = VerticalAlignment.Center };

        /// <summary>
        /// Instantiate the settings window, loading current values.
        /// </summary>
        /// <param name="onThemeMode">Invoked when the theme selector changes with "system", "light", or "dark".</param>
        /// <param name="currentThemeMode">The current theme mode to preselect ("system", "light", or "dark").</param>
        public SettingsWindow(Action<string>? onThemeMode = null, string currentThemeMode = "dark")
        {
            Title = Localizer.T("settings.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 760;
            Height = 760;
            MinWidth = 560;
            MinHeight = 480;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AppTheme.Current.Surface;

            _OnThemeMode = onThemeMode;
            _Settings = SettingsLoader.LoadSettings();

            _ThemeSelector.ItemsSource = new List<string>
            {
                Localizer.T("settings.theme.system"), Localizer.T("settings.theme.light"), Localizer.T("settings.theme.dark")
            };
            _ThemeSelector.SelectedIndex = IndexOfValue(ThemeValues, currentThemeMode, 2);
            _ThemeSelector.SelectionChanged += (sender, args) =>
            {
                int index = _ThemeSelector.SelectedIndex;
                if (index >= 0 && index < ThemeValues.Length)
                {
                    _OnThemeMode?.Invoke(ThemeValues[index]);
                }
            };

            _ApprovalPolicy.ItemsSource = new List<string>
            {
                Localizer.T("settings.approval.ask"), Localizer.T("settings.approval.auto"), Localizer.T("settings.approval.deny")
            };
            _ApprovalPolicy.SelectedIndex = IndexOfValue(ApprovalValues, _Settings.DefaultApprovalPolicy, 0);

            _CompactionStrategy.ItemsSource = new List<string>
            {
                Localizer.T("settings.compact.summary"), Localizer.T("settings.compact.trim")
            };
            _CompactionStrategy.SelectedIndex = IndexOfValue(CompactionValues, _Settings.CompactionStrategy, 0);

            _EnqueueBehavior.ItemsSource = new List<string>
            {
                Localizer.T("settings.enqueue.ask"), Localizer.T("settings.enqueue.run_now"),
                Localizer.T("settings.enqueue.queue_after"), Localizer.T("settings.enqueue.add_to_focused")
            };
            _EnqueueBehavior.SelectedIndex = IndexOfValue(EnqueueValues, _Settings.DefaultEnqueueBehavior, 0);

            _AutoCompact.Content = Localizer.T("settings.autoCompact");
            _TaskPlanning.Content = Localizer.T("settings.taskPlanning");
            _TaskParallelism.Content = Localizer.T("settings.taskParallel");
            _IgnoreCertErrors.Content = Localizer.T("settings.ignoreCert");
            _ShowBoundaryLines.Content = Localizer.T("settings.boundaryLines");
            _SkillsEnabled.Content = Localizer.T("settings.loadSkills");
            _Telemetry.Content = Localizer.T("settings.recordTelemetry");

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

            TextBlock title = new TextBlock { Text = Localizer.T("settings.title"), FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = theme.Text };
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(_Status);
            Button close = new Button { Content = Localizer.T("act.close") };
            close.Tip(Localizer.T("settings.close.tip"));
            close.Click += (sender, args) => Close();
            buttons.Children.Add(close);
            Button save = new Button { Content = Localizer.T("act.save"), Background = theme.AccentButton, Foreground = theme.AccentText };
            save.Tip(Localizer.T("settings.save.tip"));
            save.Click += (sender, args) => Save();
            buttons.Children.Add(save);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            StackPanel form = new StackPanel { Spacing = 8, Margin = new Thickness(0, 14, 14, 0) };

            form.Children.Add(Section(Localizer.T("settings.sec.general")));
            form.Children.Add(LabeledRow(Localizer.T("settings.theme"), _ThemeSelector, Localizer.T("settings.theme.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.approval"), _ApprovalPolicy, Localizer.T("settings.approval.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.maxIter"), _MaxIterations, Localizer.T("settings.maxIter.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.maxBudget"), _MaxTokenBudget, Localizer.T("settings.maxBudget.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.sysPrompt"), _SystemPromptPath, Localizer.T("settings.sysPrompt.tip")));

            form.Children.Add(Section(Localizer.T("settings.sec.context")));
            form.Children.Add(_AutoCompact.Tip(Localizer.T("settings.autoCompact.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.compactStrategy"), _CompactionStrategy, Localizer.T("settings.compactStrategy.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.preserveTurns"), _CompactionPreserveTurns, Localizer.T("settings.preserveTurns.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.warnThreshold"), _ContextWarningThreshold, Localizer.T("settings.warnThreshold.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.safetyMargin"), _ContextSafetyMargin, Localizer.T("settings.safetyMargin.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.tokenRatio"), _TokenEstimationRatio, Localizer.T("settings.tokenRatio.tip")));

            form.Children.Add(Section(Localizer.T("settings.sec.jobs")));
            form.Children.Add(LabeledRow(Localizer.T("settings.maxConcurrency"), _MaxConcurrency, Localizer.T("settings.maxConcurrency.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.enqueue"), _EnqueueBehavior, Localizer.T("settings.enqueue.tip")));
            form.Children.Add(_TaskPlanning.Tip(Localizer.T("settings.taskPlanning.tip")));
            form.Children.Add(_TaskParallelism.Tip(Localizer.T("settings.taskParallel.tip")));

            form.Children.Add(Section(Localizer.T("settings.sec.tools")));
            form.Children.Add(LabeledRow(Localizer.T("settings.toolTimeout"), _ToolTimeout, Localizer.T("settings.toolTimeout.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.processTimeout"), _ProcessTimeout, Localizer.T("settings.processTimeout.tip")));
            form.Children.Add(_IgnoreCertErrors.Tip(Localizer.T("settings.ignoreCert.tip")));
            form.Children.Add(_ShowBoundaryLines.Tip(Localizer.T("settings.boundaryLines.tip")));

            form.Children.Add(Section(Localizer.T("settings.sec.skills")));
            form.Children.Add(_SkillsEnabled.Tip(Localizer.T("settings.loadSkills.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.skillRefresh"), _SkillRefreshInterval, Localizer.T("settings.skillRefresh.tip")));
            form.Children.Add(LabeledRow(Localizer.T("settings.skillsDir"), _SkillsDirectory, Localizer.T("settings.skillsDir.tip")));

            form.Children.Add(Section(Localizer.T("settings.sec.telemetry")));
            form.Children.Add(_Telemetry.Tip(Localizer.T("settings.recordTelemetry.tip")));

            form.Children.Add(new TextBlock
            {
                Text = Localizer.T("settings.autoSafeNote"),
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
            _Settings.DefaultApprovalPolicy = ValueAt(ApprovalValues, _ApprovalPolicy.SelectedIndex, _Settings.DefaultApprovalPolicy);
            _Settings.CompactionStrategy = ValueAt(CompactionValues, _CompactionStrategy.SelectedIndex, _Settings.CompactionStrategy);
            _Settings.DefaultEnqueueBehavior = ValueAt(EnqueueValues, _EnqueueBehavior.SelectedIndex, _Settings.DefaultEnqueueBehavior);

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
                _Status.Text = Localizer.T("settings.saved");
                SyncBack();
            }
            catch (Exception ex)
            {
                _Status.Foreground = new SolidColorBrush(Color.Parse("#c0392b"));
                _Status.Text = Localizer.T("settings.saveFailed") + " " + ex.Message;
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

        // Map a stored canonical value to its index in the option list (for preselecting a localized combo).
        private static int IndexOfValue(string[] values, string? current, int fallback)
        {
            string candidate = (current ?? string.Empty).Trim().ToLowerInvariant();
            for (int i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i], candidate, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return fallback;
        }

        // Map a selected index back to its canonical value (for saving), falling back when out of range.
        private static string ValueAt(string[] values, int index, string fallback)
        {
            return index >= 0 && index < values.Length ? values[index] : fallback;
        }
    }
}
