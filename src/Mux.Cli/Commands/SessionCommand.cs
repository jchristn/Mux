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
    /// Implements <c>mux session</c>: non-interactive management of a persisted session's labels and tags,
    /// so scripts and headless callers can annotate sessions without a slash command. All mutation flows
    /// through the shared <see cref="SessionManager"/>, so normalization and dedupe match every other surface.
    ///
    /// <para>Usage:</para>
    /// <list type="bullet">
    /// <item><c>mux session &lt;id&gt; show</c> — print the session's labels and tags.</item>
    /// <item><c>mux session &lt;id&gt; label &lt;text&gt;</c> / <c>unlabel &lt;text&gt;</c></item>
    /// <item><c>mux session &lt;id&gt; tag &lt;key:value&gt;</c> / <c>untag &lt;key&gt;</c></item>
    /// <item><c>mux session --list</c> — list sessions with their metadata.</item>
    /// </list>
    /// </summary>
    public sealed class SessionCommand
    {
        /// <summary>
        /// Runs the session verb.
        /// </summary>
        /// <param name="args">Arguments after the <c>session</c> verb.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The process exit code.</returns>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
        {
            string? sessionsDir = null;
            bool json = false;
            List<string> positional = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--sessions-dir":
                        if (i + 1 < args.Length) sessionsDir = args[++i];
                        break;
                    case "--json":
                        json = true;
                        break;
                    case "--config-dir":
                        if (i + 1 < args.Length) i++; // consumed globally
                        break;
                    default:
                        positional.Add(a);
                        break;
                }
            }

            string resolvedSessionsDir = string.IsNullOrWhiteSpace(sessionsDir)
                ? Path.Combine(SettingsLoader.GetConfigDirectory(), "sessions")
                : sessionsDir;
            SessionStore store = new SessionStore(resolvedSessionsDir);
            SessionManager manager = new SessionManager(store);

            if (positional.Count == 1 && string.Equals(positional[0], "--list", StringComparison.OrdinalIgnoreCase))
            {
                return await ListAsync(manager, cancellationToken).ConfigureAwait(false);
            }

            if (positional.Count < 2)
            {
                PrintUsage();
                return 1;
            }

            string id = positional[0];
            string verb = positional[1].ToLowerInvariant();
            string rest = positional.Count > 2 ? string.Join(" ", positional.GetRange(2, positional.Count - 2)) : string.Empty;

            try
            {
                switch (verb)
                {
                    case "show":
                        return await ShowAsync(store, id, json, cancellationToken).ConfigureAwait(false);
                    case "label":
                        return await ApplyAsync(await manager.AddLabelAsync(id, rest, cancellationToken).ConfigureAwait(false), id, json);
                    case "unlabel":
                        return await ApplyAsync(await manager.RemoveLabelAsync(id, rest, cancellationToken).ConfigureAwait(false), id, json);
                    case "tag":
                    {
                        if (!SessionMetadataNormalizer.TryParseTag(rest, out SessionTag? tag, out string? err) || tag == null)
                        {
                            Console.Error.WriteLine("mux session: " + (err ?? "invalid tag; use key:value."));
                            return 1;
                        }

                        return await ApplyAsync(await manager.SetTagAsync(id, tag.Key, tag.Value, cancellationToken).ConfigureAwait(false), id, json);
                    }
                    case "untag":
                        return await ApplyAsync(await manager.RemoveTagAsync(id, rest, cancellationToken).ConfigureAwait(false), id, json);
                    default:
                        PrintUsage();
                        return 1;
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("mux session: " + ex.Message);
                return 1;
            }
        }

        private static Task<int> ApplyAsync(SessionInfo? info, string id, bool json)
        {
            if (info == null)
            {
                Console.Error.WriteLine("mux session: no session with id '" + id + "'.");
                return Task.FromResult(1);
            }

            PrintMetadata(info.Id, info.Labels, info.Tags, json);
            return Task.FromResult(0);
        }

        private static async Task<int> ShowAsync(SessionStore store, string id, bool json, CancellationToken cancellationToken)
        {
            SessionSnapshot? snapshot = await store.LoadAsync(id, cancellationToken).ConfigureAwait(false);
            if (snapshot == null)
            {
                Console.Error.WriteLine("mux session: no session with id '" + id + "'.");
                return 1;
            }

            PrintMetadata(snapshot.Id, snapshot.Labels, snapshot.Tags, json);
            return 0;
        }

        private static async Task<int> ListAsync(SessionManager manager, CancellationToken cancellationToken)
        {
            IReadOnlyList<SessionInfo> sessions = await manager.ListAsync(cancellationToken).ConfigureAwait(false);
            if (sessions.Count == 0)
            {
                Console.WriteLine("No saved sessions.");
                return 0;
            }

            foreach (SessionInfo session in sessions)
            {
                string labels = session.Labels.Count == 0 ? "-" : string.Join(",", session.Labels);
                List<string> tagParts = new List<string>();
                foreach (SessionTag tag in session.Tags) tagParts.Add(tag.Key + ":" + tag.Value);
                string tags = tagParts.Count == 0 ? "-" : string.Join(",", tagParts);
                Console.WriteLine(session.Id + "\t" + labels + "\t" + tags);
            }

            return 0;
        }

        private static void PrintMetadata(string id, IReadOnlyList<string> labels, IReadOnlyList<SessionTag> tags, bool json)
        {
            if (json)
            {
                System.Text.StringBuilder builder = new System.Text.StringBuilder();
                builder.Append("{\"id\":").Append(JsonString(id)).Append(",\"labels\":[");
                for (int i = 0; i < labels.Count; i++)
                {
                    if (i > 0) builder.Append(',');
                    builder.Append(JsonString(labels[i]));
                }

                builder.Append("],\"tags\":[");
                for (int i = 0; i < tags.Count; i++)
                {
                    if (i > 0) builder.Append(',');
                    builder.Append("{\"key\":").Append(JsonString(tags[i].Key)).Append(",\"value\":").Append(JsonString(tags[i].Value)).Append('}');
                }

                builder.Append("]}");
                Console.WriteLine(builder.ToString());
                return;
            }

            Console.WriteLine("Labels: " + (labels.Count == 0 ? "(none)" : string.Join(", ", labels)));
            List<string> parts = new List<string>();
            foreach (SessionTag tag in tags) parts.Add(tag.Key + ": " + tag.Value);
            Console.WriteLine("Tags: " + (parts.Count == 0 ? "(none)" : string.Join(", ", parts)));
        }

        private static string JsonString(string value)
        {
            return System.Text.Json.JsonSerializer.Serialize(value);
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("Usage: mux session <id> show");
            Console.Error.WriteLine("       mux session <id> label <text>    |  unlabel <text>");
            Console.Error.WriteLine("       mux session <id> tag <key:value> |  untag <key>");
            Console.Error.WriteLine("       mux session --list");
            Console.Error.WriteLine("Options: --sessions-dir <path>  --json");
        }
    }
}
