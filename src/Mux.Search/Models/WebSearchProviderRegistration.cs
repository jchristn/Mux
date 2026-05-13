namespace Mux.Search.Models
{
    using System;

    /// <summary>
    /// Registers one named provider instance for normalized web-search execution.
    /// </summary>
    public class WebSearchProviderRegistration
    {
        private string _Name = string.Empty;
        private string _ProviderType = string.Empty;
        private bool _Enabled = true;
        private bool _IsDefault = false;
        private SearchProviderOptions _Options = new SearchProviderOptions();

        /// <summary>
        /// Friendly provider configuration name.
        /// </summary>
        public string Name
        {
            get => _Name;
            set => _Name = value?.Trim() ?? throw new ArgumentNullException(nameof(Name));
        }

        /// <summary>
        /// Provider type identifier such as tavily or you.
        /// </summary>
        public string ProviderType
        {
            get => _ProviderType;
            set => _ProviderType = value?.Trim() ?? throw new ArgumentNullException(nameof(ProviderType));
        }

        /// <summary>
        /// Whether this provider may be selected.
        /// </summary>
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// Whether this provider is the default selection.
        /// </summary>
        public bool IsDefault
        {
            get => _IsDefault;
            set => _IsDefault = value;
        }

        /// <summary>
        /// Provider HTTP options.
        /// </summary>
        public SearchProviderOptions Options
        {
            get => _Options;
            set => _Options = value ?? throw new ArgumentNullException(nameof(Options));
        }
    }
}
