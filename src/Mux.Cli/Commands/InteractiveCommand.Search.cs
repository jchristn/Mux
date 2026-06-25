namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Tools;
    using Spectre.Console;

    public partial class InteractiveCommand
    {
        private const string TavilySearchProviderType = "tavily";
        private const string YouSearchProviderType = "you";
        private const string TavilySearchDefaultEndpoint = "https://api.tavily.com/search";
        private const string YouSearchDefaultEndpoint = "https://ydc-index.io/v1/search";

        private void HandleSearchCommand(string argument)
        {
            string trimmed = (argument ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed)
                || string.Equals(trimmed, "list", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "ls", StringComparison.OrdinalIgnoreCase))
            {
                HandleSearchListCommand();
                return;
            }

            string[] parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string action = parts[0].Trim().ToLowerInvariant();
            string value = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            switch (action)
            {
                case "show":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        WriteFailureLine("Usage: /search show <name>");
                        return;
                    }

                    HandleSearchShowCommand(value);
                    return;

                case "add":
                    HandleSearchAddCommand(string.IsNullOrWhiteSpace(value) ? null : value);
                    return;

                case "edit":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        WriteFailureLine("Usage: /search edit <name>");
                        return;
                    }

                    HandleSearchEditCommand(value);
                    return;

                case "remove":
                case "delete":
                case "rm":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        WriteFailureLine("Usage: /search remove|delete|rm <name>");
                        return;
                    }

                    HandleSearchRemoveCommand(value);
                    return;

                default:
                    WriteFailureLine("Unknown /search command. Use /search, /search list|ls, /search add [name], /search show <name>, /search edit <name>, or /search remove|delete|rm <name>.");
                    return;
            }
        }

        private void HandleSearchListCommand()
        {
            ExternalSearchSettings settings = CloneExternalSearchSettings(_MuxSettings.ExternalSearch);
            bool toolExposed = IsWebSearchToolExposed(settings);

            WriteOutputBlock(() =>
            {
                WriteWorkflowTitle("External Search");
                WriteWorkflowSummaryItem("Search enabled", settings.Enabled ? "yes" : "no");
                WriteWorkflowSummaryItem("Allow fallback", settings.AllowFallback ? "yes" : "no");
                WriteWorkflowSummaryItem("web_search tool exposed", toolExposed ? "yes" : "no");
                WriteWorkflowSummaryItem("Configured providers", settings.Providers.Count.ToString());

                if (settings.Providers.Count < 1)
                {
                    WriteWorkflowBlankLine();
                    WriteWorkflowHint("No external search providers are configured. Use /search add to create one.");
                    Console.WriteLine();
                    return;
                }

                WriteWorkflowBlankLine();
                WriteWorkflowLine("[dim][green]yes[/] marks enabled providers. [cyan]yes[/] marks the default provider for future searches.[/]");
                WriteWorkflowBlankLine();

                Table table = new Table();
                table.Border = TableBorder.Rounded;
                table.AddColumn("[bold]Provider[/]");
                table.AddColumn("[bold]Type[/]");
                table.AddColumn("[bold]Enabled[/]");
                table.AddColumn("[bold]Default[/]");
                table.AddColumn("[bold]API Key[/]");
                table.AddColumn("[bold]Endpoint[/]");

                foreach (ExternalSearchProviderConfig provider in settings.Providers)
                {
                    table.AddRow(
                        Markup.Escape(provider.Name),
                        Markup.Escape(FormatSearchProviderType(provider.ProviderType)),
                        provider.Enabled ? "[green]yes[/]" : "[dim]no[/]",
                        provider.IsDefault ? "[cyan]yes[/]" : "[dim]no[/]",
                        Markup.Escape(DescribeStoredSecret(provider.ApiKey)),
                        Markup.Escape(provider.Endpoint));
                }

                AnsiConsole.Write(table);
                Console.WriteLine();
            }, outputEndsWithPromptSpacer: true);
        }

        private void HandleSearchShowCommand(string providerName)
        {
            ExternalSearchProviderConfig? provider = _MuxSettings.ExternalSearch.Providers
                .FirstOrDefault(existing => string.Equals(existing.Name, providerName, StringComparison.OrdinalIgnoreCase));

            if (provider == null)
            {
                WriteFailureLine($"No external search provider named '{providerName}' is configured.");
                return;
            }

            Table table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn("[bold]Field[/]");
            table.AddColumn("[bold]Value[/]");

            table.AddRow("Name", Markup.Escape(provider.Name));
            table.AddRow("Provider type", Markup.Escape(FormatSearchProviderType(provider.ProviderType)));
            table.AddRow("Endpoint", Markup.Escape(provider.Endpoint));
            table.AddRow("API key", Markup.Escape(DescribeStoredSecret(provider.ApiKey)));
            table.AddRow("Enabled", provider.Enabled ? "[green]yes[/]" : "[dim]no[/]");
            table.AddRow("Default", provider.IsDefault ? "[cyan]yes[/]" : "[dim]no[/]");
            table.AddRow("Timeout (ms)", provider.TimeoutMs.ToString());
            table.AddRow("Global search enabled", _MuxSettings.ExternalSearch.Enabled ? "[green]yes[/]" : "[dim]no[/]");
            table.AddRow("Allow fallback", _MuxSettings.ExternalSearch.AllowFallback ? "[green]yes[/]" : "[dim]no[/]");
            table.AddRow("web_search tool exposed", IsWebSearchToolExposed() ? "[green]yes[/]" : "[dim]no[/]");

            WriteOutputBlock(() =>
            {
                WriteWorkflowTitle($"External Search Provider: {provider.Name}");
                WriteWorkflowBlankLine();
                AnsiConsole.Write(table);
                Console.WriteLine();
            }, outputEndsWithPromptSpacer: true);
        }

        private void HandleSearchAddCommand(string? suggestedName)
        {
            if (!EnsureQueueEmptyForStateChange("modify external search settings"))
            {
                return;
            }

            if (!TryRunSearchProviderWizard(
                SearchWizardMode.Add,
                null,
                suggestedName,
                out ExternalSearchProviderConfig provider,
                out bool searchEnabled,
                out bool allowFallback))
            {
                return;
            }

            ExternalSearchSettings updatedSettings = CloneExternalSearchSettings(_MuxSettings.ExternalSearch);
            if (updatedSettings.Providers.Any(existing => string.Equals(existing.Name, provider.Name, StringComparison.OrdinalIgnoreCase)))
            {
                WriteFailureLine($"An external search provider named '{provider.Name}' already exists.");
                return;
            }

            updatedSettings.Enabled = searchEnabled;
            updatedSettings.AllowFallback = allowFallback;
            updatedSettings.Providers.Add(provider);
            SaveExternalSearchSettings(updatedSettings);

            ExternalSearchProviderConfig savedProvider = _MuxSettings.ExternalSearch.Providers
                .First(existing => string.Equals(existing.Name, provider.Name, StringComparison.OrdinalIgnoreCase));

            WriteOutputBlock(() =>
            {
                WriteWorkflowTitle("External Search Provider Added");
                WriteWorkflowSummaryItem("Name", savedProvider.Name);
                WriteWorkflowSummaryItem("Provider type", FormatSearchProviderType(savedProvider.ProviderType));
                WriteWorkflowSummaryItem("Endpoint", savedProvider.Endpoint);
                WriteWorkflowSummaryItem("API key", DescribeStoredSecret(savedProvider.ApiKey));
                WriteWorkflowSummaryItem("Enabled", savedProvider.Enabled ? "yes" : "no");
                WriteWorkflowSummaryItem("Default", savedProvider.IsDefault ? "yes" : "no");
                WriteWorkflowSummaryItem("Search enabled", _MuxSettings.ExternalSearch.Enabled ? "yes" : "no");
                WriteWorkflowSummaryItem("Allow fallback", _MuxSettings.ExternalSearch.AllowFallback ? "yes" : "no");
                WriteWorkflowBlankLine();
                WriteWorkflowHint(IsWebSearchToolExposed()
                    ? "Future runs can use the built-in web_search tool."
                    : "Settings were saved, but the web_search tool is not currently exposed because search is disabled or no enabled provider is fully configured.");
                Console.WriteLine();
            }, outputEndsWithPromptSpacer: true);
        }

        private void HandleSearchEditCommand(string providerName)
        {
            if (!EnsureQueueEmptyForStateChange("modify external search settings"))
            {
                return;
            }

            ExternalSearchProviderConfig? existingProvider = _MuxSettings.ExternalSearch.Providers
                .FirstOrDefault(existing => string.Equals(existing.Name, providerName, StringComparison.OrdinalIgnoreCase));

            if (existingProvider == null)
            {
                WriteFailureLine($"No external search provider named '{providerName}' is configured.");
                return;
            }

            if (!TryRunSearchProviderWizard(
                SearchWizardMode.Edit,
                existingProvider,
                null,
                out ExternalSearchProviderConfig updatedProvider,
                out bool searchEnabled,
                out bool allowFallback))
            {
                return;
            }

            ExternalSearchSettings updatedSettings = CloneExternalSearchSettings(_MuxSettings.ExternalSearch);
            for (int i = 0; i < updatedSettings.Providers.Count; i++)
            {
                if (string.Equals(updatedSettings.Providers[i].Name, providerName, StringComparison.OrdinalIgnoreCase))
                {
                    updatedSettings.Providers[i] = updatedProvider;
                    break;
                }
            }

            updatedSettings.Enabled = searchEnabled;
            updatedSettings.AllowFallback = allowFallback;
            SaveExternalSearchSettings(updatedSettings);

            ExternalSearchProviderConfig savedProvider = _MuxSettings.ExternalSearch.Providers
                .First(existing => string.Equals(existing.Name, providerName, StringComparison.OrdinalIgnoreCase));

            WriteOutputBlock(() =>
            {
                WriteWorkflowTitle("External Search Provider Updated");
                WriteWorkflowSummaryItem("Name", savedProvider.Name);
                WriteWorkflowSummaryItem("Provider type", FormatSearchProviderType(savedProvider.ProviderType));
                WriteWorkflowSummaryItem("Endpoint", savedProvider.Endpoint);
                WriteWorkflowSummaryItem("API key", DescribeStoredSecret(savedProvider.ApiKey));
                WriteWorkflowSummaryItem("Enabled", savedProvider.Enabled ? "yes" : "no");
                WriteWorkflowSummaryItem("Default", savedProvider.IsDefault ? "yes" : "no");
                WriteWorkflowSummaryItem("Search enabled", _MuxSettings.ExternalSearch.Enabled ? "yes" : "no");
                WriteWorkflowSummaryItem("Allow fallback", _MuxSettings.ExternalSearch.AllowFallback ? "yes" : "no");
                WriteWorkflowBlankLine();
                WriteWorkflowHint(IsWebSearchToolExposed()
                    ? "Future runs can use the built-in web_search tool."
                    : "Settings were saved, but the web_search tool is not currently exposed because search is disabled or no enabled provider is fully configured.");
                Console.WriteLine();
            }, outputEndsWithPromptSpacer: true);
        }

        private void HandleSearchRemoveCommand(string providerName)
        {
            if (!EnsureQueueEmptyForStateChange("modify external search settings"))
            {
                return;
            }

            ExternalSearchProviderConfig? existingProvider = _MuxSettings.ExternalSearch.Providers
                .FirstOrDefault(existing => string.Equals(existing.Name, providerName, StringComparison.OrdinalIgnoreCase));

            if (existingProvider == null)
            {
                WriteFailureLine($"No external search provider named '{providerName}' is configured.");
                return;
            }

            bool completed = RunConsoleWizard(() =>
            {
                WriteWorkflowTitle($"External Search Remove: {existingProvider.Name}");
                WriteWorkflowHint("Ctrl+C or type cancel to abort.");
                WriteWorkflowBlankLine();
                WriteWorkflowSummaryItem("Provider type", FormatSearchProviderType(existingProvider.ProviderType));
                WriteWorkflowSummaryItem("Endpoint", existingProvider.Endpoint);
                WriteWorkflowSummaryItem("API key", DescribeStoredSecret(existingProvider.ApiKey));
                WriteWorkflowSummaryItem("Enabled", existingProvider.Enabled ? "yes" : "no");
                WriteWorkflowSummaryItem("Default", existingProvider.IsDefault ? "yes" : "no");
                WriteWorkflowBlankLine();

                if (!TryPromptYesNo("Remove this external search provider", false, out bool removeProvider))
                {
                    return CancelSearchWizard();
                }

                if (!removeProvider)
                {
                    return CancelSearchWizard();
                }

                return true;
            });

            if (!completed)
            {
                return;
            }

            ExternalSearchSettings updatedSettings = CloneExternalSearchSettings(_MuxSettings.ExternalSearch);
            updatedSettings.Providers = updatedSettings.Providers
                .Where(provider => !string.Equals(provider.Name, providerName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (updatedSettings.Providers.Count < 1)
            {
                updatedSettings.Enabled = false;
            }

            SaveExternalSearchSettings(updatedSettings);

            WriteOutputBlock(() =>
            {
                WriteWorkflowTitle("External Search Provider Removed");
                WriteWorkflowSummaryItem("Name", providerName);
                WriteWorkflowSummaryItem("Search enabled", _MuxSettings.ExternalSearch.Enabled ? "yes" : "no");
                WriteWorkflowSummaryItem("Remaining providers", _MuxSettings.ExternalSearch.Providers.Count.ToString());
                WriteWorkflowBlankLine();
                WriteWorkflowHint(IsWebSearchToolExposed()
                    ? "Future runs can still use the built-in web_search tool."
                    : "The built-in web_search tool is not currently exposed.");
                Console.WriteLine();
            }, outputEndsWithPromptSpacer: true);
        }

        private bool TryRunSearchProviderWizard(
            SearchWizardMode mode,
            ExternalSearchProviderConfig? existingProvider,
            string? suggestedName,
            out ExternalSearchProviderConfig configuredProvider,
            out bool searchEnabled,
            out bool allowFallback)
        {
            ExternalSearchProviderConfig workingProvider = existingProvider != null
                ? CloneExternalSearchProvider(existingProvider)
                : new ExternalSearchProviderConfig
                {
                    Name = suggestedName?.Trim() ?? string.Empty,
                    ProviderType = TavilySearchProviderType,
                    Endpoint = TavilySearchDefaultEndpoint,
                    Enabled = true,
                    IsDefault = !_MuxSettings.ExternalSearch.Providers.Any(provider => provider.Enabled),
                    TimeoutMs = 60000
                };

            bool effectiveSearchEnabled = _MuxSettings.ExternalSearch.Enabled || _MuxSettings.ExternalSearch.Providers.Count == 0;
            bool effectiveAllowFallback = _MuxSettings.ExternalSearch.AllowFallback;

            bool completed = RunConsoleWizard(() =>
            {
                WriteWorkflowTitle(mode == SearchWizardMode.Add
                    ? "External Search Add Wizard"
                    : $"External Search Edit Wizard: {existingProvider!.Name}");
                WriteWorkflowHint("Ctrl+C or type cancel at any prompt to abort.");
                WriteWorkflowHint("Press Enter to accept defaults where shown.");
                WriteWorkflowBlankLine();

                if (mode == SearchWizardMode.Add)
                {
                    if (!TryPromptSearchProviderName(suggestedName, out string providerName))
                    {
                        return CancelSearchWizard();
                    }

                    workingProvider.Name = providerName;
                }
                else
                {
                    WriteWorkflowLine($"[dim]Editing provider:[/] {Markup.Escape(existingProvider!.Name)}");
                    WriteWorkflowHint("Provider name is fixed during edit. Remove and re-add if you need to rename it.");
                    WriteWorkflowBlankLine();
                }

                string originalProviderType = workingProvider.ProviderType;
                if (!TryPromptSearchProviderType(workingProvider.ProviderType, out string providerType))
                {
                    return CancelSearchWizard();
                }

                workingProvider.ProviderType = providerType;

                string endpointDefault = workingProvider.Endpoint;
                if (string.IsNullOrWhiteSpace(endpointDefault))
                {
                    endpointDefault = GetDefaultSearchEndpoint(providerType);
                }
                else if (mode == SearchWizardMode.Edit
                    && string.Equals(endpointDefault, GetDefaultSearchEndpoint(originalProviderType), StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(originalProviderType, providerType, StringComparison.OrdinalIgnoreCase))
                {
                    endpointDefault = GetDefaultSearchEndpoint(providerType);
                }

                if (!TryPromptSearchEndpoint(providerType, endpointDefault, out string endpoint))
                {
                    return CancelSearchWizard();
                }

                workingProvider.Endpoint = endpoint;

                if (!TryPromptSearchApiKey(providerType, existingProvider?.ApiKey, out string apiKey))
                {
                    return CancelSearchWizard();
                }

                workingProvider.ApiKey = apiKey;

                if (!TryPromptYesNo("Enable this provider", workingProvider.Enabled, out bool providerEnabled))
                {
                    return CancelSearchWizard();
                }

                workingProvider.Enabled = providerEnabled;

                bool hasOtherEnabledProviders = _MuxSettings.ExternalSearch.Providers.Any(provider =>
                    provider.Enabled
                    && !string.Equals(provider.Name, workingProvider.Name, StringComparison.OrdinalIgnoreCase));
                bool defaultProviderChoice = workingProvider.IsDefault || !hasOtherEnabledProviders;

                if (!TryPromptYesNo("Set this as the default provider", defaultProviderChoice, out bool isDefault))
                {
                    return CancelSearchWizard();
                }

                workingProvider.IsDefault = workingProvider.Enabled && isDefault;

                if (!TryPromptYesNo("Enable external web search globally", effectiveSearchEnabled, out effectiveSearchEnabled))
                {
                    return CancelSearchWizard();
                }

                if (!TryPromptYesNo("Allow fallback to another provider on failure", effectiveAllowFallback, out effectiveAllowFallback))
                {
                    return CancelSearchWizard();
                }

                if (!TryPromptYesNo("Review advanced settings", false, out bool reviewAdvanced))
                {
                    return CancelSearchWizard();
                }

                if (reviewAdvanced)
                {
                    if (!TryPromptSearchTimeout(workingProvider.TimeoutMs, out int timeoutMs))
                    {
                        return CancelSearchWizard();
                    }

                    workingProvider.TimeoutMs = timeoutMs;
                }

                WriteWorkflowBlankLine();
                PrintSearchWizardSummary(workingProvider, effectiveSearchEnabled, effectiveAllowFallback);

                WriteWorkflowBlankLine();
                string savePrompt = mode == SearchWizardMode.Add
                    ? "Save this external search provider"
                    : "Save these external search changes";

                if (!TryPromptYesNo(savePrompt, true, out bool saveChanges))
                {
                    return CancelSearchWizard();
                }

                if (!saveChanges)
                {
                    return CancelSearchWizard();
                }

                return true;
            });

            configuredProvider = workingProvider;
            searchEnabled = effectiveSearchEnabled;
            allowFallback = effectiveAllowFallback;
            return completed;
        }

        private bool TryPromptSearchProviderName(string? suggestedName, out string providerName)
        {
            while (true)
            {
                if (!TryPromptRequiredWizardValue(
                    "Provider name",
                    suggestedName,
                    out providerName,
                    "Short name used with /search show, /search edit, /search remove/delete/rm, and the web_search tool's provider override."))
                {
                    return false;
                }

                string candidateName = providerName;
                if (_MuxSettings.ExternalSearch.Providers.Any(existing =>
                    string.Equals(existing.Name, candidateName, StringComparison.OrdinalIgnoreCase)))
                {
                    WriteWorkflowLine($"[red]An external search provider named '{Markup.Escape(candidateName)}' already exists. Choose a different name.[/]");
                    WriteWorkflowBlankLine();
                    continue;
                }

                return true;
            }
        }

        private bool TryPromptSearchProviderType(string currentValue, out string providerType)
        {
            while (true)
            {
                WriteWorkflowSection("Provider Type");
                WriteWorkflowOption("1", "tavily", "POST search API with answer and image support.");
                WriteWorkflowOption("2", "you", "You.com search API with web and news sections.");

                if (!TryPromptWizardValue("Provider type", FormatSearchProviderType(currentValue), out string selection))
                {
                    providerType = string.Empty;
                    return false;
                }

                string normalized = selection.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
                switch (normalized)
                {
                    case "1":
                    case "tavily":
                        providerType = TavilySearchProviderType;
                        return true;
                    case "2":
                    case "you":
                    case "you.com":
                    case "ydc":
                        providerType = YouSearchProviderType;
                        return true;
                    default:
                        WriteWorkflowLine("[red]Choose 1 (tavily) or 2 (you).[/]");
                        WriteWorkflowBlankLine();
                        break;
                }
            }
        }

        private bool TryPromptSearchEndpoint(string providerType, string currentValue, out string endpoint)
        {
            while (true)
            {
                string defaultValue = string.IsNullOrWhiteSpace(currentValue)
                    ? GetDefaultSearchEndpoint(providerType)
                    : currentValue;

                if (!TryPromptRequiredWizardValue(
                    "Endpoint",
                    defaultValue,
                    out endpoint,
                    "Press Enter to use the provider default endpoint, or enter a compatible override URL."))
                {
                    return false;
                }

                if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? endpointUri)
                    && (endpointUri.Scheme == Uri.UriSchemeHttps || endpointUri.Scheme == Uri.UriSchemeHttp))
                {
                    return true;
                }

                WriteWorkflowLine("[red]Enter a valid absolute http or https URL.[/]");
                WriteWorkflowBlankLine();
            }
        }

        private bool TryPromptSearchApiKey(string providerType, string? currentValue, out string apiKey)
        {
            if (!string.IsNullOrWhiteSpace(currentValue))
            {
                WriteWorkflowLine($"[dim]Current API key source:[/] {Markup.Escape(DescribeStoredSecret(currentValue))}");
                if (!TryPromptYesNo("Keep the existing API key configuration", true, out bool keepExisting))
                {
                    apiKey = string.Empty;
                    return false;
                }

                if (keepExisting)
                {
                    apiKey = currentValue;
                    return true;
                }

                WriteWorkflowBlankLine();
            }

            WriteWorkflowHint($"Suggested environment variable: {GetDefaultSearchApiKeyVariable(providerType)}");
            if (!TryPromptSecretStorageMode(out SecretStorageMode storageMode))
            {
                apiKey = string.Empty;
                return false;
            }

            switch (storageMode)
            {
                case SecretStorageMode.StoredValue:
                    if (!TryPromptSecretValue("API key", out string storedValue))
                    {
                        apiKey = string.Empty;
                        return false;
                    }

                    apiKey = storedValue;
                    return true;

                case SecretStorageMode.EnvironmentVariable:
                    if (!TryPromptEnvironmentReference("Environment variable", out string normalizedReference))
                    {
                        apiKey = string.Empty;
                        return false;
                    }

                    apiKey = normalizedReference;
                    return true;

                default:
                    apiKey = string.Empty;
                    return false;
            }
        }

        private bool TryPromptSearchTimeout(int currentValue, out int timeoutMs)
        {
            while (true)
            {
                if (!TryPromptInt("Timeout (ms)", currentValue, out timeoutMs))
                {
                    return false;
                }

                if (timeoutMs >= 1000 && timeoutMs <= 300000)
                {
                    return true;
                }

                WriteWorkflowLine("[red]Enter a timeout between 1000 and 300000 milliseconds.[/]");
                WriteWorkflowBlankLine();
            }
        }

        private void SaveExternalSearchSettings(ExternalSearchSettings settings)
        {
            MuxSettings persistedSettings = SettingsLoader.LoadSettings();
            persistedSettings.ExternalSearch = CloneExternalSearchSettings(settings);
            SettingsLoader.SaveSettings(persistedSettings);
            _MuxSettings.ExternalSearch = SettingsLoader.LoadSettings().ExternalSearch;
        }

        private bool IsWebSearchToolExposed()
        {
            return IsWebSearchToolExposed(_MuxSettings.ExternalSearch);
        }

        private bool IsWebSearchToolExposed(ExternalSearchSettings settings)
        {
            MuxSettings muxSettings = new MuxSettings
            {
                ExternalSearch = CloneExternalSearchSettings(settings)
            };

            return new BuiltInToolRegistry(muxSettings).HasTool("web_search");
        }

        private static ExternalSearchSettings CloneExternalSearchSettings(ExternalSearchSettings settings)
        {
            ExternalSearchSettings clone = new ExternalSearchSettings
            {
                Enabled = settings?.Enabled ?? false,
                AllowFallback = settings?.AllowFallback ?? true
            };

            foreach (ExternalSearchProviderConfig provider in settings?.Providers ?? new List<ExternalSearchProviderConfig>())
            {
                clone.Providers.Add(CloneExternalSearchProvider(provider));
            }

            return clone;
        }

        private static ExternalSearchProviderConfig CloneExternalSearchProvider(ExternalSearchProviderConfig provider)
        {
            return new ExternalSearchProviderConfig
            {
                Name = provider.Name,
                ProviderType = provider.ProviderType,
                Endpoint = provider.Endpoint,
                ApiKey = provider.ApiKey,
                Enabled = provider.Enabled,
                IsDefault = provider.IsDefault,
                TimeoutMs = provider.TimeoutMs
            };
        }

        private bool CancelSearchWizard()
        {
            WriteWorkflowLine("[yellow]Search workflow cancelled; nothing was saved.[/]");
            return false;
        }

        private void PrintSearchWizardSummary(ExternalSearchProviderConfig provider, bool searchEnabled, bool allowFallback)
        {
            WriteWorkflowSection("External Search Summary");
            WriteWorkflowSummaryItem("Name", provider.Name);
            WriteWorkflowSummaryItem("Provider type", FormatSearchProviderType(provider.ProviderType));
            WriteWorkflowSummaryItem("Endpoint", provider.Endpoint);
            WriteWorkflowSummaryItem("API key", DescribeStoredSecret(provider.ApiKey));
            WriteWorkflowSummaryItem("Enabled", provider.Enabled ? "yes" : "no");
            WriteWorkflowSummaryItem("Default", provider.IsDefault ? "yes" : "no");
            WriteWorkflowSummaryItem("Search enabled", searchEnabled ? "yes" : "no");
            WriteWorkflowSummaryItem("Allow fallback", allowFallback ? "yes" : "no");
            WriteWorkflowSummaryItem("Timeout (ms)", provider.TimeoutMs.ToString());
        }

        private static string GetDefaultSearchEndpoint(string providerType)
        {
            return NormalizeSearchProviderType(providerType) switch
            {
                YouSearchProviderType => YouSearchDefaultEndpoint,
                _ => TavilySearchDefaultEndpoint
            };
        }

        private static string GetDefaultSearchApiKeyVariable(string providerType)
        {
            return NormalizeSearchProviderType(providerType) switch
            {
                YouSearchProviderType => "YOU_API_KEY",
                _ => "TAVILY_API_KEY"
            };
        }

        private static string NormalizeSearchProviderType(string providerType)
        {
            string normalized = (providerType ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "you.com" => YouSearchProviderType,
                "ydc" => YouSearchProviderType,
                _ => normalized
            };
        }

        private static string FormatSearchProviderType(string providerType)
        {
            return NormalizeSearchProviderType(providerType) switch
            {
                YouSearchProviderType => "you",
                TavilySearchProviderType => "tavily",
                _ => providerType?.Trim() ?? string.Empty
            };
        }

        private static string DescribeStoredSecret(string value)
        {
            if (SettingsLoader.TryGetEnvironmentVariableName(value ?? string.Empty, out string variableName))
            {
                return "${" + variableName + "}";
            }

            return "stored in settings.json";
        }

        private enum SearchWizardMode
        {
            Add,
            Edit
        }
    }
}
