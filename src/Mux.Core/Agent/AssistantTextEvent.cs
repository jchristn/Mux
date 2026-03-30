namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;

    /// <summary>
    /// Event containing a streamed or complete text chunk from the assistant.
    /// </summary>
    public class AssistantTextEvent : AgentEvent
    {
        #region Private-Members

        private string _Text = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="AssistantTextEvent"/> class.
        /// </summary>
        public AssistantTextEvent()
        {
            EventType = AgentEventTypeEnum.AssistantText;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The text chunk produced by the assistant.
        /// </summary>
        public string Text
        {
            get => _Text;
            set => _Text = value ?? throw new ArgumentNullException(nameof(Text));
        }

        #endregion
    }
}
