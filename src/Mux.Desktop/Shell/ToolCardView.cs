namespace Mux.Desktop.Shell
{
    using System;
    using System.Text.Json;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Media;
    using Mux.Core.Models;

    /// <summary>
    /// A collapsible transcript card for a single tool call, built on <see cref="CollapsibleSection"/>. The
    /// header shows the tool name and status (running → succeeded/failed with elapsed time); expanding drills
    /// into the pretty-printed arguments and, once complete, the result. Theme-aware; collapsed by default.
    /// </summary>
    public sealed class ToolCardView
    {
        private const int MaxContentChars = 8000;

        private readonly AppTheme _Theme;
        private readonly string _ToolName;
        private readonly CollapsibleSection _Section;

        /// <summary>
        /// Build a card for a proposed tool call (initial "running" state).
        /// </summary>
        /// <param name="toolCall">The proposed tool call. Required.</param>
        /// <param name="theme">The active palette. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ToolCardView(ToolCall toolCall, AppTheme theme)
        {
            ArgumentNullException.ThrowIfNull(toolCall);
            ArgumentNullException.ThrowIfNull(theme);

            _Theme = theme;
            _ToolName = string.IsNullOrEmpty(toolCall.Name) ? "tool" : toolCall.Name;

            _Section = new CollapsibleSection("🔧 " + _ToolName + " · running…", theme);
            _Section.Body.Children.Add(SectionLabel("Arguments"));
            _Section.Body.Children.Add(CodeBlock(FormatJson(toolCall.Arguments)));
        }

        /// <summary>The control to add to the transcript.</summary>
        public Control Root
        {
            get => _Section.Root;
        }

        /// <summary>
        /// Update the card to a completed state and append the result for drill-in.
        /// </summary>
        /// <param name="success">Whether the tool succeeded.</param>
        /// <param name="elapsedMs">The tool execution time in milliseconds.</param>
        /// <param name="resultContent">The tool result text, if any.</param>
        public void Complete(bool success, long elapsedMs, string? resultContent)
        {
            _Section.SetHeader((success ? "✓ " : "✗ ") + _ToolName + "  (" + elapsedMs + " ms)", success ? _Theme.Success : _Theme.Error);
            _Section.Body.Children.Add(SectionLabel("Result"));
            _Section.Body.Children.Add(CodeBlock(Truncate(resultContent ?? string.Empty)));
        }

        private TextBlock SectionLabel(string text)
        {
            return new TextBlock { Text = text, Foreground = _Theme.Muted, FontSize = 11, FontWeight = FontWeight.SemiBold };
        }

        private TextBox CodeBlock(string text)
        {
            return new TextBox
            {
                Text = text,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Foreground = _Theme.Text,
                FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                MaxHeight = 240
            };
        }

        private static string FormatJson(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(raw);
                return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (JsonException)
            {
                return raw;
            }
        }

        private static string Truncate(string text)
        {
            if (text.Length <= MaxContentChars)
            {
                return text;
            }

            return text.Substring(0, MaxContentChars) + "\n… (truncated)";
        }
    }
}
