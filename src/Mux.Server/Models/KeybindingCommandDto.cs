namespace Mux.Server.Models
{
    /// <summary>
    /// A bindable command with its shipped default chord and the chord in effect after the user's overrides.
    /// </summary>
    public class KeybindingCommandDto
    {
        /// <summary>The command id (for example <c>mux.save</c>).</summary>
        public string CommandId { get; set; } = string.Empty;

        /// <summary>The command's display title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>The command's group (Session, Model, View, Help).</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>The shipped default chord, or null when unbound by default.</summary>
        public string? DefaultChord { get; set; }

        /// <summary>The chord in effect (the override when one is recorded, else the default), or null when unbound.</summary>
        public string? EffectiveChord { get; set; }

        /// <summary>Whether an override is recorded for the command (rebound or unbound).</summary>
        public bool Overridden { get; set; }
    }
}
