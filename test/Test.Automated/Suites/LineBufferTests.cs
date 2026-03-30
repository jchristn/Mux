namespace Test.Automated.Suites
{
    using System.Threading.Tasks;
    using Mux.Cli.Rendering;
    using Test.Shared;

    /// <summary>
    /// Automated integration tests for <see cref="LineBuffer"/> cursor navigation and editing.
    /// </summary>
    public class LineBufferTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// The name of this test suite.
        /// </summary>
        public override string Name => "LineBuffer Tests";

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Runs all LineBuffer tests.
        /// </summary>
        public override async Task RunTestsAsync()
        {
            await RunTest("Insert and read back", () =>
            {
                LineBuffer buffer = new LineBuffer();
                buffer.Insert('h');
                buffer.Insert('i');
                AssertEqual("hi", buffer.GetText(), "text");
                AssertEqual(2, buffer.CursorColumn, "cursor");
            });

            await RunTest("Left arrow then insert", () =>
            {
                LineBuffer buffer = new LineBuffer();
                buffer.Insert('a');
                buffer.Insert('c');
                buffer.MoveLeft();
                buffer.Insert('b');
                AssertEqual("abc", buffer.GetText(), "text");
                AssertEqual(2, buffer.CursorColumn, "cursor after insert");
            });

            await RunTest("Home, type, End, type", () =>
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
                AssertEqual("[world]", buffer.GetText(), "text");
            });

            await RunTest("Backspace mid-line", () =>
            {
                LineBuffer buffer = new LineBuffer();
                foreach (char c in "abc")
                {
                    buffer.Insert(c);
                }
                buffer.MoveLeft();
                buffer.Backspace(); // removes 'b'
                AssertEqual("ac", buffer.GetText(), "text");
                AssertEqual(1, buffer.CursorColumn, "cursor");
            });

            await RunTest("Delete key at start", () =>
            {
                LineBuffer buffer = new LineBuffer();
                buffer.Insert('a');
                buffer.Insert('b');
                buffer.MoveHome();
                buffer.Delete();
                AssertEqual("b", buffer.GetText(), "text");
                AssertEqual(0, buffer.CursorColumn, "cursor");
            });

            await RunTest("Multi-line insert and merge", () =>
            {
                LineBuffer buffer = new LineBuffer();
                buffer.Insert('a');
                buffer.InsertNewLine();
                buffer.Insert('b');
                AssertEqual(2, buffer.LineCount, "line count");
                AssertEqual(1, buffer.CurrentLineIndex, "current line");

                buffer.MoveHome();
                bool merged = buffer.RemoveCurrentLineAndMergeUp();
                AssertTrue(merged, "merge succeeded");
                AssertEqual(1, buffer.LineCount, "line count after merge");
                AssertEqual("ab", buffer.GetText(), "merged text");
            });

            await RunTest("Clear resets state", () =>
            {
                LineBuffer buffer = new LineBuffer();
                buffer.Insert('x');
                buffer.InsertNewLine();
                buffer.Insert('y');
                buffer.Clear();
                AssertEqual("", buffer.GetText(), "text after clear");
                AssertEqual(0, buffer.CursorColumn, "cursor after clear");
                AssertEqual(0, buffer.CurrentLineIndex, "line index after clear");
            });
        }

        #endregion
    }
}
