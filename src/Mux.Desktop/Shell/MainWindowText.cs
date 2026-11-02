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
    using Mux.Core.Prompting;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using Mux.Core.Tasks;
    using Mux.Core.Telemetry;
    using Mux.Core.Tools;
    using Mux.Core.Utility;
    using Mux.Core.Conversation;
    using Mux.Desktop.Conversation;
    using Mux.Desktop.I18n;
    using Mux.Desktop.Services;
    using Mux.Desktop.ViewModels;
    using Mux.Desktop.Views;

    /// <summary>
    /// Stateless text and label helpers for <see cref="MainWindow"/>: thread title/format helpers,
    /// checkpoint labels, task glyphs, and timing strings. Pure functions only; no window state.
    /// </summary>
    internal static class MainWindowText
    {
        public static string ViewItemHeader(string label, bool on)
        {
            return (on ? "✓   " : "      ") + label;
        }

        public static bool ResolveSystemDark()
        {
            return Application.Current == null || Application.Current.ActualThemeVariant == ThemeVariant.Dark;
        }

        public static string CheckpointLabel(string prompt)
        {
            string label = PromptText.Preview(prompt, 60);
            return label.Length == 0 ? "turn" : label;
        }

        public static int ConversationCharCount(IReadOnlyList<ConversationMessage>? history)
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

        public static bool TryStripRemovePrefix(string argument, out string remainder)
        {
            foreach (string prefix in new[] { "rm ", "remove ", "delete ", "-" })
            {
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    remainder = argument.Substring(prefix.Length).Trim();
                    return true;
                }
            }

            remainder = argument;
            return false;
        }

        public static string DescribeLabels(ThreadSummary s)
        {
            return s.Labels.Count == 0 ? "(none)" : string.Join(", ", s.Labels);
        }

        public static string DescribeTags(ThreadSummary s)
        {
            if (s.Tags.Count == 0) return "(none)";
            System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
            foreach (Mux.Core.Sessions.SessionTag tag in s.Tags) parts.Add(tag.Key + ": " + tag.Value);
            return string.Join(", ", parts);
        }

        public static string TaskGlyph(AgentTaskStatusEnum status)
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

        public static string FormatMs(long milliseconds)
        {
            if (milliseconds < 1000)
            {
                return milliseconds + " ms";
            }

            return (milliseconds / 1000.0).ToString("0.0") + " s";
        }

        public static string SafeFileName(string title)
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

        public static string FirstUserMessage(IReadOnlyList<ConversationMessage> history)
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
