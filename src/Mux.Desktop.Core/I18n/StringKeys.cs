namespace Mux.Desktop.I18n
{
    /// <summary>
    /// Stable translation keys for user-facing strings. Keys — not English sentences — are the identifiers
    /// used throughout the UI, so a locale change never requires editing view code. Desktop-specific keys are
    /// defined here; keys shared with the <c>mux serve</c> dashboard (nav, actions, tags) reuse the dashboard's
    /// dotted names so the two front ends share the same authored translations (see <c>DashboardStrings</c>).
    /// </summary>
    public static class StringKeys
    {
        // --- Desktop-specific chrome (see DesktopStrings) ---

        /// <summary>The application title ("mux").</summary>
        public const string AppTitle = "app.title";

        /// <summary>The application tagline.</summary>
        public const string AppTagline = "app.tagline";

        /// <summary>Splash "loading" status text.</summary>
        public const string SplashLoading = "splash.loading";

        /// <summary>Sidebar section header for the conversation list.</summary>
        public const string Conversations = "sidebar.conversations";

        /// <summary>New-conversation action label (long form).</summary>
        public const string NewConversation = "sidebar.newConversation";

        /// <summary>New-conversation action label (short form, for the sidebar button).</summary>
        public const string New = "sidebar.new";

        /// <summary>Refresh action label / tooltip for the sidebar.</summary>
        public const string Refresh = "sidebar.refresh";

        /// <summary>Empty-state title in the workspace.</summary>
        public const string EmptyStateTitle = "workspace.emptyTitle";

        /// <summary>Empty-state body text in the workspace (English only).</summary>
        public const string EmptyStateBody = "workspace.emptyBody";

        /// <summary>Composer placeholder text.</summary>
        public const string ComposerPlaceholder = "composer.placeholder";

        /// <summary>Composer send button label.</summary>
        public const string Send = "composer.send";

        /// <summary>Composer stop (cancel turn) button label.</summary>
        public const string Stop = "composer.stop";

        /// <summary>Streaming "thinking" status label.</summary>
        public const string ChatThinking = "chat.thinking";

        /// <summary>User role label in the transcript.</summary>
        public const string ChatYou = "chat.you";

        /// <summary>Assistant role label in the transcript.</summary>
        public const string ChatAssistant = "chat.assistant";

        /// <summary>About / Help action label.</summary>
        public const string AboutHelp = "header.aboutHelp";

        /// <summary>About window: heading for the help section.</summary>
        public const string HelpHeading = "about.helpHeading";

        /// <summary>About window: help/getting-started body text (English only).</summary>
        public const string HelpBody = "about.helpBody";

        /// <summary>License descriptor shown in the About window.</summary>
        public const string License = "about.license";

        // --- Shared with the dashboard (see DashboardStrings) ---

        /// <summary>Language selector label.</summary>
        public const string LangLabel = "lang.label";

        /// <summary>Navigation: Chat.</summary>
        public const string NavChat = "nav.chat";

        /// <summary>Navigation: Endpoints.</summary>
        public const string NavEndpoints = "nav.endpoints";

        /// <summary>Navigation: MCP Servers.</summary>
        public const string NavMcp = "nav.mcp";

        /// <summary>Navigation: Prompts.</summary>
        public const string NavPrompts = "nav.prompts";

        /// <summary>Navigation: Subagents.</summary>
        public const string NavSubagents = "nav.subagents";

        /// <summary>Navigation: Skills.</summary>
        public const string NavSkills = "nav.skills";

        /// <summary>Navigation: Hooks.</summary>
        public const string NavHooks = "nav.hooks";

        /// <summary>Navigation: Commands.</summary>
        public const string NavCommands = "nav.commands";

        /// <summary>Navigation: Keybindings.</summary>
        public const string NavKeybindings = "nav.keybindings";

        /// <summary>Navigation: Sessions.</summary>
        public const string NavSessions = "nav.sessions";

        /// <summary>Navigation: Settings.</summary>
        public const string NavSettings = "nav.settings";

        /// <summary>Navigation: Usage.</summary>
        public const string NavUsage = "nav.usage";

        /// <summary>Navigation: Pricing.</summary>
        public const string NavPricing = "nav.pricing";

        /// <summary>Action: Save.</summary>
        public const string ActSave = "act.save";

        /// <summary>Action: Cancel.</summary>
        public const string ActCancel = "act.cancel";

        /// <summary>Action: Close.</summary>
        public const string ActClose = "act.close";

        /// <summary>Action: Delete.</summary>
        public const string ActDelete = "act.delete";

        /// <summary>Action: Edit.</summary>
        public const string ActEdit = "act.edit";

        /// <summary>Action: Reload.</summary>
        public const string ActReload = "act.reload";
    }
}
