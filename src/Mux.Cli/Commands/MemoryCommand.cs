namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Memory;

    /// <summary>
    /// Implements <c>mux memory</c>: list, show, add, and delete persistent memories for the current directory's
    /// project (and the global scope) without starting a session.
    /// </summary>
    public sealed class MemoryCommand
    {
        #region Private-Members

        private const string Usage = "Usage: mux memory list [--query <words>] [--output-format json] | show <name> | add <text> [--name <name>] [--global] | delete <name> [--global] [--cwd <dir>]";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Runs the memory verb.
        /// </summary>
        /// <param name="args">Arguments after the <c>memory</c> verb.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The process exit code: 0 success, 1 not found or invalid input.</returns>
        public Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
        {
            string action = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "list";
            List<string> positional = new List<string>();
            string? cwd = null;
            string? query = null;
            string? name = null;
            bool global = false;
            bool json = false;
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--cwd" && i + 1 < args.Length) cwd = args[++i];
                else if (arg == "--query" && i + 1 < args.Length) query = args[++i];
                else if (arg == "--name" && i + 1 < args.Length) name = args[++i];
                else if (arg == "--global") global = true;
                else if (arg == "--output-format" && i + 1 < args.Length) json = string.Equals(args[++i], "json", StringComparison.OrdinalIgnoreCase);
                else positional.Add(arg);
            }

            string directory = string.IsNullOrWhiteSpace(cwd) ? Directory.GetCurrentDirectory() : Path.GetFullPath(cwd!);
            MemoryStore store = MemoryStore.FromConfigDirectory();
            MemoryScopeEnum scope = global ? MemoryScopeEnum.Global : MemoryScopeEnum.Project;
            try
            {
                switch (action)
                {
                    case "list":
                    case "ls":
                        List<MemoryEntry> entries = store.Search(query, directory);
                        if (json)
                        {
                            List<object> rows = entries.ConvertAll(e => (object)new { name = e.Slug, scope = ScopeName(e.Scope), description = e.Description, content = e.Content, updatedUtc = e.UpdatedUtc });
                            Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = true, projectKey = MemoryStore.ProjectKey(directory), memories = rows }));
                            return Task.FromResult(0);
                        }

                        if (entries.Count == 0)
                        {
                            Console.WriteLine("No memories" + (string.IsNullOrWhiteSpace(query) ? "." : " match '" + query + "'."));
                            return Task.FromResult(0);
                        }

                        foreach (MemoryEntry entry in entries)
                        {
                            Console.WriteLine("[" + ScopeName(entry.Scope) + "] " + entry.Slug + ": " + entry.Description);
                        }

                        return Task.FromResult(0);
                    case "show":
                        MemoryEntry? found = positional.Count > 0 ? store.Get(positional[0], global ? scope : (MemoryScopeEnum?)null, directory) : null;
                        if (found == null)
                        {
                            Console.Error.WriteLine(positional.Count == 0 ? Usage : "No memory named '" + positional[0] + "'.");
                            return Task.FromResult(1);
                        }

                        Console.WriteLine(found.Slug + " (" + ScopeName(found.Scope) + ")");
                        Console.WriteLine(found.Description);
                        Console.WriteLine();
                        Console.WriteLine(found.Content);
                        return Task.FromResult(0);
                    case "add":
                        string text = string.Join(" ", positional).Trim();
                        if (text.Length == 0)
                        {
                            Console.Error.WriteLine(Usage);
                            return Task.FromResult(1);
                        }

                        MemoryEntry saved = store.Save(string.IsNullOrWhiteSpace(name) ? MemoryStore.NameFromText(text) : name!, text, text, scope, directory, out bool created);
                        Console.WriteLine((created ? "Remembered " : "Updated ") + ScopeName(saved.Scope) + " memory '" + saved.Slug + "'.");
                        return Task.FromResult(0);
                    case "delete":
                    case "rm":
                        MemoryEntry? deleted = positional.Count > 0 ? store.Delete(positional[0], global ? scope : (MemoryScopeEnum?)null, directory) : null;
                        if (deleted == null)
                        {
                            Console.Error.WriteLine(positional.Count == 0 ? Usage : "No memory named '" + positional[0] + "'.");
                            return Task.FromResult(1);
                        }

                        Console.WriteLine("Deleted " + ScopeName(deleted.Scope) + " memory '" + deleted.Slug + "'.");
                        return Task.FromResult(0);
                    default:
                        Console.Error.WriteLine(Usage);
                        return Task.FromResult(1);
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return Task.FromResult(1);
            }
        }

        #endregion

        #region Private-Methods

        private static string ScopeName(MemoryScopeEnum scope)
        {
            return scope == MemoryScopeEnum.Global ? "global" : "project";
        }

        #endregion
    }
}
