namespace Mux.Desktop.ViewModels
{
    using System;
    using CommunityToolkit.Mvvm.ComponentModel;
    using Mux.Desktop.I18n;

    /// <summary>
    /// View model for the application shell. Seeds the MVVM layer that later phases build the conversation
    /// list, transcript, and composer on top of. Its localized text refreshes when the active locale changes.
    /// </summary>
    public sealed class ShellViewModel : ObservableObject
    {
        private readonly ILocalizationService _Localization;
        private string _Title;
        private string _EmptyStateTitle;
        private string _EmptyStateBody;

        /// <summary>
        /// Instantiate the shell view model.
        /// </summary>
        /// <param name="localization">The localization service supplying UI strings. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="localization"/> is null.</exception>
        public ShellViewModel(ILocalizationService localization)
        {
            ArgumentNullException.ThrowIfNull(localization);

            _Localization = localization;
            _Title = _Localization.Get(StringKeys.AppTitle);
            _EmptyStateTitle = _Localization.Get(StringKeys.EmptyStateTitle);
            _EmptyStateBody = _Localization.Get(StringKeys.EmptyStateBody);

            _Localization.CultureChanged += OnCultureChanged;
        }

        /// <summary>The window/application title.</summary>
        public string Title
        {
            get => _Title;
            private set => SetProperty(ref _Title, value);
        }

        /// <summary>Workspace empty-state heading.</summary>
        public string EmptyStateTitle
        {
            get => _EmptyStateTitle;
            private set => SetProperty(ref _EmptyStateTitle, value);
        }

        /// <summary>Workspace empty-state body text.</summary>
        public string EmptyStateBody
        {
            get => _EmptyStateBody;
            private set => SetProperty(ref _EmptyStateBody, value);
        }

        private void OnCultureChanged(object? sender, EventArgs e)
        {
            Title = _Localization.Get(StringKeys.AppTitle);
            EmptyStateTitle = _Localization.Get(StringKeys.EmptyStateTitle);
            EmptyStateBody = _Localization.Get(StringKeys.EmptyStateBody);
        }
    }
}
