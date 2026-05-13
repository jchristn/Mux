namespace SearchConsoleShared
{
    using System;

    /// <summary>
    /// Minimal command-line argument helper for the interactive console apps.
    /// </summary>
    public static class ConsoleArguments
    {
        /// <summary>
        /// Gets an argument value from either <c>--name value</c> or <c>--name=value</c> syntax.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <param name="names">Permitted argument names.</param>
        /// <returns>The resolved value, or null if not supplied.</returns>
        public static string? GetValue(string[] args, params string[] names)
        {
            if (args is null || names is null || names.Length < 1)
            {
                return null;
            }

            for (int i = 0; i < args.Length; i++)
            {
                foreach (string name in names)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return i + 1 < args.Length ? args[i + 1] : null;
                    }

                    string prefix = name + "=";
                    if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return args[i][prefix.Length..];
                    }
                }
            }

            return null;
        }
    }
}
