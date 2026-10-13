namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Interaction;
    using Mux.Desktop.ViewModels;
    using Touchstone.Core;

    /// <summary>Slash-prefix matching and composer boundaries shared by native clients.</summary>
    public static class SlashCommandCompletionSuite
    {
        /// <summary>Builds slash command completion regression tests.</summary>
        public static TestSuiteDescriptor Create()
        {
            const string id = "SlashCommandCompletion";
            return new TestSuiteDescriptor(id, "Slash-command prefix completion", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(id, "MatchingAndLimits", "Aliases match case-insensitively with deterministic order, deduplication, and limits", (CancellationToken ct) =>
                {
                    var aliases = new[] { "endpoints", "endpoint", "ENDPOINT", "help", "", "usage" };
                    var matches = SlashCommandCompletion.Match("/END", 4, aliases);
                    MuxAssert.AreEqual(2, matches.Count, "distinct matches");
                    MuxAssert.AreEqual("/endpoint", matches[0], "ordered first alias");
                    MuxAssert.AreEqual("/endpoints", matches[1], "ordered second alias");
                    MuxAssert.AreEqual(1, SlashCommandCompletion.Match("/", 1, aliases, 1).Count, "suggestion limit");
                    MuxAssert.AreEqual(0, SlashCommandCompletion.Match("/unknown", 8, aliases).Count, "unknown prefix");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(id, "CommandTokenOnly", "Arguments, ordinary prompts, pasted multiline text, and a caret inside the token are left alone", (CancellationToken ct) =>
                {
                    foreach (string text in new[] { "hello /end", " /end", "/endpoint value", "/endpoint\tvalue", "/end\nhello", "" })
                        MuxAssert.AreEqual(0, SlashCommandCompletion.Match(text, text.Length, new[] { "endpoint" }).Count, "no suggestions for " + text);
                    MuxAssert.AreEqual(0, SlashCommandCompletion.Match("/end", 2, new[] { "endpoint" }).Count, "caret not at end");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(id, "DashboardEmbedsSharedCompletion", "Web dashboard embeds the shared completion UI without exposing hidden diagnostics", (CancellationToken ct) =>
                {
                    string html = Mux.Server.DashboardPage.Render(null, "test");
                    MuxAssert.Contains("id=\"slash-completions\"", html, "suggestion list");
                    MuxAssert.Contains("MuxSlashCompletion.attach", html, "shared completion controller wired");
                    MuxAssert.IsTrue(!html.Contains("__SLASH_COMPLETION_JS__"), "asset placeholder replaced");
                    MuxAssert.Contains("CHAT_COMMAND_VIEWS", html, "client supported commands drive completion");
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(id, "DesktopSupportedAliases", "Desktop suggestions include supported endpoint aliases and omit hidden diagnostics", (CancellationToken ct) =>
                {
                    var matches = SlashCommandCompletion.Match("/end", 4, DesktopSlashCommands.Aliases);
                    MuxAssert.AreEqual(2, matches.Count, "desktop endpoint aliases");
                    MuxAssert.AreEqual(0, SlashCommandCompletion.Match("/toast", 6, DesktopSlashCommands.Aliases).Count, "diagnostics excluded");
                    return Task.CompletedTask;
                })
            });
        }
    }
}
