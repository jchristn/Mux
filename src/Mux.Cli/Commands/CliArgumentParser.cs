namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    /// <summary>
    /// Small parser for mux's documented command-line surface.
    /// </summary>
    internal static class CliArgumentParser
    {
        public static InteractiveSettings ParseInteractive(string[] args)
        {
            InteractiveSettings settings = new InteractiveSettings();
            ParseCommon(args, settings, null);
            return settings;
        }

        public static PrintSettings ParsePrint(string[] args)
        {
            PrintSettings settings = new PrintSettings();
            List<string> positionals = ParseCommon(args, settings, (string option, string? inlineValue, string[] values, ref int index) =>
            {
                switch (option)
                {
                    case "-p":
                    case "--print":
                        settings.Print = ReadBool(option, inlineValue, defaultValue: true);
                        return true;
                    case "--output-last-message":
                        settings.OutputLastMessage = ReadValue(option, inlineValue, values, ref index);
                        return true;
                    case "--resume":
                        settings.Resume = ReadValue(option, inlineValue, values, ref index);
                        return true;
                    case "--continue":
                        settings.Continue = ReadBool(option, inlineValue, defaultValue: true);
                        return true;
                    case "--session-id":
                        settings.SessionId = ReadValue(option, inlineValue, values, ref index);
                        return true;
                    case "--fork-session":
                        settings.ForkSession = ReadBool(option, inlineValue, defaultValue: true);
                        return true;
                    case "--no-session-persistence":
                        settings.NoSessionPersistence = ReadBool(option, inlineValue, defaultValue: true);
                        return true;
                    case "--output-schema":
                        settings.OutputSchema = ReadValue(option, inlineValue, values, ref index);
                        return true;
                    default:
                        return false;
                }
            });

            settings.Prompt = JoinPositionals(positionals);
            return settings;
        }

        public static ProbeSettings ParseProbe(string[] args)
        {
            ProbeSettings settings = new ProbeSettings();
            ParseCommon(args, settings, (string option, string? inlineValue, string[] values, ref int index) =>
            {
                switch (option)
                {
                    case "--probe-prompt":
                        settings.ProbePrompt = ReadValue(option, inlineValue, values, ref index);
                        return true;
                    case "--require-tools":
                        settings.RequireTools = ReadBool(option, inlineValue, defaultValue: true);
                        return true;
                    default:
                        return false;
                }
            });

            return settings;
        }

        public static EndpointSettings ParseEndpoint(string[] args)
        {
            EndpointSettings settings = new EndpointSettings();
            List<string> positionals = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--")
                {
                    positionals.AddRange(args.Skip(i + 1));
                    break;
                }

                SplitOption(arg, out string option, out string? inlineValue);
                switch (option)
                {
                    case "--output-format":
                        settings.OutputFormat = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--config-dir":
                        settings.ConfigDir = ReadValue(option, inlineValue, args, ref i);
                        break;
                    default:
                        if (IsOption(arg))
                        {
                            throw new InvalidOperationException($"Unknown endpoint option '{arg}'.");
                        }

                        positionals.Add(arg);
                        break;
                }
            }

            if (positionals.Count > 0)
            {
                settings.Action = positionals[0];
            }

            if (positionals.Count > 1)
            {
                settings.Name = positionals[1];
            }

            return settings;
        }

        /// <summary>
        /// Parses the arguments for the <c>mux skill</c> verb.
        /// </summary>
        /// <param name="args">The verb arguments (excluding the leading <c>skill</c>).</param>
        /// <returns>The populated <see cref="SkillSettings"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when an unknown option is supplied.</exception>
        public static SkillSettings ParseSkill(string[] args)
        {
            SkillSettings settings = new SkillSettings();
            List<string> positionals = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--")
                {
                    positionals.AddRange(args.Skip(i + 1));
                    break;
                }

                SplitOption(arg, out string option, out string? inlineValue);
                switch (option)
                {
                    case "--output-format":
                        settings.OutputFormat = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--config-dir":
                        settings.ConfigDir = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--cwd":
                        settings.WorkingDirectory = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--arg":
                        settings.Args.Add(ReadValue(option, inlineValue, args, ref i));
                        break;
                    default:
                        if (IsOption(arg))
                        {
                            throw new InvalidOperationException($"Unknown skill option '{arg}'.");
                        }

                        positionals.Add(arg);
                        break;
                }
            }

            if (positionals.Count > 0)
            {
                settings.Action = positionals[0];
            }

            if (positionals.Count > 1)
            {
                settings.Name = positionals[1];
            }

            if (positionals.Count > 2)
            {
                settings.Command = positionals[2];
            }

            return settings;
        }

        private delegate bool ExtraOptionHandler(string option, string? inlineValue, string[] args, ref int index);

        private static List<string> ParseCommon(string[] args, CommonSettings settings, ExtraOptionHandler? extraHandler)
        {
            List<string> positionals = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--")
                {
                    positionals.AddRange(args.Skip(i + 1));
                    break;
                }

                SplitOption(arg, out string option, out string? inlineValue);
                if (extraHandler != null && extraHandler(option, inlineValue, args, ref i))
                {
                    continue;
                }

                switch (option)
                {
                    case "--config-dir":
                        settings.ConfigDir = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "-e":
                    case "--endpoint":
                        settings.Endpoint = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "-m":
                    case "--model":
                        settings.Model = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--base-url":
                        settings.BaseUrl = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--adapter-type":
                        settings.AdapterType = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--temperature":
                        settings.Temperature = double.Parse(ReadValue(option, inlineValue, args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--max-tokens":
                        settings.MaxTokens = int.Parse(ReadValue(option, inlineValue, args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--effort":
                        settings.Effort = ReadEffort(option, ReadValue(option, inlineValue, args, ref i));
                        break;
                    case "--effort-openai-value":
                        settings.EffortOpenAiValue = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--effort-gemini-budget":
                        settings.EffortGeminiBudget = int.Parse(ReadValue(option, inlineValue, args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--effort-ollama-think":
                        settings.EffortOllamaThink = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "-w":
                    case "--working-directory":
                        settings.WorkingDirectory = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--system-prompt":
                        settings.SystemPrompt = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--yolo":
                        settings.Yolo = ReadBool(option, inlineValue, defaultValue: true);
                        break;
                    case "--approval-policy":
                        settings.ApprovalPolicy = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "-v":
                    case "--verbose":
                        settings.Verbose = ReadBool(option, inlineValue, defaultValue: true);
                        break;
                    case "--no-mcp":
                        settings.NoMcp = ReadBool(option, inlineValue, defaultValue: true);
                        break;
                    case "--mcp-config":
                        settings.McpConfig = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--strict-mcp-config":
                        settings.StrictMcpConfig = ReadBool(option, inlineValue, defaultValue: true);
                        break;
                    case "--output-format":
                        settings.OutputFormat = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--input-format":
                        settings.InputFormat = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--compaction-strategy":
                        settings.CompactionStrategy = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--max-turns":
                        settings.MaxTurns = int.Parse(ReadValue(option, inlineValue, args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--append-system-prompt":
                        settings.AppendSystemPrompt = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--max-token-budget":
                        settings.MaxTokenBudget = int.Parse(ReadValue(option, inlineValue, args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--allow-tools":
                        settings.AllowTools = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--deny-tools":
                        settings.DenyTools = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--add-dir":
                        settings.AddDir.Add(ReadValue(option, inlineValue, args, ref i));
                        break;
                    case "--sandbox":
                        settings.Sandbox = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--ignore-cert-errors":
                    case "--insecure":
                        settings.IgnoreCertErrors = ReadBool(option, inlineValue, defaultValue: true);
                        break;
                    default:
                        if (IsOption(arg))
                        {
                            throw new InvalidOperationException($"Unknown option '{arg}'.");
                        }

                        positionals.Add(arg);
                        break;
                }
            }

            return positionals;
        }

        private static void SplitOption(string arg, out string option, out string? inlineValue)
        {
            int equalsIndex = arg.IndexOf('=');
            if (equalsIndex > 0 && arg.StartsWith("-", StringComparison.Ordinal))
            {
                option = arg.Substring(0, equalsIndex);
                inlineValue = arg.Substring(equalsIndex + 1);
                return;
            }

            option = arg;
            inlineValue = null;
        }

        private static string ReadValue(string option, string? inlineValue, string[] args, ref int index)
        {
            if (inlineValue != null)
            {
                return inlineValue;
            }

            if (index + 1 >= args.Length)
            {
                throw new InvalidOperationException($"Option '{option}' requires a value.");
            }

            index++;
            return args[index];
        }

        private static bool ReadBool(string option, string? inlineValue, bool defaultValue)
        {
            if (inlineValue == null)
            {
                return defaultValue;
            }

            if (bool.TryParse(inlineValue, out bool parsed))
            {
                return parsed;
            }

            switch (inlineValue.Trim().ToLowerInvariant())
            {
                case "1":
                case "yes":
                case "on":
                    return true;
                case "0":
                case "no":
                case "off":
                    return false;
                default:
                    throw new InvalidOperationException($"Option '{option}' expects a boolean value.");
            }
        }

        private static string ReadEffort(string option, string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "off":
                case "none":
                case "minimal":
                case "low":
                case "medium":
                case "high":
                    return normalized;
                default:
                    throw new InvalidOperationException($"Option '{option}' expects one of: off, minimal, low, medium, high.");
            }
        }

        private static bool IsOption(string arg)
        {
            return arg.Length > 1 && arg.StartsWith("-", StringComparison.Ordinal);
        }

        private static string? JoinPositionals(List<string> positionals)
        {
            return positionals.Count == 0
                ? null
                : string.Join(" ", positionals);
        }
    }
}
