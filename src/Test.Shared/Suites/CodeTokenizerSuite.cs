namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Desktop.Text;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="CodeTokenizer"/>: keyword/string/comment/number classification across
    /// languages, the lossless round-trip guarantee, and negative cases (unknown language, unterminated
    /// literals, empty and null input).
    /// </summary>
    public static class CodeTokenizerSuite
    {
        /// <summary>
        /// Builds the code tokenizer suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the tokenizer cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "CodeTokenizer",
                "Fenced code block tokenizer",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("CodeTokenizer", "CsharpKeyword", "C# keywords are classified", (CancellationToken ct) =>
                    {
                        IReadOnlyList<CodeToken> tokens = CodeTokenizer.Tokenize("public void M()", "csharp");
                        MuxAssert.IsTrue(HasToken(tokens, "public", CodeTokenKind.Keyword), "public is a keyword");
                        MuxAssert.IsTrue(HasToken(tokens, "void", CodeTokenKind.Keyword), "void is a keyword");
                        MuxAssert.IsFalse(HasToken(tokens, "M", CodeTokenKind.Keyword), "M is not a keyword");
                        MuxAssert.IsTrue(HasTokenContains(tokens, "M", CodeTokenKind.Plain), "M rendered as plain text");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CodeTokenizer", "StringLiteral", "Double-quoted strings are one token including delimiters", (CancellationToken ct) =>
                    {
                        IReadOnlyList<CodeToken> tokens = CodeTokenizer.Tokenize("x = \"hi there\";", "csharp");
                        MuxAssert.IsTrue(HasToken(tokens, "\"hi there\"", CodeTokenKind.StringLiteral), "quoted string is one token");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CodeTokenizer", "EscapedQuoteInString", "A backslash-escaped quote does not end the string", (CancellationToken ct) =>
                    {
                        IReadOnlyList<CodeToken> tokens = CodeTokenizer.Tokenize("\"a\\\"b\"", "csharp");
                        MuxAssert.IsTrue(HasToken(tokens, "\"a\\\"b\"", CodeTokenKind.StringLiteral), "escaped quote kept inside");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CodeTokenizer", "LineComment", "// runs to end of line", (CancellationToken ct) =>
                    {
                        IReadOnlyList<CodeToken> tokens = CodeTokenizer.Tokenize("a; // note\nb;", "csharp");
                        MuxAssert.IsTrue(HasToken(tokens, "// note", CodeTokenKind.Comment), "line comment token");
                        MuxAssert.IsTrue(HasTokenContains(tokens, "b", CodeTokenKind.Plain), "code after newline resumes");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CodeTokenizer", "BlockComment", "/* */ spans lines", (CancellationToken ct) =>
                    {
                        IReadOnlyList<CodeToken> tokens = CodeTokenizer.Tokenize("a /* x\ny */ b", "csharp");
                        MuxAssert.IsTrue(HasToken(tokens, "/* x\ny */", CodeTokenKind.Comment), "block comment spans newline");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CodeTokenizer", "NumberLiterals", "Numbers (incl. hex and decimals) are classified", (CancellationToken ct) =>
                    {
                        IReadOnlyList<CodeToken> tokens = CodeTokenizer.Tokenize("n = 42 + 3.14 + 0xFF", "csharp");
                        MuxAssert.IsTrue(HasToken(tokens, "42", CodeTokenKind.Number), "integer");
                        MuxAssert.IsTrue(HasToken(tokens, "3.14", CodeTokenKind.Number), "decimal");
                        MuxAssert.IsTrue(HasToken(tokens, "0xFF", CodeTokenKind.Number), "hex");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CodeTokenizer", "DigitInIdentifierNotNumber", "A trailing digit inside an identifier is not a number", (CancellationToken ct) =>
                    {
                        IReadOnlyList<CodeToken> tokens = CodeTokenizer.Tokenize("var x1 = y2;", "csharp");
                        MuxAssert.IsFalse(HasKind(tokens, CodeTokenKind.Number), "no number tokens");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CodeTokenizer", "PythonHashComment", "Python uses # for comments and has no block comments", (CancellationToken ct) =>
                    {
                        IReadOnlyList<CodeToken> tokens = CodeTokenizer.Tokenize("x = 1  # hi\n/* not a comment */", "python");
                        MuxAssert.IsTrue(HasToken(tokens, "# hi", CodeTokenKind.Comment), "hash comment");
                        MuxAssert.IsFalse(HasKind(tokens, CodeTokenKind.Comment) && HasTokenContains(tokens, "not a comment", CodeTokenKind.Comment), "no C-style block comment in python");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CodeTokenizer", "SqlDashComment", "SQL keywords are case-insensitive and -- comments work", (CancellationToken ct) =>
                    {
                        IReadOnlyList<CodeToken> tokens = CodeTokenizer.Tokenize("SELECT * FROM t -- all\n", "sql");
                        MuxAssert.IsTrue(HasToken(tokens, "SELECT", CodeTokenKind.Keyword), "uppercase keyword");
                        MuxAssert.IsTrue(HasToken(tokens, "FROM", CodeTokenKind.Keyword), "FROM keyword");
                        MuxAssert.IsTrue(HasToken(tokens, "-- all", CodeTokenKind.Comment), "dash comment");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CodeTokenizer", "UnknownLanguageFallback", "Unknown language still colors strings but has no keywords", (CancellationToken ct) =>
                    {
                        IReadOnlyList<CodeToken> tokens = CodeTokenizer.Tokenize("public \"s\" 3", "no-such-lang");
                        MuxAssert.IsFalse(HasKind(tokens, CodeTokenKind.Keyword), "no keywords for unknown language");
                        MuxAssert.IsTrue(HasToken(tokens, "\"s\"", CodeTokenKind.StringLiteral), "string still recognized");
                        MuxAssert.IsTrue(HasToken(tokens, "3", CodeTokenKind.Number), "number still recognized");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CodeTokenizer", "UnterminatedStringDoesNotThrow", "An unterminated string consumes to end without throwing", (CancellationToken ct) =>
                    {
                        IReadOnlyList<CodeToken> tokens = CodeTokenizer.Tokenize("x = \"open", "csharp");
                        MuxAssert.IsTrue(HasToken(tokens, "\"open", CodeTokenKind.StringLiteral), "unterminated string to end");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CodeTokenizer", "LosslessRoundTrip", "Concatenating tokens reproduces the input exactly", (CancellationToken ct) =>
                    {
                        string source = "public int f() { return 0xA + 1.5; } // done\n/* block */\n\"str\"";
                        MuxAssert.AreEqual(source, Concat(CodeTokenizer.Tokenize(source, "csharp")), "roundtrip csharp");
                        MuxAssert.AreEqual(source, Concat(CodeTokenizer.Tokenize(source, "python")), "roundtrip python");
                        MuxAssert.AreEqual(source, Concat(CodeTokenizer.Tokenize(source, null)), "roundtrip null language");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("CodeTokenizer", "EmptyAndNull", "Empty and null input yield no tokens", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual(0, CodeTokenizer.Tokenize("", "csharp").Count, "empty input");
                        MuxAssert.AreEqual(0, CodeTokenizer.Tokenize(null, "csharp").Count, "null input");
                        return Task.CompletedTask;
                    })
                });
        }

        private static bool HasToken(IReadOnlyList<CodeToken> tokens, string text, CodeTokenKind kind)
        {
            foreach (CodeToken token in tokens)
            {
                if (token.Kind == kind && token.Text == text)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasTokenContains(IReadOnlyList<CodeToken> tokens, string substring, CodeTokenKind kind)
        {
            foreach (CodeToken token in tokens)
            {
                if (token.Kind == kind && token.Text.Contains(substring))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasKind(IReadOnlyList<CodeToken> tokens, CodeTokenKind kind)
        {
            foreach (CodeToken token in tokens)
            {
                if (token.Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Concat(IReadOnlyList<CodeToken> tokens)
        {
            StringBuilder builder = new StringBuilder();
            foreach (CodeToken token in tokens)
            {
                builder.Append(token.Text);
            }

            return builder.ToString();
        }
    }
}
