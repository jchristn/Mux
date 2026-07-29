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
                    case "--output-format":
                        settings.OutputFormat = ReadValue(option, inlineValue, args, ref i);
                        break;
                    case "--compaction-strategy":
                        settings.CompactionStrategy = ReadValue(option, inlineValue, args, ref i);
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
