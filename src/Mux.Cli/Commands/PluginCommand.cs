namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Plugins;
    using Mux.Core.Settings;

    /// <summary>
    /// Implements <c>mux plugin</c>: inspects the configured event hooks and custom commands from
    /// <c>~/.mux/hooks.json</c>. Read-only — it never runs a hook or command, so automation can audit the
    /// plugin surface safely.
    /// </summary>
    public sealed class PluginCommand
    {
        /// <summary>
        /// Runs the plugin verb.
        /// </summary>
        /// <param name="args">Arguments after the <c>plugin</c> verb.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The process exit code.</returns>
        public Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
        {
            string action = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "list";
            bool json = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--output-format", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    json = string.Equals(args[i + 1], "json", StringComparison.OrdinalIgnoreCase);
                }
            }

            if (action != "list")
            {
                Console.Error.WriteLine("Usage: mux plugin list [--output-format json]");
                return Task.FromResult(1);
            }

            PluginRegistry registry = new PluginRegistry(SettingsLoader.LoadPluginConfig());

            if (json)
            {
                List<object> hooks = new List<object>();
                foreach (HookDefinition hook in registry.Hooks)
                {
                    hooks.Add(new
                    {
                        name = hook.Name,
                        @event = HookEventEnumConverter.ToWireName(hook.Event),
                        command = hook.Command,
                        args = hook.Args,
                        blocking = hook.Blocking,
                        timeoutMs = hook.TimeoutMs
                    });
                }

                List<object> commands = new List<object>();
                foreach (CustomCommandDefinition command in registry.Commands)
                {
                    commands.Add(new
                    {
                        name = command.Name,
                        description = command.Description,
                        command = command.Command,
                        args = command.Args,
                        timeoutMs = command.TimeoutMs
                    });
                }

                Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = true, hooks, commands }));
                return Task.FromResult(0);
            }

            if (registry.IsEmpty)
            {
                Console.WriteLine("No hooks or custom commands configured. Edit " + SettingsLoader.GetConfigDirectory() + "/hooks.json to add some.");
                return Task.FromResult(0);
            }

            Console.WriteLine("Hooks:");
            if (registry.Hooks.Count == 0)
            {
                Console.WriteLine("  (none)");
            }
            else
            {
                foreach (HookDefinition hook in registry.Hooks)
                {
                    string label = string.IsNullOrWhiteSpace(hook.Name) ? hook.Command : hook.Name;
                    string blocking = hook.Blocking ? " [blocking]" : string.Empty;
                    Console.WriteLine($"  {HookEventEnumConverter.ToWireName(hook.Event)}\t{label}{blocking} — {hook.Command} {string.Join(' ', hook.Args)}");
                }
            }

            Console.WriteLine("Custom commands:");
            if (registry.Commands.Count == 0)
            {
                Console.WriteLine("  (none)");
            }
            else
            {
                foreach (CustomCommandDefinition command in registry.Commands)
                {
                    Console.WriteLine($"  /{command.Name}\t{command.Command} {string.Join(' ', command.Args)} — {command.Description}");
                }
            }

            return Task.FromResult(0);
        }
    }
}
