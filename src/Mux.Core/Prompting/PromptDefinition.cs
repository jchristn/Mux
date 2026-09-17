namespace Mux.Core.Prompting
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One entry in the <see cref="PromptCatalog"/>: the metadata and coded default for a single model-facing
    /// prompt. A definition is immutable — it describes what a prompt is, where its override lives, which
    /// placeholders it must keep, and what text to use when the user has not overridden it. User overrides are
    /// resolved against a definition by <see cref="PromptResolver"/>; the definition itself never carries the
    /// override.
    /// </summary>
    public sealed class PromptDefinition
    {
        /// <summary>The stable, unique key (for example <c>compaction.user</c> or <c>tool.read_file</c>). Never null or empty.</summary>
        public string Key { get; }

        /// <summary>The category used to group and label the prompt in a Prompts view.</summary>
        public PromptKind Kind { get; }

        /// <summary>Where an override for this prompt is stored, which determines how it is resolved and edited.</summary>
        public PromptScope Scope { get; }

        /// <summary>A short human label for the prompt in a Prompts view. Never null or empty.</summary>
        public string DisplayName { get; }

        /// <summary>A one-line explanation of what the prompt is used for. Never null (may be empty).</summary>
        public string Description { get; }

        /// <summary>
        /// The placeholder tokens (each written as <c>{Name}</c>) that an override MUST preserve for the prompt
        /// to work — for example <c>{WorkingDirectory}</c>. Empty when the prompt takes no placeholders. Never null.
        /// </summary>
        public IReadOnlyList<string> Placeholders { get; }

        /// <summary>The coded default text used when no override is present. Never null (may be empty).</summary>
        public string DefaultContent { get; }

        /// <summary>
        /// Creates a prompt definition.
        /// </summary>
        /// <param name="key">The stable, unique key; may not be null or whitespace.</param>
        /// <param name="kind">The category.</param>
        /// <param name="scope">Where an override is stored.</param>
        /// <param name="displayName">A short human label; may not be null or whitespace.</param>
        /// <param name="description">A one-line explanation; null becomes empty.</param>
        /// <param name="placeholders">Required placeholder tokens; null becomes an empty list.</param>
        /// <param name="defaultContent">The coded default text; null becomes empty.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> or <paramref name="displayName"/> is null or whitespace.</exception>
        public PromptDefinition(
            string key,
            PromptKind kind,
            PromptScope scope,
            string displayName,
            string? description,
            IReadOnlyList<string>? placeholders,
            string? defaultContent)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key may not be null or whitespace.", nameof(key));
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("DisplayName may not be null or whitespace.", nameof(displayName));

            Key = key;
            Kind = kind;
            Scope = scope;
            DisplayName = displayName;
            Description = description ?? string.Empty;
            Placeholders = placeholders ?? Array.Empty<string>();
            DefaultContent = defaultContent ?? string.Empty;
        }
    }
}
