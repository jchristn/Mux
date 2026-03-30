namespace Mux.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Represents a tool invocation requested by the LLM.
    /// </summary>
    public class ToolCall
    {
        #region Private-Members

        private string _Id = string.Empty;
        private string _Name = string.Empty;
        private string _Arguments = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCall"/> class with default values.
        /// </summary>
        public ToolCall()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The unique identifier for this tool call.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id
        {
            get => _Id;
            set => _Id = value ?? throw new ArgumentNullException(nameof(Id));
        }

        /// <summary>
        /// The name of the tool to invoke.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name
        {
            get => _Name;
            set => _Name = value ?? throw new ArgumentNullException(nameof(Name));
        }

        /// <summary>
        /// The arguments for the tool call as a JSON string.
        /// </summary>
        [JsonPropertyName("arguments")]
        public string Arguments
        {
            get => _Arguments;
            set => _Arguments = value ?? throw new ArgumentNullException(nameof(Arguments));
        }

        #endregion
    }
}
