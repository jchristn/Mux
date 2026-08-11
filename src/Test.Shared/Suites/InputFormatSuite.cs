namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <c>--input-format jsonl</c> multi-turn print: the turn-record extraction and
    /// system-message stripping helpers, plus an end-to-end multi-turn run against a mock model that proves
    /// the prior turn's history is replayed into the next turn. Both valid and invalid record shapes are
    /// covered.
    /// </summary>
    public static class InputFormatSuite
    {
        /// <summary>
        /// Builds the input-format suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> containing all input-format cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "InputFormat",
                "Multi-turn jsonl stdin input",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("InputFormat", "ExtractTurnPromptReadsFields", "turn records expose prompt/text/content and bare strings", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("hello", PrintCommand.ExtractTurnPrompt("{\"prompt\":\"hello\"}"), "prompt field read");
                        MuxAssert.AreEqual("hi", PrintCommand.ExtractTurnPrompt("{\"text\":\"hi\"}"), "text field read");
                        MuxAssert.AreEqual("yo", PrintCommand.ExtractTurnPrompt("{\"content\":\"yo\"}"), "content field read");
                        MuxAssert.AreEqual("bare", PrintCommand.ExtractTurnPrompt("\"bare\""), "bare JSON string read");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("InputFormat", "ExtractTurnPromptRejectsBadRecords", "invalid records are rejected", (CancellationToken ct) =>
                    {
                        MuxAssert.Throws<InvalidOperationException>(() => PrintCommand.ExtractTurnPrompt("{\"role\":\"user\"}"), "object without a text field throws");
                        MuxAssert.Throws<InvalidOperationException>(() => PrintCommand.ExtractTurnPrompt("42"), "non-object/non-string JSON throws");
                        MuxAssert.Throws<System.Text.Json.JsonException>(() => PrintCommand.ExtractTurnPrompt("{not json"), "invalid JSON throws");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("InputFormat", "StripSystemMessagesDropsSystemRole", "system messages are dropped when carrying history", (CancellationToken ct) =>
                    {
                        List<ConversationMessage> conversation = new List<ConversationMessage>
                        {
                            new ConversationMessage { Role = RoleEnum.System, Content = "sys" },
                            new ConversationMessage { Role = RoleEnum.User, Content = "u" },
                            new ConversationMessage { Role = RoleEnum.Assistant, Content = "a" }
                        };

                        List<ConversationMessage> stripped = PrintCommand.StripSystemMessages(conversation);
                        MuxAssert.AreEqual(2, stripped.Count, "system message removed");
                        MuxAssert.IsFalse(stripped.Any((ConversationMessage m) => m.Role == RoleEnum.System), "no system messages remain");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("InputFormat", "MultiTurnThreadsHistory", "each jsonl turn replays the prior turn's history", (CancellationToken ct) => MultiTurnThreadsHistoryAsync(ct))
                });
        }

        private static Task MultiTurnThreadsHistoryAsync(CancellationToken ct)
        {
            using (MockHttpServer server = new MockHttpServer())
            {
                // Distinct-length keys so the longest-match router picks the right reply even when turn two's
                // request also contains turn one's text via replayed history.
                server.RegisterStreamingResponse("first turn", new List<string> { AgentTestHarness.BuildTextSseChunk("Reply one.") });
                server.RegisterStreamingResponse("second turn", new List<string> { AgentTestHarness.BuildTextSseChunk("Reply two.") });
                server.Start();

                string records = "{\"prompt\":\"first turn here\"}\n{\"prompt\":\"second turn hello\"}\n";
                CliInvocationResult result = InvokeCliWithStdin(
                    records,
                    new[]
                    {
                        "print",
                        "--input-format", "jsonl",
                        "--output-format", "jsonl",
                        "--yolo",
                        "--base-url", server.BaseUrl,
                        "--model", "test-model",
                        "--adapter-type", "openai-compatible"
                    });

                MuxAssert.AreEqual(0, result.ExitCode, "multi-turn run succeeds");

                int runStartedCount = CountOccurrences(result.StdOut, "\"eventType\":\"run_started\"");
                MuxAssert.AreEqual(2, runStartedCount, "one run_started per turn");

                MuxAssert.IsTrue(
                    server.ReceivedRequests.Count >= 2,
                    "the model was called for both turns");
                MuxAssert.Contains(
                    "first turn here",
                    server.ReceivedRequests[server.ReceivedRequests.Count - 1],
                    "the second turn's request replays the first turn's prompt from history");
            }

            return Task.CompletedTask;
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static CliInvocationResult InvokeCliWithStdin(string stdin, string[] args)
        {
            TextReader originalIn = Console.In;
            TextWriter originalOut = Console.Out;
            TextWriter originalErr = Console.Error;
            StringWriter stdout = new StringWriter();
            StringWriter stderr = new StringWriter();
            try
            {
                Console.SetIn(new StringReader(stdin));
                Console.SetOut(stdout);
                Console.SetError(stderr);
                int exitCode = Mux.Cli.Program.Main(args);
                return new CliInvocationResult(exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetIn(originalIn);
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
    }
}
