namespace Mux.Desktop.Shell
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.Documents;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// A lightweight Markdown-to-Avalonia renderer for assistant messages. Handles the constructs language
    /// models commonly emit: fenced code blocks (with correct handling of longer outer fences that contain
    /// inner fences), headings, unordered/ordered lists, blockquotes, horizontal rules, paragraphs, inline
    /// bold/italic/code/links, and the <c>&lt;details&gt;/&lt;summary&gt;</c> HTML element (rendered as a
    /// collapsible section with its inner Markdown). Dependency-free and theme-aware.
    /// </summary>
    public static class MarkdownRenderer
    {
        private static readonly FontFamily Mono = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace");

        /// <summary>
        /// Render Markdown text into a stack of Avalonia controls.
        /// </summary>
        /// <param name="markdown">The Markdown source. Null is treated as empty.</param>
        /// <param name="theme">The active palette. Required.</param>
        /// <returns>A control containing the rendered blocks.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="theme"/> is null.</exception>
        public static Control Render(string? markdown, AppTheme theme)
        {
            ArgumentNullException.ThrowIfNull(theme);

            StackPanel panel = new StackPanel { Spacing = 8 };
            string[] lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            int i = 0;
            while (i < lines.Length)
            {
                int before = i;
                try
                {
                    i = RenderBlock(panel, lines, i, theme);
                }
                catch (Exception)
                {
                    // A single malformed block must never drop the whole answer to raw text: degrade just
                    // this line to selectable plain text and keep rendering the rest as Markdown.
                    panel.Children.Add(new SelectableTextBlock { Text = lines[before], TextWrapping = TextWrapping.Wrap, Foreground = theme.Text });
                    i = before + 1;
                }

                if (i <= before)
                {
                    i = before + 1;
                }
            }

            return panel;
        }

        private static int RenderBlock(StackPanel panel, string[] lines, int i, AppTheme theme)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();

            if (FenceLength(trimmed) > 0)
            {
                return AppendCodeBlock(panel, lines, i, theme);
            }

            if (trimmed.StartsWith("<details", StringComparison.OrdinalIgnoreCase))
            {
                return AppendDetails(panel, lines, i, theme);
            }

            if (trimmed.Length == 0)
            {
                return i + 1;
            }

            if (IsHorizontalRule(trimmed))
            {
                panel.Children.Add(new Border { Height = 1, Background = theme.Border, Margin = new Thickness(0, 4, 0, 4) });
                return i + 1;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                panel.Children.Add(BuildHeading(trimmed, theme));
                return i + 1;
            }

            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                return AppendBlockquote(panel, lines, i, theme);
            }

            if (IsListItem(trimmed))
            {
                return AppendList(panel, lines, i, theme);
            }

            return AppendParagraph(panel, lines, i, theme);
        }

        private static int AppendCodeBlock(StackPanel panel, string[] lines, int start, AppTheme theme)
        {
            // The opening fence length governs closing: only a fence of at least this many backticks (with
            // nothing but whitespace after) closes it. This keeps inner ``` fences literal inside a longer
            // ```` outer fence, so "markdown in a code block" renders as one monospace block.
            string fenceLine = lines[start].TrimStart();
            int fenceLen = FenceLength(fenceLine);
            string language = fenceLine.Substring(fenceLen).Trim();
            int langBreak = language.IndexOfAny(new[] { ' ', '\t' });
            if (langBreak >= 0)
            {
                language = language.Substring(0, langBreak);
            }

            StringBuilder code = new StringBuilder();
            int i = start + 1;
            while (i < lines.Length)
            {
                string t = lines[i].TrimStart();
                if (IsClosingFence(t, fenceLen))
                {
                    i++;
                    break;
                }

                code.Append(lines[i]).Append('\n');
                i++;
            }

            string codeText = code.ToString().TrimEnd('\n');
            SelectableTextBlock codeBox = new SelectableTextBlock
            {
                FontFamily = Mono,
                FontSize = 12.5,
                Foreground = theme.Text,
                TextWrapping = TextWrapping.NoWrap
            };

            foreach (Run run in SyntaxHighlighter.Highlight(codeText, language, theme))
            {
                codeBox.Inlines?.Add(run);
            }

            ScrollViewer codeScroll = new ScrollViewer
            {
                Content = codeBox,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };

            panel.Children.Add(new Border
            {
                Background = theme.SurfaceAlt,
                BorderBrush = theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Child = codeScroll
            });

            return i;
        }

        private static int AppendDetails(StackPanel panel, string[] lines, int start, AppTheme theme)
        {
            List<string> block = new List<string>();
            int depth = 0;
            int i = start;
            while (i < lines.Length)
            {
                block.Add(lines[i]);
                depth += CountCaseInsensitive(lines[i], "<details");
                depth -= CountCaseInsensitive(lines[i], "</details>");
                i++;
                if (depth <= 0)
                {
                    break;
                }
            }

            string joined = string.Join("\n", block);

            Match summaryMatch = Regex.Match(joined, "<summary>(.*?)</summary>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            string summary = summaryMatch.Success ? StripTags(summaryMatch.Groups[1].Value).Trim() : "Details";
            if (summary.Length == 0)
            {
                summary = "Details";
            }

            string inner = Regex.Replace(joined, "<summary>.*?</summary>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            inner = Regex.Replace(inner, "</?details[^>]*>", string.Empty, RegexOptions.IgnoreCase).Trim();

            CollapsibleSection section = new CollapsibleSection(summary, theme);
            section.Body.Children.Add(Render(inner, theme));
            panel.Children.Add(section.Root);
            return i;
        }

        private static int AppendBlockquote(StackPanel panel, string[] lines, int start, AppTheme theme)
        {
            StringBuilder text = new StringBuilder();
            int i = start;
            while (i < lines.Length && lines[i].TrimStart().StartsWith(">", StringComparison.Ordinal))
            {
                string content = lines[i].TrimStart();
                content = content.Length > 1 ? content.Substring(1).TrimStart() : string.Empty;
                if (text.Length > 0)
                {
                    text.Append(' ');
                }

                text.Append(content);
                i++;
            }

            TextBlock body = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = theme.Muted };
            AppendInlines(body.Inlines!, text.ToString(), theme);

            panel.Children.Add(new Border
            {
                BorderBrush = theme.Border,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(10, 2, 0, 2),
                Child = body
            });

            return i;
        }

        private static int AppendList(StackPanel panel, string[] lines, int start, AppTheme theme)
        {
            StackPanel list = new StackPanel { Spacing = 3, Margin = new Thickness(4, 0, 0, 0) };
            int i = start;
            int ordinal = 1;

            while (i < lines.Length)
            {
                string trimmed = lines[i].TrimStart();
                if (!IsListItem(trimmed))
                {
                    break;
                }

                bool ordered = IsOrderedItem(trimmed);
                string content = StripListMarker(trimmed);
                string marker = ordered ? ordinal + "." : "•";
                ordinal = ordered ? ordinal + 1 : ordinal;

                DockPanel row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                TextBlock bullet = new TextBlock { Text = marker + "  ", Foreground = theme.Muted, VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(bullet, Dock.Left);
                row.Children.Add(bullet);

                SelectableTextBlock body = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Foreground = theme.Text };
                AppendInlines(body.Inlines!, content, theme);
                row.Children.Add(body);

                list.Children.Add(row);
                i++;
            }

            panel.Children.Add(list);
            return i;
        }

        private static int AppendParagraph(StackPanel panel, string[] lines, int start, AppTheme theme)
        {
            StringBuilder text = new StringBuilder();
            int i = start;
            while (i < lines.Length)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (trimmed.Length == 0
                    || FenceLength(trimmed) > 0
                    || trimmed.StartsWith("#", StringComparison.Ordinal)
                    || trimmed.StartsWith(">", StringComparison.Ordinal)
                    || trimmed.StartsWith("<details", StringComparison.OrdinalIgnoreCase)
                    || IsListItem(trimmed)
                    || IsHorizontalRule(trimmed))
                {
                    break;
                }

                if (text.Length > 0)
                {
                    text.Append(' ');
                }

                text.Append(line.Trim());
                i++;
            }

            SelectableTextBlock paragraph = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Foreground = theme.Text };
            AppendInlines(paragraph.Inlines!, text.ToString(), theme);
            panel.Children.Add(paragraph);
            return i;
        }

        private static TextBlock BuildHeading(string trimmed, AppTheme theme)
        {
            int level = 0;
            while (level < trimmed.Length && trimmed[level] == '#')
            {
                level++;
            }

            string text = trimmed.Substring(level).Trim();
            double size = level <= 1 ? 19 : level == 2 ? 16.5 : 14.5;

            SelectableTextBlock heading = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Foreground = theme.Text, FontWeight = FontWeight.SemiBold, FontSize = size, Margin = new Thickness(0, 4, 0, 0) };
            AppendInlines(heading.Inlines!, text, theme);
            return heading;
        }

        private static void AppendInlines(InlineCollection sink, string rawText, AppTheme theme)
        {
            string text = Regex.Replace(rawText, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            StringBuilder run = new StringBuilder();
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];

                if (c == '`')
                {
                    int end = text.IndexOf('`', i + 1);
                    if (end > i)
                    {
                        Flush(sink, run);
                        sink.Add(new Run(text.Substring(i + 1, end - i - 1)) { FontFamily = Mono, Foreground = theme.Accent });
                        i = end + 1;
                        continue;
                    }
                }

                if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i)
                    {
                        Flush(sink, run);
                        Bold bold = new Bold();
                        bold.Inlines.Add(new Run(text.Substring(i + 2, end - i - 2)));
                        sink.Add(bold);
                        i = end + 2;
                        continue;
                    }
                }

                if ((c == '*' || c == '_') && i + 1 < text.Length && text[i + 1] != ' ')
                {
                    int end = text.IndexOf(c, i + 1);
                    if (end > i)
                    {
                        Flush(sink, run);
                        Italic italic = new Italic();
                        italic.Inlines.Add(new Run(text.Substring(i + 1, end - i - 1)));
                        sink.Add(italic);
                        i = end + 1;
                        continue;
                    }
                }

                if (c == '[')
                {
                    int close = text.IndexOf(']', i + 1);
                    if (close > i && close + 1 < text.Length && text[close + 1] == '(')
                    {
                        int paren = text.IndexOf(')', close + 2);
                        if (paren > close)
                        {
                            Flush(sink, run);
                            string label = text.Substring(i + 1, close - i - 1);
                            sink.Add(new Run(label) { Foreground = theme.Accent, TextDecorations = TextDecorations.Underline });
                            i = paren + 1;
                            continue;
                        }
                    }
                }

                run.Append(c);
                i++;
            }

            Flush(sink, run);
        }

        private static void Flush(InlineCollection sink, StringBuilder run)
        {
            if (run.Length > 0)
            {
                sink.Add(new Run(run.ToString()));
                run.Clear();
            }
        }

        private static int FenceLength(string trimmed)
        {
            int n = 0;
            while (n < trimmed.Length && trimmed[n] == '`')
            {
                n++;
            }

            return n >= 3 ? n : 0;
        }

        private static bool IsClosingFence(string trimmed, int minLength)
        {
            int n = 0;
            while (n < trimmed.Length && trimmed[n] == '`')
            {
                n++;
            }

            if (n < minLength)
            {
                return false;
            }

            for (int k = n; k < trimmed.Length; k++)
            {
                if (!char.IsWhiteSpace(trimmed[k]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int CountCaseInsensitive(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static string StripTags(string value)
        {
            return Regex.Replace(value, "<[^>]+>", string.Empty);
        }

        private static bool IsHorizontalRule(string trimmed)
        {
            return trimmed == "---" || trimmed == "***" || trimmed == "___";
        }

        private static bool IsListItem(string trimmed)
        {
            return IsUnorderedItem(trimmed) || IsOrderedItem(trimmed);
        }

        private static bool IsUnorderedItem(string trimmed)
        {
            return trimmed.Length >= 2 && (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+') && trimmed[1] == ' ';
        }

        private static bool IsOrderedItem(string trimmed)
        {
            int digits = 0;
            while (digits < trimmed.Length && char.IsDigit(trimmed[digits]))
            {
                digits++;
            }

            return digits > 0 && digits + 1 < trimmed.Length && trimmed[digits] == '.' && trimmed[digits + 1] == ' ';
        }

        private static string StripListMarker(string trimmed)
        {
            if (IsUnorderedItem(trimmed))
            {
                return trimmed.Substring(2).Trim();
            }

            int index = trimmed.IndexOf('.');
            return index >= 0 && index + 1 < trimmed.Length ? trimmed.Substring(index + 1).Trim() : trimmed;
        }
    }
}
