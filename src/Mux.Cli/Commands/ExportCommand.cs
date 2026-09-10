namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Settings;
    using Mux.Core.Sessions;

    /// <summary>
    /// Implements <c>mux export</c>: renders a persisted session to a self-contained Markdown or HTML
    /// document and writes it to a file or stdout. This is the local, server-free session-sharing tier —
    /// it reads the same snapshots the session store persists and needs no running server or network.
    /// </summary>
    public sealed class ExportCommand
    {
        /// <summary>
        /// Runs the export verb.
        /// </summary>
        /// <param name="args">Arguments after the <c>export</c> verb.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The process exit code.</returns>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
        {
            string? id = null;
            string format = "md";
            string? outputPath = null;
            string? sessionsDir = null;
            bool listRequested = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--format":
                    case "-f":
                        if (i + 1 < args.Length) format = args[++i];
                        break;
                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length) outputPath = args[++i];
                        break;
                    case "--sessions-dir":
                        if (i + 1 < args.Length) sessionsDir = args[++i];
                        break;
                    case "--list":
                    case "-l":
                        listRequested = true;
                        break;
                    case "--config-dir":
                        // Consumed globally in Program before dispatch; skip its value here.
                        if (i + 1 < args.Length) i++;
                        break;
                    default:
                        if (!a.StartsWith("-", StringComparison.Ordinal) && id == null)
                        {
                            id = a;
                        }

                        break;
                }
            }

            if (!SessionExporter.TryNormalizeFormat(format, out string extension))
            {
                Console.Error.WriteLine($"mux export: unknown format '{format}'. Use 'md' or 'html'.");
                return 1;
            }

            string resolvedSessionsDir = string.IsNullOrWhiteSpace(sessionsDir)
                ? Path.Combine(SettingsLoader.GetConfigDirectory(), "sessions")
                : sessionsDir;
            SessionStore store = new SessionStore(resolvedSessionsDir);

            if (listRequested)
            {
                return await ListSessionsAsync(store, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                Console.Error.WriteLine("Usage: mux export <session-id> [--format md|html] [--output <path>] [--sessions-dir <path>]");
                Console.Error.WriteLine("       mux export --list");
                return 1;
            }

            SessionSnapshot? snapshot = await ResolveSessionAsync(store, id!, cancellationToken).ConfigureAwait(false);
            if (snapshot == null)
            {
                Console.Error.WriteLine($"mux export: no session matching '{id}' in {resolvedSessionsDir}.");
                return 1;
            }

            string document = extension == "html" ? SessionExporter.ToHtml(snapshot) : SessionExporter.ToMarkdown(snapshot);

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                Console.WriteLine(document);
                return 0;
            }

            string fullPath = Path.GetFullPath(outputPath!);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(fullPath, document, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Exported session '{snapshot.Id}' to {fullPath}");
            return 0;
        }

        private static async Task<int> ListSessionsAsync(SessionStore store, CancellationToken cancellationToken)
        {
            IReadOnlyList<SessionSnapshot> sessions = await store.ListAsync(cancellationToken).ConfigureAwait(false);
            if (sessions.Count == 0)
            {
                Console.WriteLine("No saved sessions.");
                return 0;
            }

            foreach (SessionSnapshot session in sessions)
            {
                string title = string.IsNullOrWhiteSpace(session.Title) ? session.Id : session.Title;
                Console.WriteLine($"{session.Id}\t{title}\t{session.ConversationHistory.Count} msg");
            }

            return 0;
        }

        private static async Task<SessionSnapshot?> ResolveSessionAsync(SessionStore store, string idOrTitle, CancellationToken cancellationToken)
        {
            // Try a direct id match first (the on-disk file stem), then fall back to a case-insensitive
            // title match so a human-friendly name works too.
            try
            {
                SessionSnapshot? byId = await store.LoadAsync(idOrTitle, cancellationToken).ConfigureAwait(false);
                if (byId != null)
                {
                    return byId;
                }
            }
            catch (ArgumentException)
            {
                // idOrTitle is not a valid file stem (e.g. contains spaces); fall through to a title search.
            }

            IReadOnlyList<SessionSnapshot> all = await store.ListAsync(cancellationToken).ConfigureAwait(false);
            foreach (SessionSnapshot session in all)
            {
                if (string.Equals(session.Title, idOrTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return session;
                }
            }

            return null;
        }
    }
}
