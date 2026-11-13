namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Conversation;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Prompting;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using Mux.Core.Tools;
    using Mux.Core.Utility;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Layout;
    using TUIKit.Modals;
    using TUIKit.Terminal;
    using TUIKit.Testing;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    /// <summary>
    /// Stateless label, formatting, and text-math helpers for the TUI: effort/theme display names,
    /// prompt previews, caret word math, truncation, and small session/endpoint description strings.
    /// Extracted from <see cref="MuxTuiApp"/> so the application class keeps only interaction logic.
    /// </summary>
    internal static class TuiPresentation
    {
        public static string EffortLabelFor(EndpointConfig endpoint)
        {
            ReasoningLevelEnum? level = endpoint.ReasoningEffort?.Level;
            return level.HasValue ? ReasoningLevelEnumConverter.ToWire(level.Value) : "off";
        }

        public static int EffortIndexFor(ReasoningLevelEnum? level)
        {
            switch (level)
            {
                case ReasoningLevelEnum.None: return 1;
                case ReasoningLevelEnum.Minimal: return 2;
                case ReasoningLevelEnum.Low: return 3;
                case ReasoningLevelEnum.Medium: return 4;
                case ReasoningLevelEnum.High: return 5;
                case ReasoningLevelEnum.XHigh: return 6;
                case ReasoningLevelEnum.Max: return 7;
                default: return 0;
            }
        }

        public static ReasoningLevelEnum? LevelForEffortIndex(int index)
        {
            switch (index)
            {
                case 1: return ReasoningLevelEnum.None;
                case 2: return ReasoningLevelEnum.Minimal;
                case 3: return ReasoningLevelEnum.Low;
                case 4: return ReasoningLevelEnum.Medium;
                case 5: return ReasoningLevelEnum.High;
                case 6: return ReasoningLevelEnum.XHigh;
                case 7: return ReasoningLevelEnum.Max;
                default: return null;
            }
        }

        public static string EffortDisplayName(ReasoningLevelEnum? level)
        {
            switch (level)
            {
                case ReasoningLevelEnum.Minimal: return "Minimal";
                case ReasoningLevelEnum.Low: return "Low";
                case ReasoningLevelEnum.Medium: return "Medium";
                case ReasoningLevelEnum.High: return "High";
                case ReasoningLevelEnum.XHigh: return "Extra high";
                case ReasoningLevelEnum.Max: return "Max";
                case ReasoningLevelEnum.None: return "None";
                default: return "Off";
            }
        }

        public static int ThemeIndexForName(string? name)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "dark":
                    return 1;
                case "light":
                    return 2;
                case "highcontrast":
                    return 3;
                default:
                    return 0;
            }
        }

        public static string ThemeNameForIndex(int index)
        {
            switch (index)
            {
                case 1:
                    return "dark";
                case 2:
                    return "light";
                case 3:
                    return "highcontrast";
                default:
                    return "mux";
            }
        }

        public static string ThemeLabel(Theme theme)
        {
            string name = theme.Name ?? string.Empty;
            if (name.Length == 0)
            {
                return "theme";
            }

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        public static string ScopeLabel(Mux.Core.Memory.MemoryScopeEnum scope)
        {
            return scope == Mux.Core.Memory.MemoryScopeEnum.Global ? "global" : "project";
        }

        public static string PromptPreview(string prompt)
        {
            return Mux.Core.Prompting.PromptText.Preview(prompt, 60);
        }

        public static string[] ShuffledThinkingPhrases()
        {
            List<string> phrases = new List<string>(ThinkingPhrases.All);
            for (int i = phrases.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                string swap = phrases[i];
                phrases[i] = phrases[j];
                phrases[j] = swap;
            }

            return phrases.ToArray();
        }

        public static int WordStartBefore(string text, int offset)
        {
            int i = offset;
            while (i > 0 && char.IsWhiteSpace(text[i - 1]))
            {
                i--;
            }

            while (i > 0 && !char.IsWhiteSpace(text[i - 1]))
            {
                i--;
            }

            return i;
        }

        public static int WordEndAfter(string text, int offset)
        {
            int i = offset;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            while (i < text.Length && !char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            return i;
        }

        public static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength) + "…";
        }

        public static string SanitizeFileStem(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
            foreach (char c in value.Trim())
            {
                bool bad = c == ' ';
                foreach (char inv in invalid)
                {
                    if (c == inv)
                    {
                        bad = true;
                        break;
                    }
                }

                builder.Append(bad ? '-' : c);
            }

            return builder.ToString();
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

        public static string DescribeLabels(Mux.Core.Sessions.SessionInfo info)
        {
            return info.Labels.Count == 0 ? "Labels: (none)" : "Labels: " + string.Join(", ", info.Labels);
        }

        public static string DescribeTags(Mux.Core.Sessions.SessionInfo info)
        {
            if (info.Tags.Count == 0) return "Tags: (none)";
            List<string> parts = new List<string>();
            foreach (Mux.Core.Sessions.SessionTag tag in info.Tags) parts.Add(tag.Key + ": " + tag.Value);
            return "Tags: " + string.Join(", ", parts);
        }

        public static string CenterMessage(string text)
        {
            const int minBoxContentWidth = 28;
            int pad = Math.Max(0, (minBoxContentWidth - text.Length) / 2);
            return new string(' ', pad) + text;
        }

        public static List<string> BuildValidationLines(EndpointConfig endpoint, ModelLoadResult result)
        {
            const int wrapWidth = 60;
            string label = string.IsNullOrWhiteSpace(endpoint.Model) ? endpoint.Name : endpoint.Model;
            List<string> lines = new List<string>();

            if (result.Success)
            {
                lines.Add($"✓ {label} is ready.");
                lines.Add(string.Empty);
                lines.Add("The endpoint responded successfully.");
            }
            else if (result.Reachable)
            {
                lines.Add($"⚠ {endpoint.Name} is reachable, but the validation");
                lines.Add("   request did not succeed:");
                lines.Add(string.Empty);
                lines.AddRange(WrapText(result.Error ?? "unknown error", wrapWidth));
                lines.Add(string.Empty);
                lines.Add("Normal chats may still work — for example, a");
                lines.Add("reasoning model that answers slowly.");
            }
            else
            {
                lines.Add($"✗ {endpoint.Name} is unreachable:");
                lines.Add(string.Empty);
                lines.AddRange(WrapText(result.Error ?? "unknown error", wrapWidth));
            }

            return lines;
        }

        public static List<string> WrapText(string text, int width)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrEmpty(text) || width <= 0)
            {
                if (!string.IsNullOrEmpty(text))
                {
                    lines.Add(text);
                }

                return lines;
            }

            string current = string.Empty;
            foreach (string word in text.Split(' '))
            {
                if (current.Length == 0)
                {
                    current = word;
                }
                else if (current.Length + 1 + word.Length <= width)
                {
                    current += " " + word;
                }
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }

            if (current.Length > 0)
            {
                lines.Add(current);
            }

            return lines;
        }

        public static string UniqueEndpointName(string model, HashSet<string> taken)
        {
            string baseName = string.IsNullOrWhiteSpace(model) ? "ollama-model" : model.Trim();
            if (!taken.Contains(baseName))
            {
                return baseName;
            }

            for (int suffix = 2; ; suffix++)
            {
                string candidate = $"{baseName} ({suffix})";
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        public static string? FirstLine(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            int newline = text.IndexOfAny(new[] { '\r', '\n' });
            return newline < 0 ? text.Trim() : text.Substring(0, newline).Trim();
        }

        public static string StripServerPrefix(string firstLine)
        {
            int colon = firstLine.LastIndexOf(": ", StringComparison.Ordinal);
            return colon >= 0 && colon + 2 < firstLine.Length ? firstLine.Substring(colon + 2) : firstLine;
        }

        public static SkillStatus StatusForRow(List<SkillStatus> statuses, List<SkillMenuAction> actions, int index)
        {
            int position = -1;
            for (int i = 0; i <= index && i < actions.Count; i++)
            {
                if (actions[i] == SkillMenuAction.Manage || actions[i] == SkillMenuAction.ProjectInfo)
                {
                    position++;
                }
            }

            return position >= 0 && position < statuses.Count ? statuses[position] : new SkillStatus();
        }

        /// <summary>
        /// Prepares a SKILL.md for the Markdown viewer: YAML frontmatter (which Markdown would read as a rule and a
        /// setext heading) is shown as a fenced <c>yaml</c> block above the body.
        /// </summary>
        /// <param name="content">The raw SKILL.md text. May be null.</param>
        /// <returns>Markdown ready to render. Never null.</returns>
        public static string SkillMarkdownForDisplay(string? content)
        {
            string text = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            if (!text.StartsWith("---\n", StringComparison.Ordinal)) return text;

            int close = text.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (close < 0) return text;

            int bodyStart = close + 4;
            if (bodyStart < text.Length && text[bodyStart] != '\n') return text; // "---" was not alone on its line
            string frontmatter = text.Substring(4, close - 4);
            string body = bodyStart < text.Length ? text.Substring(bodyStart).TrimStart('\n') : string.Empty;
            return "```yaml\n" + frontmatter + "\n```\n\n" + body;
        }

        public static bool IsCarriageReturn(KeyEvent key)
        {
            return key.Code == KeyCode.Character && (key.Rune == 13 || key.Rune == 10);
        }

        public static bool HistoryEquals(IReadOnlyList<ConversationMessage> left, IReadOnlyList<ConversationMessage> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i].Role != right[i].Role
                    || !string.Equals(left[i].Content ?? string.Empty, right[i].Content ?? string.Empty, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
