namespace Mux.Desktop.I18n
{
    /// <summary>
    /// Stable translation keys for user-facing strings. Keys — not English sentences — are the identifiers
    /// used throughout the UI, so a locale change never requires editing view code.
    /// </summary>
    public static class StringKeys
    {
        /// <summary>The application title ("mux").</summary>
        public const string AppTitle = "app.title";

        /// <summary>The application tagline.</summary>
        public const string AppTagline = "app.tagline";

        /// <summary>Splash "loading" status text.</summary>
        public const string SplashLoading = "splash.loading";

        /// <summary>Sidebar section header for the conversation list.</summary>
        public const string Conversations = "sidebar.conversations";

        /// <summary>New-conversation action label.</summary>
        public const string NewConversation = "sidebar.newConversation";

        /// <summary>Empty-state title in the workspace.</summary>
        public const string EmptyStateTitle = "workspace.emptyTitle";

        /// <summary>Empty-state body text in the workspace.</summary>
        public const string EmptyStateBody = "workspace.emptyBody";

        /// <summary>About / Help action label.</summary>
        public const string AboutHelp = "header.aboutHelp";

        /// <summary>About window: heading for the help section.</summary>
        public const string HelpHeading = "about.helpHeading";

        /// <summary>About window: help/getting-started body text.</summary>
        public const string HelpBody = "about.helpBody";

        /// <summary>License descriptor shown in the About window.</summary>
        public const string License = "about.license";
    }
}
