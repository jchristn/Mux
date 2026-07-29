namespace Mux.Cli.Rendering
{
    using System;
    using System.Collections.Generic;
    using TUIKit;

    /// <summary>
    /// TUIKit-backed replacement for the small styled console surface mux used from Spectre.Console.
    /// </summary>
    public static class AnsiConsole
    {
        /// <summary>
        /// Writes parsed markup to stdout.
        /// </summary>
        /// <param name="markup">The markup to write.</param>
        public static void Markup(string markup)
        {
            StyledConsole.ForStandardOutput().Markup(RewriteMarkup(markup));
        }

        /// <summary>
        /// Writes parsed markup to stdout followed by a newline.
        /// </summary>
        /// <param name="markup">The markup to write.</param>
        public static void MarkupLine(string markup)
        {
            StyledConsole.ForStandardOutput().MarkupLine(RewriteMarkup(markup));
        }

        /// <summary>
        /// Writes a newline to stdout.
        /// </summary>
        public static void WriteLine()
        {
            StyledConsole.ForStandardOutput().WriteLine();
        }

        /// <summary>
        /// Writes a rendered table to stdout.
        /// </summary>
        /// <param name="table">The table to write.</param>
        public static void Write(Table table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            StyledConsole.ForStandardOutput().Write(table.ToWidget());
        }

        internal static string RewriteMarkup(string markup)
        {
            return (markup ?? string.Empty)
                .Replace("grey15", "#262626", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// TUIKit-backed markup helper matching the Spectre escape call sites mux used.
    /// </summary>
    public static class Markup
    {
        /// <summary>
        /// Escapes TUIKit markup control characters.
        /// </summary>
        /// <param name="text">The literal text.</param>
        /// <returns>The escaped text.</returns>
        public static string Escape(string text)
        {
            return TUIKit.Markup.Escape(text);
        }
    }

    /// <summary>
    /// Table border styles used by mux's transitional line-mode renderer.
    /// </summary>
    public enum TableBorder
    {
        /// <summary>No border.</summary>
        None = 0,

        /// <summary>Square border.</summary>
        Square = 1,

        /// <summary>Rounded border.</summary>
        Rounded = 2
    }

    /// <summary>
    /// Compatibility wrapper around TUIKit's table widget.
    /// </summary>
    public sealed class Table
    {
        private readonly List<string> _Columns = new List<string>();
        private readonly List<string[]> _Rows = new List<string[]>();

        /// <summary>
        /// Gets or sets the table border.
        /// </summary>
        public TableBorder Border { get; set; } = TableBorder.None;

        /// <summary>
        /// Adds a column header.
        /// </summary>
        /// <param name="header">The header markup or text.</param>
        public void AddColumn(string header)
        {
            _Columns.Add(header ?? string.Empty);
        }

        /// <summary>
        /// Adds a row whose cells are interpreted as markup.
        /// </summary>
        /// <param name="cells">The row cells.</param>
        public void AddRow(params string[] cells)
        {
            _Rows.Add(cells ?? Array.Empty<string>());
        }

        internal TUIKit.Widgets.Table ToWidget()
        {
            string[] headers = new string[_Columns.Count == 0 ? 1 : _Columns.Count];
            if (_Columns.Count == 0)
            {
                headers[0] = string.Empty;
            }
            else
            {
                for (int i = 0; i < _Columns.Count; i++)
                {
                    headers[i] = TUIKit.Markup.Parse(AnsiConsole.RewriteMarkup(_Columns[i])).ToPlainString();
                }
            }

            TUIKit.Widgets.Table widget = new TUIKit.Widgets.Table(headers, ToTuiBorder(Border))
            {
                Sizing = TUIKit.Widgets.ColumnSizing.FitContent
            };

            foreach (string[] row in _Rows)
            {
                string[] normalized = new string[headers.Length];
                for (int i = 0; i < normalized.Length; i++)
                {
                    normalized[i] = i < row.Length
                        ? AnsiConsole.RewriteMarkup(row[i] ?? string.Empty)
                        : string.Empty;
                }

                widget.AddMarkupRow(normalized);
            }

            return widget;
        }

        private static TUIKit.Widgets.TableBorder ToTuiBorder(TableBorder border)
        {
            return border switch
            {
                TableBorder.Rounded => TUIKit.Widgets.TableBorder.Rounded,
                TableBorder.Square => TUIKit.Widgets.TableBorder.Square,
                _ => TUIKit.Widgets.TableBorder.None
            };
        }
    }
}
