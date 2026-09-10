namespace Mux.Core.Subagents
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An in-memory, case-insensitive lookup of the <see cref="SubagentDefinition"/> instances available
    /// to a run. Built once from the loaded <c>subagents.json</c> and threaded onto the agent loop so the
    /// <c>spawn_subagent</c> tool can resolve a requested name and enumerate the choices offered to the
    /// model. Invalid definitions (empty name/prompt, or a duplicate name) are dropped at construction so
    /// the model is only ever offered usable subagents.
    /// </summary>
    public sealed class SubagentRegistry
    {
        #region Private-Members

        private readonly List<SubagentDefinition> _Definitions = new List<SubagentDefinition>();
        private readonly Dictionary<string, SubagentDefinition> _ByName = new Dictionary<string, SubagentDefinition>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SubagentRegistry"/> class from the supplied
        /// definitions. Definitions that fail <see cref="IsValid"/> and later duplicates of an already
        /// registered name are skipped.
        /// </summary>
        /// <param name="definitions">The candidate definitions, or null for an empty registry.</param>
        public SubagentRegistry(IEnumerable<SubagentDefinition>? definitions)
        {
            if (definitions == null)
            {
                return;
            }

            foreach (SubagentDefinition definition in definitions)
            {
                if (definition == null || !IsValid(definition))
                {
                    continue;
                }

                if (_ByName.ContainsKey(definition.Name))
                {
                    continue;
                }

                SubagentDefinition copy = definition.Clone();
                _Definitions.Add(copy);
                _ByName[copy.Name] = copy;
            }
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The registered subagents in insertion order.
        /// </summary>
        public IReadOnlyList<SubagentDefinition> Definitions
        {
            get => _Definitions;
        }

        /// <summary>
        /// The number of registered subagents.
        /// </summary>
        public int Count
        {
            get => _Definitions.Count;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Finds a subagent by name (case-insensitive), or returns null when none is registered.
        /// </summary>
        /// <param name="name">The subagent name to look up.</param>
        /// <returns>The matching definition, or null.</returns>
        public SubagentDefinition? Find(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return _ByName.TryGetValue(name, out SubagentDefinition? definition) ? definition : null;
        }

        /// <summary>
        /// Determines whether a definition is usable: it has a non-empty name and a non-empty system prompt.
        /// </summary>
        /// <param name="definition">The definition to test.</param>
        /// <returns>True when the definition is valid.</returns>
        public static bool IsValid(SubagentDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(definition.Name)
                && !string.IsNullOrWhiteSpace(definition.SystemPrompt);
        }

        #endregion
    }
}
