namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Cli.Commands;
    using Mux.Desktop.I18n;
    using Mux.Server;
    using Touchstone.Core;
    using TUIKit.Widgets;

    /// <summary>
    /// Show/hide toggles on password and key fields: the console reader behind <c>mux cloud login</c>, the terminal
    /// UI's Ctrl+R hint, the web dashboard's eye buttons and their strings, and the desktop strings.
    /// </summary>
    public static class SecretInputSuite
    {
        private const string Suite = "SecretInput";

        private static readonly string[] Locales = { "en", "es", "pt", "fr", "it", "de", "zh", "ar", "ru", "ms", "hi" };

        private static readonly string[] RevealKeys = { "act.showPassword", "act.hidePassword", "act.showKey", "act.hideKey" };

        /// <summary>
        /// Create the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Suite, "Show/hide toggles on password and key fields", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(Suite, "ConsoleReaderMasksAndReveals", "mux cloud login echoes * per character, Ctrl+R shows the text in place and hides it again", (CancellationToken ct) =>
                {
                    StringWriter echo = new StringWriter();
                    string value = MaskedLineReader.Read(Keys(Typed("abc"), CtrlR(), Typed("d"), Backspace(), CtrlR(), Typed("e"), Control('\t'), Enter()), echo);
                    MuxAssert.AreEqual("abce", value, "value keeps every typed character, minus the deleted one; Tab is ignored");
                    MuxAssert.AreEqual("***" + "\b\b\babc" + "d" + "\b \b" + "\b\b\b***" + "*", echo.ToString(), "masked, revealed in place, masked again");
                    MuxAssert.DoesNotContain("abce", echo.ToString(), "the final value is never echoed while hidden");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(Suite, "ConsoleReaderBackspaceOnEmpty", "Backspace on an empty value writes nothing", (CancellationToken ct) =>
                {
                    StringWriter echo = new StringWriter();
                    string value = MaskedLineReader.Read(Keys(Backspace(), CtrlR(), CtrlR(), Enter()), echo);
                    MuxAssert.AreEqual(string.Empty, value, "empty");
                    MuxAssert.AreEqual(string.Empty, echo.ToString(), "nothing to erase or redraw");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(Suite, "TerminalHintFollowsFocusAndState", "The Ctrl+R hint appears only for a focused masked field and says show or hide", (CancellationToken ct) =>
                {
                    TextField secret = SecretFields.Create();
                    MuxAssert.IsTrue(secret.IsMasked, "masked by default");
                    MuxAssert.AreEqual("Ctrl+R show password", SecretFields.Hint(secret, "password"), "hidden");
                    secret.IsRevealed = true;
                    MuxAssert.AreEqual("Ctrl+R hide password", SecretFields.Hint(secret, "password"), "revealed");
                    MuxAssert.IsNull(SecretFields.Hint(new TextField(), "key"), "plain fields get no hint");
                    MuxAssert.IsNull(SecretFields.Hint(null, "key"), "no focus, no hint");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(Suite, "DashboardSecretsHaveEyeButtons", "Every password input on the dashboard sits in a reveal wrapper, and form password fields get one too", (CancellationToken ct) =>
                {
                    string html = DashboardPage.Render(null, "9.9.9-test");
                    MatchCollection inputs = Regex.Matches(html, "<input type=\"password\"[^>]*>");
                    MuxAssert.IsTrue(inputs.Count >= 2, "the cloud password and REST API key are password inputs");
                    foreach (Match input in inputs)
                    {
                        int at = input.Index;
                        string before = html.Substring(Math.Max(0, at - 30), Math.Min(30, at));
                        string after = html.Substring(at + input.Length, Math.Min(80, html.Length - at - input.Length));
                        MuxAssert.Contains("<div class=\"pwwrap\">", before, "wrapped: " + input.Value);
                        MuxAssert.Contains("class=\"pwreveal\"", after, "followed by its eye button: " + input.Value);
                    }

                    MuxAssert.Contains("if(f.type===\"password\")inp=pwHtml(inp,", html, "form password fields (endpoint API key, MCP secret) get the toggle");
                    MuxAssert.Contains("pwSyncAll(el(\"modalBody\"))", html, "modal buttons are labeled when the modal opens");
                    MuxAssert.Contains("padding-inline-end:40px", html, "text never runs under the button, in either direction");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(Suite, "DashboardStringsInEveryLocale", "Show/Hide password and key labels exist in every dashboard locale", (CancellationToken ct) =>
                {
                    string html = DashboardPage.Render(null, "9.9.9-test");
                    foreach (string key in RevealKeys)
                    {
                        int count = Regex.Matches(html, "\"" + Regex.Escape(key) + "\":\"[^\"]+\"").Count;
                        MuxAssert.AreEqual(Locales.Length, count, key + " in every locale");
                    }

                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(Suite, "DesktopStringsTranslated", "The desktop has the reveal labels in every locale", (CancellationToken ct) =>
                {
                    LocalizationService service = new LocalizationService();
                    foreach (string locale in Locales)
                    {
                        service.SetActiveLocale(locale);
                        foreach (string key in RevealKeys)
                        {
                            string text = service.Get(key);
                            MuxAssert.IsFalse(string.IsNullOrWhiteSpace(text) || text == key, locale + " " + key);
                            if (locale != "en") MuxAssert.AreNotEqual(EnglishOf(key), text, locale + " " + key + " is translated");
                        }
                    }

                    return Task.CompletedTask;
                })
            });
        }

        private static string EnglishOf(string key)
        {
            LocalizationService en = new LocalizationService();
            en.SetActiveLocale("en");
            return en.Get(key);
        }

        private static Func<ConsoleKeyInfo> Keys(params IEnumerable<ConsoleKeyInfo>[] groups)
        {
            Queue<ConsoleKeyInfo> queue = new Queue<ConsoleKeyInfo>();
            foreach (IEnumerable<ConsoleKeyInfo> group in groups)
            {
                foreach (ConsoleKeyInfo key in group) queue.Enqueue(key);
            }

            return () => queue.Dequeue();
        }

        private static IEnumerable<ConsoleKeyInfo> Typed(string text)
        {
            foreach (char c in text) yield return new ConsoleKeyInfo(c, (ConsoleKey)char.ToUpperInvariant(c), false, false, false);
        }

        private static IEnumerable<ConsoleKeyInfo> CtrlR()
        {
            yield return new ConsoleKeyInfo('\u0012', ConsoleKey.R, false, false, true);
        }

        private static IEnumerable<ConsoleKeyInfo> Backspace()
        {
            yield return new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false);
        }

        private static IEnumerable<ConsoleKeyInfo> Control(char c)
        {
            yield return new ConsoleKeyInfo(c, ConsoleKey.Tab, false, false, false);
        }

        private static IEnumerable<ConsoleKeyInfo> Enter()
        {
            yield return new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
        }
    }
}
