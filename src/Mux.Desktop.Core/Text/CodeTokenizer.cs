namespace Mux.Desktop.Text
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// A tiny, dependency-free, lossless tokenizer for fenced code blocks. It classifies a handful of common
    /// languages (C#, JavaScript/TypeScript and other C-family languages, Python, JSON, SQL, shell) into
    /// keyword/string/comment/number spans, falling back to a language-agnostic pass that still recognizes
    /// strings, comments, and numbers when the language is unknown. It never throws on malformed input and
    /// guarantees that concatenating every returned token's text reproduces the input exactly.
    /// </summary>
    public static class CodeTokenizer
    {
        /// <summary>
        /// Tokenize <paramref name="code"/> for the given language.
        /// </summary>
        /// <param name="code">The raw code block text; null is treated as empty.</param>
        /// <param name="language">The fence language hint (e.g. "csharp"); may be null or empty.</param>
        /// <returns>The classified tokens, in order. Lossless: the concatenation equals the input.</returns>
        public static IReadOnlyList<CodeToken> Tokenize(string? code, string? language)
        {
            List<CodeToken> tokens = new List<CodeToken>();
            if (string.IsNullOrEmpty(code))
            {
                return tokens;
            }

            LangSpec spec = LangSpec.For(language);
            StringBuilder plain = new StringBuilder();

            void FlushPlain()
            {
                if (plain.Length > 0)
                {
                    tokens.Add(new CodeToken(plain.ToString(), CodeTokenKind.Plain));
                    plain.Clear();
                }
            }

            int i = 0;
            int length = code.Length;
            while (i < length)
            {
                char c = code[i];

                if (spec.BlockComments && c == '/' && i + 1 < length && code[i + 1] == '*')
                {
                    FlushPlain();
                    int end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int stop = end < 0 ? length : end + 2;
                    tokens.Add(new CodeToken(code.Substring(i, stop - i), CodeTokenKind.Comment));
                    i = stop;
                    continue;
                }

                if (spec.LineComment.Length > 0 && MatchesAt(code, i, spec.LineComment))
                {
                    FlushPlain();
                    int end = code.IndexOf('\n', i);
                    int stop = end < 0 ? length : end;
                    tokens.Add(new CodeToken(code.Substring(i, stop - i), CodeTokenKind.Comment));
                    i = stop;
                    continue;
                }

                if (spec.LineComment2.Length > 0 && MatchesAt(code, i, spec.LineComment2))
                {
                    FlushPlain();
                    int end = code.IndexOf('\n', i);
                    int stop = end < 0 ? length : end;
                    tokens.Add(new CodeToken(code.Substring(i, stop - i), CodeTokenKind.Comment));
                    i = stop;
                    continue;
                }

                if (spec.StringDelimiters.IndexOf(c) >= 0)
                {
                    FlushPlain();
                    int stop = ScanString(code, i, c);
                    tokens.Add(new CodeToken(code.Substring(i, stop - i), CodeTokenKind.StringLiteral));
                    i = stop;
                    continue;
                }

                if (IsDigit(c) && !IsIdentifierChar(i > 0 ? code[i - 1] : ' '))
                {
                    FlushPlain();
                    int stop = ScanNumber(code, i);
                    tokens.Add(new CodeToken(code.Substring(i, stop - i), CodeTokenKind.Number));
                    i = stop;
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    int stop = i + 1;
                    while (stop < length && IsIdentifierChar(code[stop]))
                    {
                        stop++;
                    }

                    string word = code.Substring(i, stop - i);
                    if (spec.Keywords.Contains(word))
                    {
                        FlushPlain();
                        tokens.Add(new CodeToken(word, CodeTokenKind.Keyword));
                    }
                    else
                    {
                        plain.Append(word);
                    }

                    i = stop;
                    continue;
                }

                plain.Append(c);
                i++;
            }

            FlushPlain();
            return tokens;
        }

        private static bool MatchesAt(string text, int index, string token)
        {
            if (index + token.Length > text.Length)
            {
                return false;
            }

            for (int k = 0; k < token.Length; k++)
            {
                if (text[index + k] != token[k])
                {
                    return false;
                }
            }

            return true;
        }

        private static int ScanString(string text, int start, char delimiter)
        {
            int i = start + 1;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\\' && delimiter != '`')
                {
                    i += 2;
                    continue;
                }

                if (c == delimiter)
                {
                    return i + 1;
                }

                if (c == '\n' && delimiter != '`')
                {
                    return i;
                }

                i++;
            }

            return text.Length;
        }

        private static int ScanNumber(string text, int start)
        {
            int i = start;
            if (text[i] == '0' && i + 1 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X'))
            {
                i += 2;
                while (i < text.Length && Uri.IsHexDigit(text[i]))
                {
                    i++;
                }

                return i;
            }

            while (i < text.Length && (IsDigit(text[i]) || text[i] == '.' || text[i] == '_'))
            {
                i++;
            }

            return i;
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static bool IsIdentifierStart(char c)
        {
            return char.IsLetter(c) || c == '_' || c == '$';
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '$';
        }

        private sealed class LangSpec
        {
            public HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal);
            public string LineComment = "//";
            public string LineComment2 = string.Empty;
            public bool BlockComments = true;
            public string StringDelimiters = "\"'`";

            public static LangSpec For(string? language)
            {
                string lang = (language ?? string.Empty).Trim().ToLowerInvariant();
                switch (lang)
                {
                    case "cs":
                    case "csharp":
                    case "c#":
                        return CFamily(CsharpKeywords);
                    case "js":
                    case "javascript":
                    case "jsx":
                    case "ts":
                    case "typescript":
                    case "tsx":
                    case "java":
                    case "c":
                    case "cpp":
                    case "c++":
                    case "go":
                    case "rust":
                    case "rs":
                        return CFamily(JsKeywords);
                    case "py":
                    case "python":
                        return new LangSpec { Keywords = PythonKeywords, LineComment = "#", BlockComments = false, StringDelimiters = "\"'" };
                    case "sh":
                    case "bash":
                    case "shell":
                    case "zsh":
                    case "console":
                        return new LangSpec { Keywords = ShellKeywords, LineComment = "#", BlockComments = false, StringDelimiters = "\"'`" };
                    case "sql":
                        return new LangSpec { Keywords = SqlKeywords, LineComment = "--", LineComment2 = "#", BlockComments = true, StringDelimiters = "'\"" };
                    case "json":
                        return new LangSpec { Keywords = JsonKeywords, LineComment = string.Empty, BlockComments = false, StringDelimiters = "\"" };
                    default:
                        return new LangSpec { Keywords = new HashSet<string>(StringComparer.Ordinal), LineComment = string.Empty, LineComment2 = string.Empty, BlockComments = false, StringDelimiters = "\"'`" };
                }
            }

            private static LangSpec CFamily(HashSet<string> keywords)
            {
                return new LangSpec { Keywords = keywords, LineComment = "//", BlockComments = true, StringDelimiters = "\"'`" };
            }

            private static readonly HashSet<string> CsharpKeywords = new HashSet<string>(StringComparer.Ordinal)
            {
                "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char",
                "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double",
                "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
                "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
                "namespace", "new", "null", "object", "operator", "out", "override", "params", "private",
                "protected", "public", "readonly", "record", "ref", "return", "sbyte", "sealed", "short",
                "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
                "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void",
                "volatile", "while", "yield", "nameof", "when", "get", "set", "value"
            };

            private static readonly HashSet<string> JsKeywords = new HashSet<string>(StringComparer.Ordinal)
            {
                "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default",
                "delete", "do", "else", "enum", "export", "extends", "false", "finally", "for", "func",
                "function", "if", "implements", "import", "in", "instanceof", "interface", "let", "new", "null",
                "package", "private", "protected", "public", "return", "static", "super", "switch", "this",
                "throw", "true", "try", "type", "typeof", "var", "void", "while", "with", "yield", "of", "as",
                "from", "struct", "impl", "fn", "pub", "mut", "use", "match", "trait", "where", "defer", "go",
                "map", "range", "chan", "select", "int", "string", "bool", "float", "double", "number"
            };

            private static readonly HashSet<string> PythonKeywords = new HashSet<string>(StringComparer.Ordinal)
            {
                "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif",
                "else", "except", "False", "finally", "for", "from", "global", "if", "import", "in", "is",
                "lambda", "None", "nonlocal", "not", "or", "pass", "raise", "return", "True", "try", "while",
                "with", "yield", "self", "match", "case"
            };

            private static readonly HashSet<string> ShellKeywords = new HashSet<string>(StringComparer.Ordinal)
            {
                "if", "then", "else", "elif", "fi", "for", "in", "do", "done", "while", "until", "case", "esac",
                "function", "return", "local", "export", "source", "echo", "cd", "set", "unset", "read", "exit"
            };

            private static readonly HashSet<string> SqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "select", "from", "where", "insert", "into", "values", "update", "set", "delete", "create",
                "table", "alter", "drop", "index", "view", "join", "inner", "left", "right", "outer", "on",
                "group", "by", "order", "having", "limit", "offset", "and", "or", "not", "null", "as", "distinct",
                "count", "sum", "avg", "min", "max", "primary", "key", "foreign", "references", "default", "case",
                "when", "then", "else", "end", "union", "all", "exists", "between", "like", "in", "is"
            };

            private static readonly HashSet<string> JsonKeywords = new HashSet<string>(StringComparer.Ordinal)
            {
                "true", "false", "null"
            };
        }
    }
}
