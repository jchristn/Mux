namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The front-end-agnostic editing model behind the desktop keybindings editor. It layers a user override
    /// map (loaded from and saved to <c>keybindings.json</c>) over the default <see cref="KeybindingCatalog"/>,
    /// and computes each command's effective chord. A <b>rebind</b> that matches the default drops the override
    /// (so the file only records genuine deviations); an <b>unbind</b> records an explicit null; a <b>reset</b>
    /// removes any override so the command falls back to its default. The chord parsing/validation itself lives
    /// in the front end (the terminal validates against TUIKit); this model only manipulates strings.
    /// </summary>
    public sealed class KeybindingEditorModel
    {
        private readonly Dictionary<string, string?> _Overrides = new Dictionary<string, string?>(StringComparer.Ordinal);

        /// <summary>
        /// Instantiate the editor over a set of persisted overrides.
        /// </summary>
        /// <param name="overrides">The persisted command-id-to-chord overrides (null chord = unbound). Null is treated as empty.</param>
        public KeybindingEditorModel(IReadOnlyDictionary<string, string?>? overrides)
        {
            if (overrides != null)
            {
                foreach (KeyValuePair<string, string?> entry in overrides)
                {
                    if (!string.IsNullOrEmpty(entry.Key) && KeybindingCatalog.Find(entry.Key) != null)
                    {
                        _Overrides[entry.Key] = Normalize(entry.Value);
                    }
                }
            }
        }

        /// <summary>
        /// The current override map to persist (only commands that deviate from their default appear here; an
        /// unbound command appears with a null value).
        /// </summary>
        public IReadOnlyDictionary<string, string?> Overrides => _Overrides;

        /// <summary>
        /// The effective chord for a command: its override when one is recorded, otherwise the catalog default.
        /// Returns null when the command is unbound or unknown.
        /// </summary>
        /// <param name="id">The command id.</param>
        /// <returns>The effective chord, or null.</returns>
        public string? EffectiveChord(string id)
        {
            if (_Overrides.TryGetValue(id, out string? chord))
            {
                return chord;
            }

            return KeybindingCatalog.Find(id)?.DefaultChord;
        }

        /// <summary>
        /// Whether a command currently deviates from its shipped default (rebound or unbound).
        /// </summary>
        /// <param name="id">The command id.</param>
        /// <returns>True when an override is recorded for the command.</returns>
        public bool IsOverridden(string id)
        {
            return _Overrides.ContainsKey(id);
        }

        /// <summary>
        /// Rebinds a command to a chord. A null/blank chord unbinds it. When the requested chord equals the
        /// command's default, the override is dropped so the file records no needless deviation.
        /// </summary>
        /// <param name="id">The command id (must be a known catalog command; unknown ids are ignored).</param>
        /// <param name="chord">The new chord, or null/blank to unbind.</param>
        public void SetChord(string id, string? chord)
        {
            KeybindingCommand? command = KeybindingCatalog.Find(id);
            if (command == null)
            {
                return;
            }

            string? normalized = Normalize(chord);
            if (string.Equals(normalized, command.DefaultChord, StringComparison.Ordinal))
            {
                _Overrides.Remove(id);
                return;
            }

            _Overrides[id] = normalized;
        }

        /// <summary>
        /// Unbinds a command (records an explicit null override), even if its default is already null — this
        /// makes the intent explicit in the persisted file.
        /// </summary>
        /// <param name="id">The command id (must be a known catalog command; unknown ids are ignored).</param>
        public void Unbind(string id)
        {
            KeybindingCommand? command = KeybindingCatalog.Find(id);
            if (command == null)
            {
                return;
            }

            if (command.DefaultChord == null)
            {
                // Default is already unbound; recording nothing keeps the file clean.
                _Overrides.Remove(id);
                return;
            }

            _Overrides[id] = null;
        }

        /// <summary>
        /// Resets a command to its shipped default by removing any override.
        /// </summary>
        /// <param name="id">The command id.</param>
        public void Reset(string id)
        {
            _Overrides.Remove(id);
        }

        private static string? Normalize(string? chord)
        {
            if (string.IsNullOrWhiteSpace(chord))
            {
                return null;
            }

            return chord.Trim().ToLowerInvariant();
        }
    }
}
