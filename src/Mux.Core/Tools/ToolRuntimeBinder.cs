namespace Mux.Core.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Models;
    using Mux.Core.Skills;

    /// <summary>
    /// Owns the live composition of external tools (MCP + skills) onto an interactive
    /// <see cref="AgentLoopOptions"/> template. <see cref="Rebind"/> rebuilds the template's tool set and
    /// system prompt from the current MCP tools, the skills runtime, and the MCP/skills-free base prompt;
    /// <see cref="SetProfilePrompt"/> swaps the base prompt (on a profile change) and rebinds. The
    /// <see cref="McpRuntime"/> and <see cref="SkillRuntime"/> are constructed with <see cref="Rebind"/> as
    /// their change callback, so every server connect/disconnect and skill re-scan re-composes the template
    /// that the next submitted turn reads. All mutation is serialized under one lock, replacing the ad-hoc
    /// <c>ApplyTemplate</c> coordinator and <c>promptSync</c> lock that previously lived in the CLI entry
    /// point. This is the shared coordinator both front ends drive once the desktop wires MCP/skills in.
    /// </summary>
    public sealed class ToolRuntimeBinder
    {
        #region Private-Members

        private readonly object _Sync = new object();
        private readonly AgentLoopOptions _Template;
        private readonly int _BuiltInToolCount;
        private string _BasePrompt;
        private string _BaseCompaction;
        private McpRuntime? _McpRuntime;
        private SkillRuntime? _SkillRuntime;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the binder over an interactive template.
        /// </summary>
        /// <param name="template">The template rebound in place as tools change. Must not be null.</param>
        /// <param name="basePrompt">The MCP- and skills-free base system prompt.</param>
        /// <param name="baseCompaction">The compaction system prompt.</param>
        /// <param name="builtInToolCount">The number of built-in tools, used to report the effective count.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="template"/> is null.</exception>
        public ToolRuntimeBinder(AgentLoopOptions template, string basePrompt, string baseCompaction, int builtInToolCount)
        {
            _Template = template ?? throw new ArgumentNullException(nameof(template));
            _BasePrompt = basePrompt ?? string.Empty;
            _BaseCompaction = baseCompaction ?? string.Empty;
            _BuiltInToolCount = builtInToolCount;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The MCP runtime whose live tools and executor are composed onto the template. Set after
        /// construction (the runtime is created with <see cref="Rebind"/> as its change callback).
        /// </summary>
        public McpRuntime? McpRuntime
        {
            get { lock (_Sync) { return _McpRuntime; } }
            set { lock (_Sync) { _McpRuntime = value; } }
        }

        /// <summary>
        /// The skills runtime registered as an external tool provider, or null when skills are off. Set after
        /// construction (the runtime is created with <see cref="Rebind"/> as its change callback).
        /// </summary>
        public SkillRuntime? SkillRuntime
        {
            get { lock (_Sync) { return _SkillRuntime; } }
            set { lock (_Sync) { _SkillRuntime = value; } }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Rebuilds the template's external tools and system prompt from the current MCP tools, the skills
        /// runtime, and the base prompt. Safe to call from any thread; the template is read per job run, so a
        /// rebind applies to the next submitted turn.
        /// </summary>
        public void Rebind()
        {
            lock (_Sync)
            {
                RebindNoLock();
            }
        }

        /// <summary>
        /// Replaces the base (MCP/skills-free) system and compaction prompts — for example after a prompt
        /// profile switch — and rebinds so the new base keeps its MCP/skills awareness.
        /// </summary>
        /// <param name="basePrompt">The new base system prompt.</param>
        /// <param name="baseCompaction">The new compaction system prompt.</param>
        public void SetProfilePrompt(string basePrompt, string baseCompaction)
        {
            lock (_Sync)
            {
                _BasePrompt = basePrompt ?? string.Empty;
                _BaseCompaction = baseCompaction ?? string.Empty;
                RebindNoLock();
            }
        }

        #endregion

        #region Private-Methods

        private void RebindNoLock()
        {
            IReadOnlyList<ToolDefinition> mcpTools = _McpRuntime?.CurrentTools ?? new List<ToolDefinition>();
            Func<string, JsonElement, string, CancellationToken, Task<ToolResult>>? executor =
                _McpRuntime != null ? _McpRuntime.ExecuteToolAsync : null;
            ExternalToolsBinder.Apply(_Template, _BasePrompt, _BaseCompaction, mcpTools, executor, _SkillRuntime, _BuiltInToolCount);
        }

        #endregion
    }
}
