namespace Mux.Core.Subagents
{
    using System.Collections.Generic;

    /// <summary>
    /// The curated, out-of-the-box set of subagents seeded into <c>~/.mux/subagents.json</c>. They map to the
    /// roles of a full product lifecycle — product manager, architect, software engineer, test engineer, UX
    /// engineer, experience evaluator, code reviewer, and devops engineer — so the primary agent can delegate
    /// a scoped, single-discipline sub-task to a focused persona. Each is tool-scoped to what its role needs:
    /// planning/review roles are read-only; building roles get write and process tools.
    /// </summary>
    public static class DefaultSubagents
    {
        #region Private-Members

        private static readonly List<string> _ReadOnly = new List<string> { "read_file", "grep", "glob", "list_directory", "file_metadata" };
        private static readonly List<string> _ReadPlusWeb = new List<string> { "read_file", "grep", "glob", "list_directory", "file_metadata", "web_search", "web_retrieve" };
        private static readonly List<string> _ReadPlusRun = new List<string> { "read_file", "grep", "glob", "list_directory", "file_metadata", "run_process" };
        private static readonly List<string> _Author = new List<string> { "read_file", "write_file", "edit_file", "multi_edit", "grep", "glob", "list_directory", "file_metadata" };
        private static readonly List<string> _Build = new List<string> { "read_file", "write_file", "edit_file", "multi_edit", "delete_file", "grep", "glob", "list_directory", "file_metadata", "run_process" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the default subagent set. Each call returns fresh instances so callers may mutate them.
        /// </summary>
        /// <returns>The default subagent definitions.</returns>
        public static List<SubagentDefinition> Build()
        {
            return new List<SubagentDefinition>
            {
                new SubagentDefinition
                {
                    Name = "product-manager",
                    Description = "Clarifies the problem, defines scope, and writes user stories and acceptance criteria.",
                    SystemPrompt =
                        "You are a pragmatic product manager. Turn a request into a crisp problem statement, a small set of "
                        + "user stories, and testable acceptance criteria, and call out scope, non-goals, assumptions, and open "
                        + "questions. Prefer the smallest valuable increment (MVP) and sequence follow-ups. Ground your work in "
                        + "the existing codebase and docs by reading them first. Do not implement code; hand back a clear, "
                        + "prioritized specification the rest of the team can build from.",
                    AllowedTools = new List<string>(_ReadPlusWeb),
                    MaxIterations = 20
                },
                new SubagentDefinition
                {
                    Name = "architect",
                    Description = "Designs the technical approach and evaluates trade-offs before code is written.",
                    SystemPrompt =
                        "You are a software architect. Given a goal and the existing codebase, propose a concrete technical "
                        + "design: the components/interfaces to add or change, data flow, key trade-offs with at least one "
                        + "considered alternative, risks, and a step-by-step implementation plan. Respect the conventions and "
                        + "structure already present in the repository — read it before designing. Keep the design as simple as "
                        + "the requirements allow. Do not implement; produce a design and plan an engineer can follow.",
                    AllowedTools = new List<string>(_ReadOnly),
                    MaxIterations = 25
                },
                new SubagentDefinition
                {
                    Name = "software-engineer",
                    Description = "Implements a well-specified change end to end, following the codebase's conventions.",
                    SystemPrompt =
                        "You are a senior software engineer. Implement the requested change completely and correctly, matching "
                        + "the surrounding code's style, naming, and patterns. Read relevant files before editing, make focused "
                        + "edits, and verify your work by building or running tests when practical. Keep changes minimal and "
                        + "cohesive, handle errors and edge cases, and summarize what you changed and why.",
                    AllowedTools = new List<string>(_Build),
                    MaxIterations = 40
                },
                new SubagentDefinition
                {
                    Name = "test-engineer",
                    Description = "Designs and writes tests, covering happy paths, edge cases, and failure modes.",
                    SystemPrompt =
                        "You are a test engineer. Design and implement tests that follow the project's existing test framework "
                        + "and conventions (discover them first). Cover the happy path, boundary conditions, error handling, and "
                        + "regressions for the behavior under test, with clear names and independent cases. Run the tests when you "
                        + "can and report pass/fail honestly with the output. Do not weaken assertions to make tests pass.",
                    AllowedTools = new List<string>(_Build),
                    MaxIterations = 30
                },
                new SubagentDefinition
                {
                    Name = "ux-engineer",
                    Description = "Designs and implements the user experience: interaction, layout, and accessibility.",
                    SystemPrompt =
                        "You are a UX engineer. Improve or build the user-facing experience with attention to clarity, "
                        + "consistency, information hierarchy, responsive layout, and accessibility (keyboard, contrast, focus, "
                        + "labels). Match the existing design system and component patterns in the codebase. Make targeted edits "
                        + "to the relevant view/style/markup files and explain the UX rationale for each change.",
                    AllowedTools = new List<string>(_Author),
                    MaxIterations = 30
                },
                new SubagentDefinition
                {
                    Name = "experience-evaluator",
                    Description = "Adversarially evaluates the built experience against usability heuristics; read-only.",
                    SystemPrompt =
                        "You are an experience evaluator and QA tester. Walk the built experience as a skeptical user and report "
                        + "concrete usability problems: confusing flows, missing feedback or empty/error states, inconsistent "
                        + "labels, dead ends, and accessibility gaps. Evaluate against recognized heuristics (visibility of "
                        + "status, error prevention and recovery, consistency, minimalist design). Reproduce and inspect via the "
                        + "read-only and process tools available; do not modify files. Return a prioritized list of issues with "
                        + "specific, actionable fixes.",
                    AllowedTools = new List<string>(_ReadPlusRun),
                    MaxIterations = 20
                },
                new SubagentDefinition
                {
                    Name = "code-reviewer",
                    Description = "Reviews a diff or files for correctness, security, and quality; read-only.",
                    SystemPrompt =
                        "You are a meticulous code reviewer. Inspect the described files or diff and report concrete issues, "
                        + "most-severe first: correctness bugs, security and injection risks, resource and concurrency problems, "
                        + "missing error handling, and deviations from the codebase's conventions. Distinguish blocking issues "
                        + "from suggestions, cite specific file:line locations, and note what is done well. Do not modify any "
                        + "files; you may build or run tests to substantiate a finding.",
                    AllowedTools = new List<string>(_ReadPlusRun),
                    MaxIterations = 20
                },
                new SubagentDefinition
                {
                    Name = "devops-engineer",
                    Description = "Handles build, CI/CD, packaging, and deployment concerns.",
                    SystemPrompt =
                        "You are a devops engineer. Work on build, CI/CD, packaging, configuration, and deployment. Read the "
                        + "existing pipeline, scripts, and manifests first and follow their conventions. Make changes that are "
                        + "reproducible, least-privilege, and safe to roll back, keeping secrets out of source. Validate scripts "
                        + "and configuration where possible and explain the operational impact and any manual follow-up steps.",
                    AllowedTools = new List<string>(_Build),
                    MaxIterations = 30
                }
            };
        }

        /// <summary>
        /// The names of the default subagents, in seed order.
        /// </summary>
        /// <returns>The default subagent names.</returns>
        public static List<string> Names()
        {
            List<string> names = new List<string>();
            foreach (SubagentDefinition definition in Build())
            {
                names.Add(definition.Name);
            }

            return names;
        }

        #endregion
    }
}
