namespace Mux.Desktop.Prompting
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;

    /// <summary>
    /// The desktop view-model over the operational-prompt catalog. It projects <see cref="PromptCatalog"/>
    /// entries into <see cref="PromptCatalogRow"/> rows (sorted by kind then display name, with the current
    /// override state layered on), and mediates edits and resets through <see cref="PromptResolver"/>. All
    /// state derives from disk, so it is safe to reload after any change. The Avalonia view binds to
    /// <see cref="Rows"/> and calls <see cref="TrySetOverride"/> / <see cref="Reset"/>; this class holds no UI.
    /// </summary>
    public sealed class PromptCatalogViewModel
    {
        private readonly List<PromptCatalogRow> _Rows = new List<PromptCatalogRow>();

        /// <summary>
        /// Initializes the view-model and loads the current catalog state from disk.
        /// </summary>
        public PromptCatalogViewModel()
        {
            Reload();
        }

        /// <summary>
        /// Gets the catalog rows, sorted by kind then display name. Rebuilt by <see cref="Reload"/>.
        /// </summary>
        public IReadOnlyList<PromptCatalogRow> Rows
        {
            get { return _Rows; }
        }

        /// <summary>
        /// Reloads every row from the catalog and the current operational overrides on disk.
        /// </summary>
        public void Reload()
        {
            PromptResolver resolver = new PromptResolver(SettingsLoader.LoadOperationalPrompts());
            _Rows.Clear();
            foreach (PromptDefinition definition in PromptCatalog.All)
            {
                _Rows.Add(new PromptCatalogRow
                {
                    Key = definition.Key,
                    Kind = definition.Kind.ToWireString(),
                    Scope = definition.Scope.ToString(),
                    DisplayName = definition.DisplayName,
                    Description = definition.Description,
                    Placeholders = new List<string>(definition.Placeholders),
                    Default = definition.DefaultContent,
                    Effective = resolver.GetEffective(definition.Key),
                    Overridden = resolver.IsOverridden(definition.Key),
                    Editable = definition.Scope == PromptScope.Global
                });
            }

            _Rows.Sort((left, right) =>
            {
                int byKind = string.Compare(left.Kind, right.Kind, StringComparison.Ordinal);
                return byKind != 0 ? byKind : string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
            });
        }

        /// <summary>
        /// Attempts to store an operational override for a key, then reloads on success. Placeholder-validation
        /// and scope failures are reported rather than thrown.
        /// </summary>
        /// <param name="key">The catalog key; must be a known, global-scoped key.</param>
        /// <param name="content">The override text; may not be null.</param>
        /// <param name="error">The failure message when the return value is false; otherwise empty.</param>
        /// <returns>True when the override was stored; false when it was rejected.</returns>
        public bool TrySetOverride(string key, string content, out string error)
        {
            error = string.Empty;
            try
            {
                PromptResolver.SetOverride(key, content ?? string.Empty);
                Reload();
                return true;
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Clears any operational override for a key, restoring the default, then reloads.
        /// </summary>
        /// <param name="key">The catalog key.</param>
        public void Reset(string key)
        {
            PromptResolver.ResetToDefault(key);
            Reload();
        }
    }
}
