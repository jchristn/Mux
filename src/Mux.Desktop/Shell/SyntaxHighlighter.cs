namespace Mux.Desktop.Shell
{
    using System.Collections.Generic;
    using Avalonia.Controls.Documents;
    using Avalonia.Media;
    using Mux.Desktop.Text;

    /// <summary>
    /// Maps the classified tokens from <see cref="CodeTokenizer"/> to colored <see cref="Run"/>s using a
    /// theme-aware palette. All tokenization logic lives in <c>Mux.Desktop.Core</c> so it can be unit-tested;
    /// this type only owns the Avalonia rendering.
    /// </summary>
    public static class SyntaxHighlighter
    {
        /// <summary>
        /// Tokenize and color <paramref name="code"/> for the given language.
        /// </summary>
        /// <param name="code">The raw code block text.</param>
        /// <param name="language">The fence language hint (e.g. "csharp"); may be null or empty.</param>
        /// <param name="theme">The active theme, used to pick a light or dark token palette.</param>
        /// <returns>The colored runs, in order.</returns>
        public static IReadOnlyList<Run> Highlight(string code, string? language, AppTheme theme)
        {
            _Palette palette = _Palette.For(theme);
            List<Run> runs = new List<Run>();
            foreach (CodeToken token in CodeTokenizer.Tokenize(code, language))
            {
                runs.Add(new Run(token.Text) { Foreground = palette.For(token.Kind) });
            }

            return runs;
        }

        private sealed class _Palette
        {
            private IBrush _Text = Brushes.Black;
            private IBrush _Keyword = Brushes.Blue;
            private IBrush _StringLiteral = Brushes.DarkGreen;
            private IBrush _Comment = Brushes.Gray;
            private IBrush _Number = Brushes.Purple;

            public IBrush For(CodeTokenKind kind)
            {
                switch (kind)
                {
                    case CodeTokenKind.Keyword:
                        return _Keyword;
                    case CodeTokenKind.StringLiteral:
                        return _StringLiteral;
                    case CodeTokenKind.Comment:
                        return _Comment;
                    case CodeTokenKind.Number:
                        return _Number;
                    default:
                        return _Text;
                }
            }

            public static _Palette For(AppTheme theme)
            {
                if (theme.IsDark)
                {
                    return new _Palette
                    {
                        _Text = Solid("#e6edf3"),
                        _Keyword = Solid("#ff7b72"),
                        _StringLiteral = Solid("#a5d6ff"),
                        _Comment = Solid("#8b949e"),
                        _Number = Solid("#79c0ff")
                    };
                }

                return new _Palette
                {
                    _Text = Solid("#1f2328"),
                    _Keyword = Solid("#cf222e"),
                    _StringLiteral = Solid("#0a3069"),
                    _Comment = Solid("#6e7781"),
                    _Number = Solid("#0550ae")
                };
            }

            private static IBrush Solid(string hex)
            {
                return new SolidColorBrush(Color.Parse(hex));
            }
        }
    }
}
