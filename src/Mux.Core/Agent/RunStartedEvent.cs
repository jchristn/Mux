namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;

    /// <summary>
    /// Event emitted when a mux run starts and effective runtime metadata is known.
    /// </summary>
    public class RunStartedEvent : AgentEvent
    {
        #region Private-Members

        private string _RunId = string.Empty;
        private string _EndpointName = string.Empty;
        private string _AdapterType = string.Empty;
        private string _BaseUrl = string.Empty;
        private string _Model = string.Empty;
        private string _ApprovalPolicy = string.Empty;
        private string _WorkingDirectory = string.Empty;
        private int _MaxIterations = 25;
        private bool _ToolsEnabled = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="RunStartedEvent"/> class.
        /// </summary>
        public RunStartedEvent()
        {
            EventType = AgentEventTypeEnum.RunStarted;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Correlation identifier for the run.
        /// </summary>
        public string RunId
        {
            get => _RunId;
            set => _RunId = value ?? throw new ArgumentNullException(nameof(RunId));
        }

        /// <summary>
        /// Effective endpoint name.
        /// </summary>
        public string EndpointName
        {
            get => _EndpointName;
            set => _EndpointName = value ?? string.Empty;
        }

        /// <summary>
        /// Effective adapter type.
        /// </summary>
        public string AdapterType
        {
            get => _AdapterType;
            set => _AdapterType = value ?? string.Empty;
        }

        /// <summary>
        /// Effective base URL.
        /// </summary>
        public string BaseUrl
        {
            get => _BaseUrl;
            set => _BaseUrl = value ?? string.Empty;
        }

        /// <summary>
        /// Effective model identifier.
        /// </summary>
        public string Model
        {
            get => _Model;
            set => _Model = value ?? string.Empty;
        }

        /// <summary>
        /// Effective approval policy.
        /// </summary>
        public string ApprovalPolicy
        {
            get => _ApprovalPolicy;
            set => _ApprovalPolicy = value ?? string.Empty;
        }

        /// <summary>
        /// Working directory used for tool execution.
        /// </summary>
        public string WorkingDirectory
        {
            get => _WorkingDirectory;
            set => _WorkingDirectory = value ?? string.Empty;
        }

        /// <summary>
        /// Maximum agent iterations for the run.
        /// </summary>
        public int MaxIterations
        {
            get => _MaxIterations;
            set => _MaxIterations = value;
        }

        /// <summary>
        /// Whether tool calling is enabled for the endpoint.
        /// </summary>
        public bool ToolsEnabled
        {
            get => _ToolsEnabled;
            set => _ToolsEnabled = value;
        }

        #endregion
    }
}
