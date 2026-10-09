namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Skills;
    using Mux.Core.Skills.Evaluation;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite that asks a real model which skill it would use for each built-in evaluation prompt, with the
    /// same listing and skill tools the agent sends. It runs only when an endpoint and model are supplied (see
    /// <see cref="LiveModelOptions"/>; Test.Automated takes <c>--llm-endpoint</c>, <c>--llm-model</c>, and friends) and is
    /// skipped otherwise, so ordinary and CI runs never call a model.
    /// </summary>
    public static class SkillSelectionLiveSuite
    {
        #region Private-Members

        private const string SuiteId = "SkillSelectionLive";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool configured = LiveModelOptions.IsConfigured;
            const string reason = "no live model configured (pass --llm-endpoint and --llm-model, or set MUX_TEST_LLM_ENDPOINT and MUX_TEST_LLM_MODEL)";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(SuiteId, "ModelPicksExpectedSkills", "A real model picks an expected skill for the built-in prompts at or above the configured floor", RunAsync, skip: !configured, skipReason: reason)
            };

            return new TestSuiteDescriptor(SuiteId, "Skill selection against a live model", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task RunAsync(CancellationToken ct)
        {
            EndpointConfig endpoint = LiveModelOptions.BuildEndpoint();
            List<SkillEvalCase> all = SkillSelectionEvaluator.LoadBuiltInCases().ToList();
            List<SkillEvalCase> chosen = Spread(all, LiveModelOptions.CaseLimit);

            string root = Path.Combine(Path.GetTempPath(), "mux-skilleval-live-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                DefaultSkillLibrary.SeedInto(root);
                SkillSelectionEvaluator evaluator = new SkillSelectionEvaluator(new SkillLoader(root).Discover()) { TopN = 1 };
                int failedCalls = 0;
                List<string> callErrors = new List<string>();
                SkillEvalReport report;
                using (LlmClient client = new LlmClient(endpoint))
                {
                    SkillModelChooser chooser = new SkillModelChooser(client.SendAsync);
                    report = await evaluator.EvaluateAsync(chosen, async (SkillEvalCase evalCase, IReadOnlyList<Skill> listed, CancellationToken token) =>
                    {
                        try
                        {
                            return await chooser.ChooseAsync(evalCase, listed, token).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException) || !token.IsCancellationRequested)
                        {
                            failedCalls++;
                            if (callErrors.Count < 3) callErrors.Add(evalCase.Id + ": " + ex.Message);
                            return new List<string>();
                        }
                    }, ct).ConfigureAwait(false);
                }

                WriteReport(report, endpoint.Model, failedCalls);
                MuxAssert.IsTrue(failedCalls <= chosen.Count / 4, $"{failedCalls} of {chosen.Count} model calls failed, so the endpoint is not usable: {string.Join(" | ", callErrors)}");
                string misses = string.Join("\n", report.Results.Where(r => !r.Passed).Take(25).Select(r =>
                    "  " + r.Case.Id + " (\"" + r.Case.Prompt + "\"): expected " + string.Join(" or ", r.Case.Expect) + ", picked " + (r.Top.Count > 0 ? r.Top[0] : "nothing")));
                MuxAssert.IsTrue(report.Top1Rate >= LiveModelOptions.Floor,
                    $"{endpoint.Model} picked an expected skill for {report.Top1Rate * 100:0.0}% of {report.Results.Count} prompts, below the floor of {LiveModelOptions.Floor * 100:0}%. First misses:\n{misses}");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        // An evenly spaced subset, so a short run still touches every category.
        private static List<SkillEvalCase> Spread(List<SkillEvalCase> all, int limit)
        {
            if (limit <= 0 || limit >= all.Count) return all;
            List<SkillEvalCase> picked = new List<SkillEvalCase>();
            for (int i = 0; i < limit; i++) picked.Add(all[(int)((long)i * all.Count / limit)]);
            return picked;
        }

        private static void WriteReport(SkillEvalReport report, string model, int failedCalls)
        {
            string path = LiveModelOptions.ReportPath;
            if (string.IsNullOrWhiteSpace(path)) return;
            var body = new
            {
                model,
                cases = report.Results.Count,
                passed = report.Passed,
                top1Rate = Math.Round(report.Top1Rate, 4),
                failedCalls,
                results = report.Results.Select(r => new { id = r.Case.Id, prompt = r.Case.Prompt, expect = r.Case.Expect, picked = r.Top.FirstOrDefault() ?? string.Empty, passed = r.Passed })
            };
            File.WriteAllText(path, JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true }));
        }

        #endregion
    }
}
