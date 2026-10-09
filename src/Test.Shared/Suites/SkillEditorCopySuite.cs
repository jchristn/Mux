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

                    Case("DashboardScriptParses", "Every inline script in the web dashboard is valid JavaScript (node --check)", (CancellationToken ct) =>
                    {
                        string? node = FindOnPath("node");
                        if (node == null)
                        {
                            return Task.CompletedTask;
                        }

                        string html = DashboardPage.Render(null, "9.9.9-test");
                        MatchCollection scripts = Regex.Matches(html, "<script(?![^>]*\\bsrc=)[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        MuxAssert.IsTrue(scripts.Count > 0, "the page has inline scripts");
                        string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mux-dashboard-" + Guid.NewGuid().ToString("N") + ".js");
                        try
                        {
                            StringBuilder all = new StringBuilder();
                            foreach (Match script in scripts) all.Append(script.Groups[1].Value).Append(";\n");
                            System.IO.File.WriteAllText(file, all.ToString());
                            System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo(node) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
                            info.ArgumentList.Add("--check");
                            info.ArgumentList.Add(file);
                            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info)!)
                            {
                                string error = process.StandardError.ReadToEnd();
                                process.WaitForExit(30000);
                                MuxAssert.AreEqual(0, process.ExitCode, "dashboard JavaScript parses: " + error);
                            }
                        }
                        finally
                        {
                            try { System.IO.File.Delete(file); } catch (Exception) { }
                        }

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
                        MuxAssert.Contains("{id:\"Body\",label:\"SKILL.md\",type:\"textarea\",rows:20,copy:true,", html, "the SKILL.md body field carries its own copy icon");
                        MuxAssert.Contains("class=\"copybtn icon fieldcopy\" data-copy-target=", html, "copyable textareas render an inline copy button");
                        MuxAssert.Contains(".copywrap .fieldcopy{position:absolute;top:6px;right:8px;", html, "the icon sits in the field's top-right corner");
                        MuxAssert.Contains("querySelectorAll(\".fieldcopy\")", html, "the icon is wired to copy the live field value");
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

        private static string? FindOnPath(string executable)
        {
            string[] names = OperatingSystem.IsWindows() ? new[] { executable + ".exe", executable + ".cmd" } : new[] { executable };
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(System.IO.Path.PathSeparator))
            {
                foreach (string name in names)
                {
                    string candidate = System.IO.Path.Combine(directory, name);
                    if (!string.IsNullOrWhiteSpace(directory) && System.IO.File.Exists(candidate)) return candidate;
                }
            }

            return null;
        }

        #endregion
    }
}
