namespace Mux.Cli.App
{
    using System;
    using System.Globalization;
    using Mux.Core.Models;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A single modal form for editing the global <see cref="MuxSettings"/>. It hosts a <see cref="Form"/>
    /// with fields for every scalar/boolean setting — the agent-loop run limits (max iterations, optional
    /// token budget, concurrency), approval/queue defaults, tool and process timeouts, context and
    /// compaction tuning, the skills and task-planning toggles, and the network/UI flags. Navigate with
    /// Tab/↑↓; Enter validates and returns the mutated <see cref="MuxSettings"/> (via
    /// <see cref="Modal.Completion"/>); Escape cancels (returns null and leaves the settings untouched).
    /// Nested settings the caller manages elsewhere — <see cref="MuxSettings.ExternalSearch"/> and
    /// <see cref="MuxSettings.SystemPromptPath"/> — are not surfaced here and are carried over unchanged
    /// because the same instance is edited in place. Per-model overrides (for example an endpoint's own
    /// max-agent-iterations) live in the endpoint editor, not here.
    /// </summary>
    public sealed class SettingsFormModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;

        // Wide enough to hold the longest field label ("Context window safety margin (%)") and the footer
        // hint without truncation on a normal-width terminal; it still shrinks to fit narrower screens.
        private const int ContentWidth = 64;

        private static readonly string[] _ApprovalPolicies = { "ask", "auto", "deny" };
        private static readonly string[] _EnqueueBehaviors = { "ask", "run_now", "queue_after", "add_to_focused" };
        private static readonly string[] _CompactionStrategies = { "summary", "trim" };

        // Per-field widget row heights, in the same order fields are added to the form below. Used to
        // compute the focused field's vertical offset so the modal can scroll a form taller than the box.
        // Text fields and checkboxes occupy one row; a radio group occupies one row per option.
        private static readonly int[] _WidgetRows =
        {
            1,                                // Max agent iterations
            1,                                // Max token budget
            1,                                // Max concurrency
            _ApprovalPolicies.Length,         // Default approval policy
            _EnqueueBehaviors.Length,         // Default enqueue behavior
            1,                                // Tool timeout
            1,                                // Process timeout
            1,                                // Context window safety margin
            1,                                // Token estimation ratio
            1,                                // Auto-compact enabled
            1,                                // Context warning threshold
            _CompactionStrategies.Length,     // Compaction strategy
            1,                                // Compaction preserve turns
            1,                                // Skills enabled
            1,                                // Skill refresh interval
            1,                                // Skills directory
            1,                                // Task planning enabled
            1,                                // Task parallelism enabled
            1,                                // Ignore cert errors
            1,                                // Show boundary lines
        };

        private readonly string _Title;
        private readonly MuxSettings _Settings;
        private readonly Form _Form;
        private readonly TextField _MaxAgentIterations;
        private readonly TextField _MaxTokenBudget;
        private readonly TextField _MaxConcurrency;
        private readonly RadioGroup _ApprovalPolicy;
        private readonly RadioGroup _EnqueueBehavior;
        private readonly TextField _ToolTimeout;
        private readonly TextField _ProcessTimeout;
        private readonly TextField _ContextSafetyMargin;
        private readonly TextField _TokenEstimationRatio;
        private readonly Checkbox _AutoCompact;
        private readonly TextField _ContextWarningThreshold;
        private readonly RadioGroup _CompactionStrategy;
        private readonly TextField _CompactionPreserveTurns;
        private readonly Checkbox _SkillsEnabled;
        private readonly TextField _SkillRefreshInterval;
        private readonly TextField _SkillsDirectory;
        private readonly Checkbox _TaskPlanning;
        private readonly Checkbox _TaskParallelism;
        private readonly Checkbox _IgnoreCertErrors;
        private readonly Checkbox _ShowBoundaryLines;
        private string _Error = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsFormModal"/> class.
        /// </summary>
        /// <param name="title">The box title (for example "Settings").</param>
        /// <param name="settings">The settings instance to edit in place. Must not be null.</param>
        public SettingsFormModal(string title, MuxSettings settings)
        {
            _Title = title ?? "Settings";
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));

            _MaxAgentIterations = new TextField { Value = _Settings.MaxAgentIterations.ToString(CultureInfo.InvariantCulture) };
            _MaxTokenBudget = new TextField
            {
                Value = _Settings.MaxTokenBudget.HasValue
                    ? _Settings.MaxTokenBudget.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty
            };
            _MaxConcurrency = new TextField { Value = _Settings.MaxConcurrency.ToString(CultureInfo.InvariantCulture) };
            _ApprovalPolicy = new RadioGroup(_ApprovalPolicies);
            SelectRadio(_ApprovalPolicy, _ApprovalPolicies, _Settings.DefaultApprovalPolicy);
            _EnqueueBehavior = new RadioGroup(_EnqueueBehaviors);
            SelectRadio(_EnqueueBehavior, _EnqueueBehaviors, _Settings.DefaultEnqueueBehavior);
            _ToolTimeout = new TextField { Value = _Settings.ToolTimeoutMs.ToString(CultureInfo.InvariantCulture) };
            _ProcessTimeout = new TextField { Value = _Settings.ProcessTimeoutMs.ToString(CultureInfo.InvariantCulture) };
            _ContextSafetyMargin = new TextField { Value = _Settings.ContextWindowSafetyMarginPercent.ToString(CultureInfo.InvariantCulture) };
            _TokenEstimationRatio = new TextField { Value = _Settings.TokenEstimationRatio.ToString(CultureInfo.InvariantCulture) };
            _AutoCompact = new Checkbox("Auto-compact history when context is tight", _Settings.AutoCompactEnabled);
            _ContextWarningThreshold = new TextField { Value = _Settings.ContextWarningThresholdPercent.ToString(CultureInfo.InvariantCulture) };
            _CompactionStrategy = new RadioGroup(_CompactionStrategies);
            SelectRadio(_CompactionStrategy, _CompactionStrategies, _Settings.CompactionStrategy);
            _CompactionPreserveTurns = new TextField { Value = _Settings.CompactionPreserveTurns.ToString(CultureInfo.InvariantCulture) };
            _SkillsEnabled = new Checkbox("Load user skills", _Settings.SkillsEnabled);
            _SkillRefreshInterval = new TextField { Value = _Settings.SkillRefreshIntervalSeconds.ToString(CultureInfo.InvariantCulture) };
            _SkillsDirectory = new TextField { Value = _Settings.SkillsDirectory ?? string.Empty };
            _TaskPlanning = new Checkbox("Allow task planning", _Settings.TaskPlanningEnabled);
            _TaskParallelism = new Checkbox("Allow task parallelism", _Settings.TaskParallelismEnabled);
            _IgnoreCertErrors = new Checkbox("Ignore TLS certificate errors", _Settings.IgnoreCertErrors);
            _ShowBoundaryLines = new Checkbox("Show boundary lines", _Settings.ShowBoundaryLines);

            _Form = new Form();
            _Form.Add("Max agent iterations", _MaxAgentIterations, () => ValidateInt(_MaxAgentIterations.Value, "Max agent iterations", 1, 100));
            _Form.Add("Max token budget (blank = off)", _MaxTokenBudget, () => ValidateOptionalInt(_MaxTokenBudget.Value, "Max token budget", 1, int.MaxValue));
            _Form.Add("Max concurrency", _MaxConcurrency, () => ValidateInt(_MaxConcurrency.Value, "Max concurrency", 1, 32));
            _Form.Add("Default approval policy", _ApprovalPolicy);
            _Form.Add("Default enqueue behavior", _EnqueueBehavior);
            _Form.Add("Tool timeout (ms)", _ToolTimeout, () => ValidateInt(_ToolTimeout.Value, "Tool timeout", 1000, 300000));
            _Form.Add("Process timeout (ms)", _ProcessTimeout, () => ValidateInt(_ProcessTimeout.Value, "Process timeout", 1000, 600000));
            _Form.Add("Context window safety margin (%)", _ContextSafetyMargin, () => ValidateInt(_ContextSafetyMargin.Value, "Context window safety margin", 5, 50));
            _Form.Add("Token estimation ratio", _TokenEstimationRatio, () => ValidateDouble(_TokenEstimationRatio.Value, "Token estimation ratio", 2.0, 6.0));
            _Form.Add("Auto-compact", _AutoCompact);
            _Form.Add("Context warning threshold (%)", _ContextWarningThreshold, () => ValidateInt(_ContextWarningThreshold.Value, "Context warning threshold", 50, 95));
            _Form.Add("Compaction strategy", _CompactionStrategy);
            _Form.Add("Compaction preserve turns", _CompactionPreserveTurns, () => ValidateInt(_CompactionPreserveTurns.Value, "Compaction preserve turns", 1, 10));
            _Form.Add("Skills", _SkillsEnabled);
            _Form.Add("Skill refresh interval (s)", _SkillRefreshInterval, () => ValidateInt(_SkillRefreshInterval.Value, "Skill refresh interval", 5, int.MaxValue));
            _Form.Add("Skills directory (blank = default)", _SkillsDirectory);
            _Form.Add("Task planning", _TaskPlanning);
            _Form.Add("Task parallelism", _TaskParallelism);
            _Form.Add("Ignore cert errors", _IgnoreCertErrors);
            _Form.Add("Boundary lines", _ShowBoundaryLines);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                Close(null);
                return true;
            }

            if (key.Code == KeyCode.Enter && key.Modifiers == KeyModifiers.None)
            {
                Submit();
                return true;
            }

            return _Form.HandleKey(key);
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int formHeight = EstimateFormHeight();
            int hintRows = 2; // blank + hint/error
            int contentWidth = Math.Max(8, Math.Min(ContentWidth, screenWidth - 2 - (2 * PadX)));
            int boxWidth = Math.Min(screenWidth, contentWidth + 2 + (2 * PadX));

            // Cap the box to the screen; when the form is taller than the visible area the content is
            // scrolled to keep the focused field in view (see the scroll offset below).
            int maxContentHeight = Math.Max(1, screenHeight - hintRows - 2 - (2 * PadY));
            int visibleFormHeight = Math.Min(formHeight, maxContentHeight);
            int boxHeight = Math.Min(screenHeight, visibleFormHeight + hintRows + 2 + (2 * PadY));

            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(8)), _Title);

            int contentX = boxX + 1 + PadX;
            int firstRow = boxY + 1 + PadY;
            int usableHeight = boxHeight - 2 - (2 * PadY) - hintRows;
            if (usableHeight < 1)
            {
                return;
            }

            // The Form only renders its field widgets into a BufferSurface, so render the full form into a
            // buffer tall enough to hold every field, then copy a vertical window of it into the modal box.
            // The window is scrolled so the focused field is always visible even when the form overflows.
            int scrollY = ComputeScrollOffset(formHeight, usableHeight);
            CellBuffer buffer = new CellBuffer(contentWidth, Math.Max(usableHeight, formHeight));
            _Form.Render(new BufferSurface(buffer));
            for (int y = 0; y < usableHeight; y++)
            {
                int sourceY = y + scrollY;
                for (int x = 0; x < contentWidth; x++)
                {
                    surface.Set(contentX + x, firstRow + y, buffer.Get(x, sourceY));
                }
            }

            int hintRow = boxY + boxHeight - 2;
            if (_Error.Length > 0)
            {
                surface.DrawText(contentX, hintRow, Trim(_Error, contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(9)));
            }
            else
            {
                surface.DrawText(contentX, hintRow, Trim("Tab/↑↓ move · Space toggle · Enter save · Esc cancel", contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(8)));
            }
        }

        #endregion

        #region Private-Methods

        private void Submit()
        {
            string? error = _Form.Validate();
            if (error != null)
            {
                _Error = error;
                return;
            }

            // Apply onto the passed-in instance; the property setters clamp/normalize each value, so the
            // validated form values land within their documented ranges. Nested settings not surfaced here
            // (ExternalSearch, SystemPromptPath) are preserved because the same instance is edited in place.
            _Settings.MaxAgentIterations = ParseInt(_MaxAgentIterations.Value);
            _Settings.MaxTokenBudget = ParseOptionalIntValue(_MaxTokenBudget.Value);
            _Settings.MaxConcurrency = ParseInt(_MaxConcurrency.Value);
            _Settings.DefaultApprovalPolicy = _ApprovalPolicy.SelectedOption;
            _Settings.DefaultEnqueueBehavior = _EnqueueBehavior.SelectedOption;
            _Settings.ToolTimeoutMs = ParseInt(_ToolTimeout.Value);
            _Settings.ProcessTimeoutMs = ParseInt(_ProcessTimeout.Value);
            _Settings.ContextWindowSafetyMarginPercent = ParseInt(_ContextSafetyMargin.Value);
            _Settings.TokenEstimationRatio = ParseDouble(_TokenEstimationRatio.Value);
            _Settings.AutoCompactEnabled = _AutoCompact.Checked;
            _Settings.ContextWarningThresholdPercent = ParseInt(_ContextWarningThreshold.Value);
            _Settings.CompactionStrategy = _CompactionStrategy.SelectedOption;
            _Settings.CompactionPreserveTurns = ParseInt(_CompactionPreserveTurns.Value);
            _Settings.SkillsEnabled = _SkillsEnabled.Checked;
            _Settings.SkillRefreshIntervalSeconds = ParseInt(_SkillRefreshInterval.Value);
            _Settings.SkillsDirectory = _SkillsDirectory.Value;
            _Settings.TaskPlanningEnabled = _TaskPlanning.Checked;
            _Settings.TaskParallelismEnabled = _TaskParallelism.Checked;
            _Settings.IgnoreCertErrors = _IgnoreCertErrors.Checked;
            _Settings.ShowBoundaryLines = _ShowBoundaryLines.Checked;

            Close(_Settings);
        }

        private int ComputeScrollOffset(int formHeight, int usableHeight)
        {
            if (formHeight <= usableHeight)
            {
                return 0;
            }

            int focused = _Form.FocusedIndex;
            if (focused < 0 || focused >= _WidgetRows.Length)
            {
                return 0;
            }

            // Reconstruct the focused field's vertical span using the same per-field layout the Form uses:
            // one label row, the widget rows, then a trailing spacer row.
            int top = 0;
            for (int i = 0; i < focused; i++)
            {
                top += 1 + _WidgetRows[i] + 1;
            }

            int blockHeight = 1 + _WidgetRows[focused] + 1;

            int scrollY = 0;
            if (top < scrollY)
            {
                scrollY = top;
            }

            if (top + blockHeight > scrollY + usableHeight)
            {
                scrollY = top + blockHeight - usableHeight;
            }

            int maxScroll = Math.Max(0, formHeight - usableHeight);
            return Math.Clamp(scrollY, 0, maxScroll);
        }

        private int EstimateFormHeight()
        {
            // Each field renders a label row plus its widget height plus a spacing row.
            int height = 0;
            for (int i = 0; i < _WidgetRows.Length; i++)
            {
                height += 1 + _WidgetRows[i] + 1;
            }

            return height;
        }

        private static void SelectRadio(RadioGroup group, string[] options, string current)
        {
            string candidate = (current ?? string.Empty).Trim();
            int target = 0;
            for (int i = 0; i < options.Length; i++)
            {
                if (string.Equals(options[i], candidate, StringComparison.OrdinalIgnoreCase))
                {
                    target = i;
                    break;
                }
            }

            for (int i = 0; i < target; i++)
            {
                group.HandleKey(KeyEvent.Special(KeyCode.Down));
            }
        }

        private static string? ValidateInt(string value, string label, int min, int max)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return label + " is required.";
            }

            if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return label + " must be a whole number.";
            }

            if (parsed < min || parsed > max)
            {
                return max == int.MaxValue
                    ? $"{label} must be at least {min}."
                    : $"{label} must be between {min} and {max}.";
            }

            return null;
        }

        private static string? ValidateOptionalInt(string value, string label, int min, int max)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            return ValidateInt(trimmed, label, min, max);
        }

        private static string? ValidateDouble(string value, string label, double min, double max)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return label + " is required.";
            }

            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                return label + " must be a number.";
            }

            if (parsed < min || parsed > max)
            {
                return $"{label} must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.";
            }

            return null;
        }

        private static int ParseInt(string value)
        {
            return int.Parse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string value)
        {
            return double.Parse((value ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static int? ParseOptionalIntValue(string value)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            return int.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static string Trim(string text, int width)
        {
            if (width <= 0)
            {
                return string.Empty;
            }

            return text.Length <= width ? text : text.Substring(0, Math.Max(0, width - 1)) + "…";
        }

        #endregion
    }
}
