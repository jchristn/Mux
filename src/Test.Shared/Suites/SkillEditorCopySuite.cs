namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Server;
    using Touchstone.Core;
    using TUIKit.Input;

    /// <summary>
    /// Touchstone suite for copying a skill's SKILL.md to the clipboard from its editors: the terminal editor's
    /// Ctrl+T binding and status line, the <see cref="TerminalClipboard"/> OSC 52 encoder and platform command
    /// choice, and the web dashboard's copy buttons and their translations. Positive and negative cases.
    /// </summary>
    public static class SkillEditorCopySuite
    {
        #region Private-Members

        private const string SuiteId = "SkillEditorCopy";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the copy cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Copy SKILL.md to the clipboard from the skill editors",
                new List<TestCaseDescriptor>
                {
                    Case("CtrlTCopiesCurrentText", "Ctrl+T copies the editor's current text, unsaved edits included, and shows the status", async (CancellationToken ct) =>
                    {
                        List<string> copied = new List<string>();
                        SkillEditorModal editor = new SkillEditorModal("demo", "hello", (string text) => { copied.Add(text); return "Copied " + text.Length + " characters."; });
                        editor.HandleKey(KeyEvent.Char('X', KeyModifiers.None));
                        editor.HandleKey(KeyEvent.Char('t', KeyModifiers.Ctrl));
                        MuxAssert.AreEqual(1, copied.Count, "copied once");
                        MuxAssert.Contains("hello", copied[0], "original text copied");
                        MuxAssert.Contains("X", copied[0], "unsaved edit copied");
                        MuxAssert.Contains("Copied 6 characters.", editor.Status, "status shown");
                        MuxAssert.IsFalse(editor.Completion.IsCompleted, "copying does not close the editor");

                        editor.HandleKey(KeyEvent.Char('Z', KeyModifiers.None));
                        MuxAssert.AreEqual(string.Empty, editor.Status, "next key clears the status");
                        editor.HandleKey(KeyEvent.Char('s', KeyModifiers.Ctrl));
                        object? result = await editor.Completion.ConfigureAwait(false);
                        MuxAssert.DoesNotContain("t", ((string)result!).Replace("hello", string.Empty), "Ctrl+T inserted no text");
                    }),

                    Case("PlainTDoesNotCopy", "A plain 't' types text instead of copying", (CancellationToken ct) =>
                    {
                        int copies = 0;
                        SkillEditorModal editor = new SkillEditorModal("demo", string.Empty, (string text) => { copies++; return "x"; });
                        editor.HandleKey(KeyEvent.Char('t', KeyModifiers.None));
                        MuxAssert.AreEqual(0, copies, "no copy");
                        MuxAssert.AreEqual(string.Empty, editor.Status, "no status");
                        MuxAssert.Contains("Ctrl+T copy", SkillEditorModal.Hint, "hint names the chord");
                        return Task.CompletedTask;
                    }),

                    Case("EditorRejectsNullCopier", "The editor requires a copy action", (CancellationToken ct) =>
                    {
                        MuxAssert.Throws<ArgumentNullException>(() => new SkillEditorModal("demo", "x", null!), "null copier");
                        return Task.CompletedTask;
                    }),

                    Case("Osc52EncodesUtf8", "OSC 52 wraps base64 UTF-8 in the clipboard escape sequence", (CancellationToken ct) =>
                    {
                        string sequence = TerminalClipboard.BuildOsc52("héllo ✓");
                        MuxAssert.IsTrue(sequence.StartsWith("\u001b]52;c;", StringComparison.Ordinal), "prefix");
                        MuxAssert.IsTrue(sequence.EndsWith("\u0007", StringComparison.Ordinal), "terminator");
                        string payload = sequence.Substring(7, sequence.Length - 8);
                        MuxAssert.AreEqual("héllo ✓", Encoding.UTF8.GetString(Convert.FromBase64String(payload)), "round trip");
                        MuxAssert.AreEqual("\u001b]52;c;\u0007", TerminalClipboard.BuildOsc52(null), "null is empty");
                        return Task.CompletedTask;
                    }),

                    Case("PlatformCommandCandidates", "Each platform tries its own clipboard commands", (CancellationToken ct) =>
                    {
                        IReadOnlyList<string[]> windows = TerminalClipboard.GetCommandCandidates(true, false);
                        MuxAssert.AreEqual(1, windows.Count, "one Windows command");
                        MuxAssert.AreEqual("clip.exe", windows[0][0], "clip");
                        MuxAssert.AreEqual("pbcopy", TerminalClipboard.GetCommandCandidates(false, true)[0][0], "macOS");
                        IReadOnlyList<string[]> linux = TerminalClipboard.GetCommandCandidates(false, false);
                        MuxAssert.AreEqual(3, linux.Count, "three Linux commands");
                        MuxAssert.AreEqual("wl-copy", linux[0][0], "Wayland first");
                        MuxAssert.AreEqual("clipboard", linux[1][2], "xclip uses the clipboard selection");
                        return Task.CompletedTask;
                    }),

                    Case("DashboardSkillEditorHasCopy", "The dashboard's skill editor and viewer have copy buttons, translated in every language", (CancellationToken ct) =>
                    {
                        string html = DashboardPage.Render(null, "9.9.9-test");
                        MuxAssert.Contains("{label:t(\"act.copyskill\"),onClick:function(){var b=el(\"f_Body\");copyText(b?b.value:body,this);}}", html, "editor copies the live textarea");
                        MuxAssert.Contains("{label:t(\"act.copyskill\"),onClick:function(){copyText(s.Body||\"\",this);}}", html, "viewer copies the body");
                        MuxAssert.Contains("function formModal(title,fields,values,onSave,size,extraButtons)", html, "form modal takes extra buttons");
                        int copyJson = Regex.Matches(html, "\"act\\.copyjson\":").Count;
                        int copySkill = Regex.Matches(html, "\"act\\.copyskill\":").Count;
                        MuxAssert.IsTrue(copyJson >= 11, "eleven languages define copyjson");
                        MuxAssert.AreEqual(copyJson, copySkill, "every language that defines copyjson defines copyskill");
                        MuxAssert.Contains("\"act.copyskill\":\"⧉ Copy SKILL.md\"", html, "English label");
                        return Task.CompletedTask;
                    })
                });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        #endregion
    }
}
