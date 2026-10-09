namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The category of every C#-defined default skill, kept in one place so the family files stay about what a skill
    /// does. <see cref="DefaultSkillBuilder"/> writes the result into the seeded <c>SKILL.md</c> as <c>category:</c>
    /// unless the definition sets <see cref="DefaultSkillDef.Category"/> itself.
    /// </summary>
    public static class DefaultSkillCategories
    {
        #region Private-Members

        private static readonly Dictionary<string, string> _Exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["git-bisect"] = "debugging",
            ["git-secret-scan"] = "security",
            ["pr-description"] = "git",
            ["pr-comments"] = "review",
            ["code-review"] = "review",
            ["simplify"] = "review",
            ["test-gap-review"] = "review",
            ["api-surface-diff"] = "review",
            ["security-review"] = "security",
            ["debug"] = "debugging",
            ["explain-codebase"] = "docs",
            ["init"] = "workflow",
            ["project-detect"] = "workflow",
            ["loop-until"] = "loops",
            ["fix-until-green"] = "loops",
            ["ci-watch"] = "loops",
            ["flaky-test-hunt"] = "loops",
            ["ci-repro"] = "devops",
            ["compose"] = "containers",
            ["dockerfile-lint"] = "containers",
            ["helm"] = "kubernetes",
            ["minikube"] = "kubernetes",
            ["terraform"] = "infrastructure",
            ["pulumi"] = "infrastructure",
            ["rackspace"] = "cloud",
            ["vercel"] = "cloud",
            ["netlify"] = "cloud",
            ["cloudflare"] = "cloud",
            ["flyio"] = "cloud",
            ["adr-new"] = "docs",
            ["doc-sync"] = "docs",
            ["readme-audit"] = "docs",
            ["spellcheck-docs"] = "docs",
            ["url-check"] = "docs",
            ["todo-scan"] = "hygiene",
            ["gitignore-audit"] = "hygiene",
            ["line-ending-check"] = "hygiene",
            ["large-file-scan"] = "hygiene",
            ["dead-code-scan"] = "hygiene",
            ["license-header-check"] = "hygiene",
            ["codestyle-audit"] = "hygiene",
            ["yaml-lint"] = "hygiene",
            ["json-validate"] = "hygiene",
            ["release-notes"] = "workflow",
            ["standup-summary"] = "workflow",
            ["env-report"] = "workflow",
            ["deps-audit"] = "security",
            ["sbom"] = "security",
            ["shell-lint"] = "hygiene",
            ["db-migrate"] = "data",
            ["node-upgrade"] = "languages",
            ["py-upgrade"] = "languages",
            ["web-framework"] = "frontend",
            ["storybook"] = "frontend",
            ["log-triage"] = "debugging",
            ["port-inspect"] = "debugging",
            ["bench"] = "debugging",
            ["ansible"] = "infrastructure",
            ["bicep"] = "infrastructure",
            ["openapi"] = "review",
            ["openapi-client"] = "scaffolding"
        };

        private static readonly string[][] _Prefixes =
        {
            new[] { "git-", "git" },
            new[] { "react-", "frontend" },
            new[] { "js-", "languages" },
            new[] { "py-", "languages" },
            new[] { "java-", "languages" },
            new[] { "cpp-", "languages" },
            new[] { "go-", "languages" },
            new[] { "cargo-", "languages" },
            new[] { "dotnet-", "languages" },
            new[] { "docker-", "containers" },
            new[] { "k8s-", "kubernetes" },
            new[] { "openstack-", "cloud" },
            new[] { "aws-", "cloud" },
            new[] { "azure-", "cloud" },
            new[] { "gcp-", "cloud" },
            new[] { "do-", "cloud" },
            new[] { "alibaba-", "cloud" },
            new[] { "huawei-", "cloud" },
            new[] { "ibm-", "cloud" },
            new[] { "linode-", "cloud" },
            new[] { "new-", "scaffolding" },
            new[] { "sql-", "data" },
            new[] { "nosql-", "data" },
            new[] { "graph-", "data" },
            new[] { "ruby-", "languages" },
            new[] { "php-", "languages" },
            new[] { "swift-", "mobile" },
            new[] { "android-", "mobile" },
            new[] { "flutter-", "mobile" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The category for a default skill id, or null when the id is not a known default.
        /// </summary>
        /// <param name="id">The skill id.</param>
        /// <returns>The category, or null.</returns>
        public static string? For(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (_Exact.TryGetValue(id, out string? exact)) return exact;
            foreach (string[] rule in _Prefixes)
            {
                if (id.StartsWith(rule[0], StringComparison.OrdinalIgnoreCase)) return rule[1];
            }

            return null;
        }

        #endregion
    }
}
