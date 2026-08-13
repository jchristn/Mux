namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TUIKit.Content;

    /// <summary>
    /// Touchstone suite for the command-menu column alignment, now provided by TUIKit's
    /// <see cref="ColumnFormatter"/>: the key-chord column and the <c>/slash</c> alias column each align on a
    /// single vertical axis regardless of title or chord lengths. mux builds the menu rows and delegates the
    /// aligning to <c>ColumnFormatter.Format</c>, so this guards the behavior mux depends on.
    /// </summary>
    public static class CommandMenuFormatterSuite
    {
        private const string SuiteId = "CommandMenuFormatter";

        /// <summary>
        /// Builds the command-menu-formatter suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the formatter cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Command-menu chord and slash columns align vertically",
                new List<TestCaseDescriptor>
                {
                    Case("ChordAndSlashColumnsAlign", "Chords and /slash aliases each start at the same column on every row", (CancellationToken ct) =>
                    {
                        List<string> titles = new List<string> { "Exit", "Toggle boundary lines", "MCP servers", "Theme" };
                        List<string> chords = new List<string> { "ctrl+q", string.Empty, "f12", string.Empty };
                        List<string> aliases = new List<string> { "/quit /exit /q", "/borders /lines", "/mcp /servers", "/theme" };

                        List<IReadOnlyList<string>> cells = new List<IReadOnlyList<string>>();
                        for (int i = 0; i < titles.Count; i++)
                        {
                            cells.Add(new[] { titles[i], chords[i], aliases[i] });
                        }

                        IReadOnlyList<string> rows = ColumnFormatter.Format(cells, 2);

                        // The /slash column starts at the same index on every row.
                        int slashColumn = rows[0].IndexOf('/');
                        MuxAssert.IsTrue(slashColumn > 0, "slash column found");
                        for (int i = 0; i < rows.Count; i++)
                        {
                            MuxAssert.AreEqual(slashColumn, rows[i].IndexOf('/'), $"row {i} slash aligned");
                        }

                        // The chord column starts at the same index on the rows that have a chord.
                        int chordColumn = rows[0].IndexOf("ctrl+q", StringComparison.Ordinal);
                        MuxAssert.IsTrue(chordColumn > 0, "chord column found");
                        MuxAssert.AreEqual(chordColumn, rows[2].IndexOf("f12", StringComparison.Ordinal), "second chord aligned");

                        // The chord column sits left of the slash column.
                        MuxAssert.IsTrue(chordColumn < slashColumn, "chord column precedes slash column");

                        return Task.CompletedTask;
                    })
                });
        }

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }
    }
}
