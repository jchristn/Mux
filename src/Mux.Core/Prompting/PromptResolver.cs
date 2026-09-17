namespace Mux.Core.Prompting
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Settings;

    /// <summary>
    /// The single read path for model-facing prompts: given a catalog key it returns the effective text
    /// (a stored override when present, otherwise the catalog default) and substitutes any runtime
    /// placeholders. Global (operational) overrides come from the <c>operational</c> map of
    /// <c>prompts.json</c>; profile-scoped persona prompts continue to resolve through the existing
    /// persona path (<see cref="SystemPromptResolver"/> + <see cref="SettingsLoader.LoadSystemPrompt"/>), so
    /// this resolver returns their catalog default. An instance holds an immutable override snapshot; the
    /// process-wide <see cref="Shared"/> instance caches the snapshot and is rebuilt by <see cref="Invalidate"/>.
    /// </summary>
    public sealed class PromptResolver
    {
        private readonly IReadOnlyDictionary<string, string> _Overrides;

        private static readonly object _SharedSync = new object();
        private static PromptResolver? _Shared;

        /// <summary>
        /// Creates a resolver over an override snapshot.
        /// </summary>
        /// <param name="overrides">The operational override map (key → override text); null is treated as empty.</param>
        public PromptResolver(IReadOnlyDictionary<string, string>? overrides)
        {
            _Overrides = overrides ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Gets the process-wide resolver, loading operational overrides from <c>prompts.json</c> once and
        /// caching them. Loading never throws — a missing or malformed override map resolves to defaults —
        /// because this instance is used by paths (such as tool description getters) that must not fail.
        /// Call <see cref="Invalidate"/> after saving an override so the next access reloads.
        /// </summary>
        public static PromptResolver Shared
        {
            get
            {
                PromptResolver? current = _Shared;
                if (current != null) return current;

                lock (_SharedSync)
                {
                    if (_Shared == null)
                    {
                        _Shared = new PromptResolver(SettingsLoader.LoadOperationalPrompts());
                    }

                    return _Shared;
                }
            }
        }

        /// <summary>
        /// Discards the cached <see cref="Shared"/> instance so the next access reloads overrides from disk.
        /// </summary>
        public static void Invalidate()
        {
            lock (_SharedSync)
            {
                _Shared = null;
            }
        }

        /// <summary>
        /// Returns whether a non-empty override is stored for a key.
        /// </summary>
        /// <param name="key">The catalog key.</param>
        /// <returns>True when a non-empty override is present; otherwise false.</returns>
        public bool IsOverridden(string key)
        {
            return key != null && _Overrides.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value);
        }

        /// <summary>
        /// Returns the effective text for a key — the stored override when present and non-empty, otherwise the
        /// catalog default. Returns an empty string for an unknown key; never throws.
        /// </summary>
        /// <param name="key">The catalog key.</param>
        /// <returns>The effective text; never null.</returns>
        public string GetEffective(string key)
        {
            if (key != null && _Overrides.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
            {
                return value!;
            }

            return PromptCatalog.DefaultFor(key ?? string.Empty);
        }

        /// <summary>
        /// Returns the effective text for a key with runtime placeholders substituted. Each entry in
        /// <paramref name="substitutions"/> replaces every occurrence of its key (for example
        /// <c>{ToolName}</c>) with its value.
        /// </summary>
        /// <param name="key">The catalog key.</param>
        /// <param name="substitutions">Placeholder token → replacement value; null applies none.</param>
        /// <returns>The resolved text; never null.</returns>
        public string Resolve(string key, IReadOnlyDictionary<string, string>? substitutions)
        {
            string text = GetEffective(key);
            if (substitutions != null)
            {
                foreach (KeyValuePair<string, string> pair in substitutions)
                {
                    text = text.Replace(pair.Key, pair.Value ?? string.Empty);
                }
            }

            return text;
        }

        /// <summary>
        /// Validates a candidate override for a key: every placeholder the definition requires must still be
        /// present in the content.
        /// </summary>
        /// <param name="key">The catalog key; must be a known key.</param>
        /// <param name="content">The candidate override text; null is treated as empty.</param>
        /// <param name="missingPlaceholders">The required placeholders absent from the content; never null.</param>
        /// <returns>True when the content preserves every required placeholder; otherwise false.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is not a known catalog key.</exception>
        public static bool TryValidateOverride(string key, string? content, out IReadOnlyList<string> missingPlaceholders)
        {
            if (!PromptCatalog.TryGet(key, out PromptDefinition? definition) || definition == null)
            {
                throw new ArgumentException("Unknown prompt key: " + (key ?? "(null)"), nameof(key));
            }

            List<string> missing = new List<string>();
            string text = content ?? string.Empty;
            foreach (string placeholder in definition.Placeholders)
            {
                if (!text.Contains(placeholder))
                {
                    missing.Add(placeholder);
                }
            }

            missingPlaceholders = missing;
            return missing.Count == 0;
        }

        /// <summary>
        /// Stores an operational override for a global-scoped key after validating its placeholders, then
        /// invalidates the shared resolver so the change takes effect.
        /// </summary>
        /// <param name="key">The catalog key; must be a known, global-scoped key.</param>
        /// <param name="content">The override text; may not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the key is unknown, is not global-scoped, or the content drops a required placeholder.</exception>
        public static void SetOverride(string key, string content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (!PromptCatalog.TryGet(key, out PromptDefinition? definition) || definition == null)
            {
                throw new ArgumentException("Unknown prompt key: " + (key ?? "(null)"), nameof(key));
            }

            if (definition!.Scope != PromptScope.Global)
            {
                throw new ArgumentException("Only global-scoped prompts have operational overrides; '" + key + "' is " + definition.Scope + "-scoped.", nameof(key));
            }

            if (!TryValidateOverride(key, content, out IReadOnlyList<string> missing))
            {
                throw new ArgumentException("Override for '" + key + "' is missing required placeholder(s): " + string.Join(", ", missing), nameof(content));
            }

            Dictionary<string, string> overrides = SettingsLoader.LoadOperationalPrompts();
            overrides[key] = content;
            SettingsLoader.SaveOperationalPrompts(overrides);
            Invalidate();
        }

        /// <summary>
        /// Clears any operational override for a key, restoring the catalog default, then invalidates the
        /// shared resolver. Removing an absent key is a no-op.
        /// </summary>
        /// <param name="key">The catalog key.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null or whitespace.</exception>
        public static void ResetToDefault(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key may not be null or whitespace.", nameof(key));

            Dictionary<string, string> overrides = SettingsLoader.LoadOperationalPrompts();
            if (overrides.Remove(key))
            {
                SettingsLoader.SaveOperationalPrompts(overrides);
                Invalidate();
            }
        }
    }
}
