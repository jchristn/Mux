namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Rendering;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite covering <see cref="LineBuffer"/> cursor navigation and editing. Ported
    /// from the legacy <c>LineBufferTests</c> suite. These cases are pure and synchronous; each
    /// returns a completed task after performing its assertions.
    /// </summary>
    public static class LineBufferSuite
    {
        /// <summary>
        /// Builds the suite descriptor for the LineBuffer test cases.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing all LineBuffer cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "LineBuffer",
                "LineBuffer cursor navigation and editing",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "LineBuffer",
                        "InsertAndReadBack",
                        "Insert and read back",
                        (CancellationToken ct) =>
                        {
                            LineBuffer buffer = new LineBuffer();
                            buffer.Insert('h');
                            buffer.Insert('i');
                            MuxAssert.AreEqual("hi", buffer.GetText(), "text");
                            MuxAssert.AreEqual(2, buffer.CursorColumn, "cursor");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "LineBuffer",
                        "LeftArrowThenInsert",
                        "Left arrow then insert",
                        (CancellationToken ct) =>
                        {
                            LineBuffer buffer = new LineBuffer();
                            buffer.Insert('a');
                            buffer.Insert('c');
                            buffer.MoveLeft();
                            buffer.Insert('b');
                            MuxAssert.AreEqual("abc", buffer.GetText(), "text");
                            MuxAssert.AreEqual(2, buffer.CursorColumn, "cursor after insert");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "LineBuffer",
                        "HomeTypeEndType",
                        "Home, type, End, type",
                        (CancellationToken ct) =>
                        {
                            LineBuffer buffer = new LineBuffer();
                            foreach (char c in "world")
                            {
                                buffer.Insert(c);
                            }
                            buffer.MoveHome();
                            buffer.Insert('[');
                            buffer.MoveEnd();
                            buffer.Insert(']');
                            MuxAssert.AreEqual("[world]", buffer.GetText(), "text");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "LineBuffer",
                        "BackspaceMidLine",
                        "Backspace mid-line",
                        (CancellationToken ct) =>
                        {
                            LineBuffer buffer = new LineBuffer();
                            foreach (char c in "abc")
                            {
                                buffer.Insert(c);
                            }
                            buffer.MoveLeft();
                            buffer.Backspace();
                            MuxAssert.AreEqual("ac", buffer.GetText(), "text");
                            MuxAssert.AreEqual(1, buffer.CursorColumn, "cursor");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "LineBuffer",
                        "DeleteKeyAtStart",
                        "Delete key at start",
                        (CancellationToken ct) =>
                        {
                            LineBuffer buffer = new LineBuffer();
                            buffer.Insert('a');
                            buffer.Insert('b');
                            buffer.MoveHome();
                            buffer.Delete();
                            MuxAssert.AreEqual("b", buffer.GetText(), "text");
                            MuxAssert.AreEqual(0, buffer.CursorColumn, "cursor");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "LineBuffer",
                        "MultiLineInsertAndMerge",
                        "Multi-line insert and merge",
                        (CancellationToken ct) =>
                        {
                            LineBuffer buffer = new LineBuffer();
                            buffer.Insert('a');
                            buffer.InsertNewLine();
                            buffer.Insert('b');
                            MuxAssert.AreEqual(2, buffer.LineCount, "line count");
                            MuxAssert.AreEqual(1, buffer.CurrentLineIndex, "current line");

                            buffer.MoveHome();
                            bool merged = buffer.RemoveCurrentLineAndMergeUp();
                            MuxAssert.IsTrue(merged, "merge succeeded");
                            MuxAssert.AreEqual(1, buffer.LineCount, "line count after merge");
                            MuxAssert.AreEqual("ab", buffer.GetText(), "merged text");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "LineBuffer",
                        "ClearResetsState",
                        "Clear resets state",
                        (CancellationToken ct) =>
                        {
                            LineBuffer buffer = new LineBuffer();
                            buffer.Insert('x');
                            buffer.InsertNewLine();
                            buffer.Insert('y');
                            buffer.Clear();
                            MuxAssert.AreEqual("", buffer.GetText(), "text after clear");
                            MuxAssert.AreEqual(0, buffer.CursorColumn, "cursor after clear");
                            MuxAssert.AreEqual(0, buffer.CurrentLineIndex, "line index after clear");
                            return Task.CompletedTask;
                        })
                });
        }
    }
}
