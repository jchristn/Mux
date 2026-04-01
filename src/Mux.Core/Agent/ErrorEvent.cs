namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;

    /// <summary>
    /// Event representing an error that occurred during agent processing.
    /// </summary>
    public class ErrorEvent : AgentEvent
    {
        #region Private-Members

        private string _Code = string.Empty;
        private string _Message = string.Empty;
        private string _FailureCategory = string.Empty;
        private string _EndpointName = string.Empty;
        private string _AdapterType = string.Empty;
        private string _BaseUrl = string.Empty;
        private string _Model = string.Empty;
        private string _CommandName = string.Empty;
        private string _ConfigDirectory = string.Empty;
        private string _EndpointSelectionSource = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorEvent"/> class.
        /// </summary>
        public ErrorEvent()
        {
            EventType = AgentEventTypeEnum.Error;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// A stable machine-readable error code (e.g. "llm_error", "tool_timeout").
        /// </summary>
        public string Code
        {
            get => _Code;
            set => _Code = value ?? throw new ArgumentNullException(nameof(Code));
        }

        /// <summary>
        /// A human-readable description of the error.
        /// </summary>
        public string Message
        {
            get => _Message;
            set => _Message = value ?? throw new ArgumentNullException(nameof(Message));
        }

        /// <summary>
        /// A stable machine-readable failure category (e.g. "configuration", "network").
        /// </summary>
        public string FailureCategory
        {
            get => _FailureCategory;
            set => _FailureCategory = value ?? string.Empty;
        }

        /// <summary>
        /// Effective endpoint name when known.
        /// </summary>
        public string EndpointName
        {
            get => _EndpointName;
            set => _EndpointName = value ?? string.Empty;
        }

        /// <summary>
        /// Effective adapter type when known.
        /// </summary>
        public string AdapterType
        {
            get => _AdapterType;
            set => _AdapterType = value ?? string.Empty;
        }

        /// <summary>
        /// Effective base URL when known.
        /// </summary>
        public string BaseUrl
        {
            get => _BaseUrl;
            set => _BaseUrl = value ?? string.Empty;
        }

        /// <summary>
        /// Effective model when known.
        /// </summary>
        public string Model
        {
            get => _Model;
            set => _Model = value ?? string.Empty;
        }

        /// <summary>
        /// Command mode that produced the error when known.
        /// </summary>
        public string CommandName
        {
            get => _CommandName;
            set => _CommandName = value ?? string.Empty;
        }

        /// <summary>
        /// Effective mux config directory when known.
        /// </summary>
        public string ConfigDirectory
        {
            get => _ConfigDirectory;
            set => _ConfigDirectory = value ?? string.Empty;
        }

        /// <summary>
        /// How mux selected the effective endpoint when known.
        /// </summary>
        public string EndpointSelectionSource
        {
            get => _EndpointSelectionSource;
            set => _EndpointSelectionSource = value ?? string.Empty;
        }

        /// <summary>
        /// CLI override categories applied to the run when known.
        /// </summary>
        public System.Collections.Generic.List<string> CliOverridesApplied { get; set; } = new System.Collections.Generic.List<string>();

        #endregion
    }
}
