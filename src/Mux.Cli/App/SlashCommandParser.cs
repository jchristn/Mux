namespace Mux.Cli.App
{
    using System;

    /// <summary>
    /// Resolves composer input that begins with <c>/</c> against the command catalog's slash aliases and
    /// invokes the matching command. The leading token (after the slash) selects the command; any
    /// remaining text is treated as arguments (unused by the current parameterless commands, reserved
    /// for later). This is the router the composer's slash hook delegates to.
    /// </summary>
    public sealed class SlashCommandParser
    {
        #region Private-Members

        private readonly MuxCommandCatalog _Catalog;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SlashCommandParser"/> class.
        /// </summary>
        /// <param name="catalog">The command catalog to resolve against. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="catalog"/> is null.</exception>
        public SlashCommandParser(MuxCommandCatalog catalog)
        {
            _Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolves slash input to a command without invoking it.
        /// </summary>
        /// <param name="input">The raw composer input (with or without the leading slash).</param>
        /// <returns>The matching descriptor, or null when no alias matches.</returns>
        public CommandDescriptor? Resolve(string input)
        {
            string token = ExtractToken(input);
            if (token.Length == 0)
            {
                return null;
            }

            foreach (CommandDescriptor descriptor in _Catalog.Commands)
            {
                if (descriptor.MatchesSlash(token))
                {
                    return descriptor;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves and invokes the command for the given slash input.
        /// </summary>
        /// <param name="input">The raw composer input (with or without the leading slash).</param>
        /// <returns>True when a command matched and was invoked; false otherwise.</returns>
        public bool TryHandle(string input)
        {
            CommandDescriptor? descriptor = Resolve(input);
            if (descriptor == null)
            {
                return false;
            }

            descriptor.Handler();
            return true;
        }

        #endregion

        #region Private-Methods

        private static string ExtractToken(string input)
        {
            string trimmed = (input ?? string.Empty).Trim();
            if (trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1);
            }

            int space = trimmed.IndexOfAny(new[] { ' ', '\t' });
            return space < 0 ? trimmed : trimmed.Substring(0, space);
        }

        #endregion
    }
}
