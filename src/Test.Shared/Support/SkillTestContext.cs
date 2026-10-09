namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Skills;

    /// <summary>
    /// Per-case state: the temp root, the seeded skills directory, and the project directory.
    /// </summary>
    public sealed class SkillTestContext
    {
        /// <summary>Creates the context.</summary>
        /// <param name="root">The temp root.</param>
        /// <param name="skills">The seeded skills directory.</param>
        /// <param name="token">The cancellation token.</param>
        public SkillTestContext(string root, string skills, CancellationToken token)
        {
            Root = root;
            Skills = skills;
            Project = Path.Combine(root, "project");
            Token = token;
        }

        /// <summary>The temp root.</summary>
        public string Root { get; }

        /// <summary>The seeded skills directory.</summary>
        public string Skills { get; }

        /// <summary>The project directory skills run in.</summary>
        public string Project { get; }

        /// <summary>The cancellation token.</summary>
        public CancellationToken Token { get; }

        /// <summary>Initializes a git repository in <see cref="Project"/>.</summary>
        /// <param name="branch">The initial branch.</param>
        /// <returns>The repository fixture.</returns>
        public GitFixture Repo(string branch = "main")
        {
            return new GitFixture(Project, branch);
        }

        /// <summary>Runs a skill command for real.</summary>
        /// <param name="skill">The skill id.</param>
        /// <param name="command">The command.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The result.</returns>
        public Task<SkillRunResult> Run(string skill, string command, params string[] arguments)
        {
            return Run(false, null, skill, command, arguments);
        }

        /// <summary>Runs a skill command, optionally as a dry run.</summary>
        /// <param name="dryRun">Whether to set MUX_SKILL_DRY_RUN.</param>
        /// <param name="skill">The skill id.</param>
        /// <param name="command">The command.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The result.</returns>
        public Task<SkillRunResult> Run(bool dryRun, string skill, string command, params string[] arguments)
        {
            return Run(dryRun, null, skill, command, arguments);
        }

        /// <summary>Runs a skill command with extra environment variables.</summary>
        /// <param name="dryRun">Whether to set MUX_SKILL_DRY_RUN.</param>
        /// <param name="environment">Extra variables, or null.</param>
        /// <param name="skill">The skill id.</param>
        /// <param name="command">The command.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The result.</returns>
        public Task<SkillRunResult> Run(bool dryRun, Dictionary<string, string>? environment, string skill, string command, params string[] arguments)
        {
            return RunCoreAsync(Project, dryRun, environment, skill, command, arguments);
        }

        /// <summary>Runs a skill command in a folder under <see cref="Project"/>, optionally as a dry run.</summary>
        /// <param name="subdirectory">The folder relative to <see cref="Project"/>; created when missing.</param>
        /// <param name="dryRun">Whether to set MUX_SKILL_DRY_RUN.</param>
        /// <param name="skill">The skill id.</param>
        /// <param name="command">The command.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The result.</returns>
        public Task<SkillRunResult> RunIn(string subdirectory, bool dryRun, string skill, string command, params string[] arguments)
        {
            string directory = Path.Combine(Project, subdirectory);
            Directory.CreateDirectory(directory);
            return RunCoreAsync(directory, dryRun, null, skill, command, arguments);
        }

        private async Task<SkillRunResult> RunCoreAsync(string workingDirectory, bool dryRun, Dictionary<string, string>? environment, string skill, string command, string[] arguments)
        {
            Skill loaded = new SkillLoader(Skills).Load(Path.Combine(Skills, skill));
            MuxAssert.IsTrue(loaded.IsValid, skill + " valid: " + string.Join("; ", loaded.Validation.Errors));
            SkillCommand? found = null;
            foreach (SkillCommand candidate in loaded.Manifest.Commands)
            {
                if (candidate.Name == command) found = candidate;
            }

            MuxAssert.IsNotNull(found, skill + " has " + command);
            Dictionary<string, string> env = new Dictionary<string, string>
            {
                ["GIT_CONFIG_NOSYSTEM"] = "1",
                ["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null",
                ["GIT_AUTHOR_NAME"] = "Mux Test",
                ["GIT_AUTHOR_EMAIL"] = "test@example.com",
                ["GIT_COMMITTER_NAME"] = "Mux Test",
                ["GIT_COMMITTER_EMAIL"] = "test@example.com"
            };
            if (dryRun) env[DefaultSkillHelpers.DryRunVariable] = "1";
            if (environment != null)
            {
                foreach (KeyValuePair<string, string> pair in environment) env[pair.Key] = pair.Value;
            }

            ToolResult result = await new SkillExecutor().ExecuteAsync("t", loaded, found!, new List<string>(arguments), workingDirectory, Token, env).ConfigureAwait(false);
            using (JsonDocument document = JsonDocument.Parse(result.Content))
            {
                JsonElement rootElement = document.RootElement;
                string stdout = rootElement.TryGetProperty("stdout", out JsonElement o) ? o.GetString() ?? string.Empty : string.Empty;
                string stderr = rootElement.TryGetProperty("stderr", out JsonElement e) ? e.GetString() ?? string.Empty : string.Empty;
                int exit = rootElement.TryGetProperty("exit_code", out JsonElement x) && x.ValueKind == JsonValueKind.Number ? x.GetInt32() : -1;
                return new SkillRunResult(skill + " " + command, stdout, stderr, exit);
            }
        }
    }
}
