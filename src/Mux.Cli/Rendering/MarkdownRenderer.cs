namespace Mux.Cli.Rendering
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using Spectre.Console;

    /// <summary>
    /// Converts markdown text to Spectre.Console ANSI markup for rich terminal display.
    /// Handles headers, code blocks, inline formatting, lists, and blockquotes.
    /// </summary>
    public static class MarkdownRenderer
    {
        #region Public-Methods

        /// <summary>
        /// Converts a markdown string to Spectre.Console ANSI markup suitable for terminal display.
        /// Processes line by line, tracking code block state to avoid formatting inside fenced blocks.
        /// </summary>
        /// <param name="markdown">The markdown source text.</param>
        /// <returns>The rendered string containing Spectre.Console markup tags.</returns>
        public static string RenderToAnsi(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                return string.Empty;
            }

            string[] lines = markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            StringBuilder result = new StringBuilder();
            bool insideCodeBlock = false;
            string codeBlockLanguage = string.Empty;
            StringBuilder codeBuffer = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (!insideCodeBlock && line.TrimStart().StartsWith("```"))
                {
                    insideCodeBlock = true;
                    codeBlockLanguage = line.TrimStart().Substring(3).Trim();
                    codeBuffer.Clear();

                    string label = string.IsNullOrEmpty(codeBlockLanguage)
                        ? "[grey]┌──[/]"
                        : $"[grey]┌── {EscapeMarkup(codeBlockLanguage)}[/]";
                    result.AppendLine(label);
                    continue;
                }

                if (insideCodeBlock && line.TrimStart().StartsWith("```"))
                {
                    insideCodeBlock = false;

                    string codeContent = codeBuffer.ToString();
                    if (codeContent.EndsWith(Environment.NewLine))
                    {
                        codeContent = codeContent.Substring(0, codeContent.Length - Environment.NewLine.Length);
                    }
                    else if (codeContent.EndsWith("\n"))
                    {
                        codeContent = codeContent.Substring(0, codeContent.Length - 1);
                    }

                    string[] codeLines = codeContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    foreach (string codeLine in codeLines)
                    {
                        result.AppendLine($"[on grey15]{EscapeMarkup(codeLine)}[/]");
                    }

                    result.AppendLine("[grey]└──[/]");
                    codeBlockLanguage = string.Empty;
                    continue;
                }

                if (insideCodeBlock)
                {
                    codeBuffer.AppendLine(line);
                    continue;
                }

                string rendered = RenderLine(line);
                result.AppendLine(rendered);
            }

            if (insideCodeBlock)
            {
                string codeContent = codeBuffer.ToString();
                if (codeContent.EndsWith(Environment.NewLine))
                {
                    codeContent = codeContent.Substring(0, codeContent.Length - Environment.NewLine.Length);
                }
                else if (codeContent.EndsWith("\n"))
                {
                    codeContent = codeContent.Substring(0, codeContent.Length - 1);
                }

                string[] codeLines = codeContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (string codeLine in codeLines)
                {
                    result.AppendLine($"[on grey15]{EscapeMarkup(codeLine)}[/]");
                }

                result.AppendLine("[grey]└──[/]");
            }

            string output = result.ToString();
            if (output.EndsWith(Environment.NewLine))
            {
                output = output.Substring(0, output.Length - Environment.NewLine.Length);
            }
            else if (output.EndsWith("\n"))
            {
                output = output.Substring(0, output.Length - 1);
            }

            return output;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Renders a single non-code-block line of markdown to Spectre.Console markup.
        /// </summary>
        /// <param name="line">The raw markdown line.</param>
        /// <returns>The rendered markup string.</returns>
        private static string RenderLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("### "))
            {
                string text = trimmed.Substring(4);
                return $"[italic bold]{EscapeMarkup(text)}[/]";
            }

            if (trimmed.StartsWith("## "))
            {
                string text = trimmed.Substring(3);
                return $"[bold]{EscapeMarkup(text)}[/]";
            }

            if (trimmed.StartsWith("# "))
            {
                string text = trimmed.Substring(2);
                return $"[bold underline]{EscapeMarkup(text)}[/]";
            }

            if (trimmed.StartsWith("> "))
            {
                string text = trimmed.Substring(2);
                string rendered = RenderInlineFormatting(text);
                return $"[grey]│[/] [italic]{rendered}[/]";
            }

            Match orderedListMatch = Regex.Match(trimmed, @"^(\d+)\.\s+(.+)$");
            if (orderedListMatch.Success)
            {
                string number = orderedListMatch.Groups[1].Value;
                string text = orderedListMatch.Groups[2].Value;
                string rendered = RenderInlineFormatting(text);
                return $"  {number}. {rendered}";
            }

            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                string text = trimmed.Substring(2);
                string rendered = RenderInlineFormatting(text);
                return $"  • {rendered}";
            }

            return RenderInlineFormatting(line);
        }

        /// <summary>
        /// Applies inline markdown formatting (bold, italic, inline code) to a line of text.
        /// Escapes Spectre markup characters in regular text segments.
        /// </summary>
        /// <param name="text">The text to process for inline formatting.</param>
        /// <returns>The text with Spectre markup applied for inline formatting.</returns>
        private static string RenderInlineFormatting(string text)
        {
            List<InlineSegment> segments = new List<InlineSegment>();
            int position = 0;

            while (position < text.Length)
            {
                Match inlineCodeMatch = Regex.Match(text.Substring(position), @"`([^`]+)`");
                Match boldMatch = Regex.Match(text.Substring(position), @"\*\*(.+?)\*\*");
                Match italicMatch = Regex.Match(text.Substring(position), @"(?<!\*)\*([^*]+)\*(?!\*)");

                int codeIdx = inlineCodeMatch.Success ? inlineCodeMatch.Index : int.MaxValue;
                int boldIdx = boldMatch.Success ? boldMatch.Index : int.MaxValue;
                int italicIdx = italicMatch.Success ? italicMatch.Index : int.MaxValue;

                int minIdx = Math.Min(codeIdx, Math.Min(boldIdx, italicIdx));

                if (minIdx == int.MaxValue)
                {
                    segments.Add(new InlineSegment(EscapeMarkup(text.Substring(position)), false));
                    break;
                }

                if (codeIdx <= boldIdx && codeIdx <= italicIdx)
                {
                    if (inlineCodeMatch.Index > 0)
                    {
                        segments.Add(new InlineSegment(EscapeMarkup(text.Substring(position, inlineCodeMatch.Index)), false));
                    }
                    segments.Add(new InlineSegment($"[on grey15]{EscapeMarkup(inlineCodeMatch.Groups[1].Value)}[/]", true));
                    position += inlineCodeMatch.Index + inlineCodeMatch.Length;
                }
                else if (boldIdx <= italicIdx)
                {
                    if (boldMatch.Index > 0)
                    {
                        segments.Add(new InlineSegment(EscapeMarkup(text.Substring(position, boldMatch.Index)), false));
                    }
                    segments.Add(new InlineSegment($"[bold]{EscapeMarkup(boldMatch.Groups[1].Value)}[/]", true));
                    position += boldMatch.Index + boldMatch.Length;
                }
                else
                {
                    if (italicMatch.Index > 0)
                    {
                        segments.Add(new InlineSegment(EscapeMarkup(text.Substring(position, italicMatch.Index)), false));
                    }
                    segments.Add(new InlineSegment($"[italic]{EscapeMarkup(italicMatch.Groups[1].Value)}[/]", true));
                    position += italicMatch.Index + italicMatch.Length;
                }
            }

            StringBuilder sb = new StringBuilder();
            foreach (InlineSegment segment in segments)
            {
                sb.Append(segment.Text);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Escapes Spectre.Console markup characters ([ and ]) in plain text.
        /// </summary>
        /// <param name="text">The text to escape.</param>
        /// <returns>The escaped text safe for use in Spectre.Console markup.</returns>
        private static string EscapeMarkup(string text)
        {
            return Markup.Escape(text);
        }

        #endregion

        #region Private-Classes

        /// <summary>
        /// Represents a segment of inline text, either raw markup or pre-formatted.
        /// </summary>
        private class InlineSegment
        {
            /// <summary>
            /// The text content of this segment.
            /// </summary>
            public string Text { get; }

            /// <summary>
            /// Whether this segment already contains markup and should not be escaped further.
            /// </summary>
            public bool IsMarkup { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="InlineSegment"/> class.
            /// </summary>
            /// <param name="text">The segment text.</param>
            /// <param name="isMarkup">Whether the segment is pre-formatted markup.</param>
            public InlineSegment(string text, bool isMarkup)
            {
                Text = text;
                IsMarkup = isMarkup;
            }
        }

        #endregion
    }
}
