namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;

    /// <summary>
    /// Event containing a streamed chunk of the assistant's reasoning ("thinking"), distinct from the final
    /// answer text. Emitted only when the active endpoint has thinking display enabled. Reasoning is
    /// display-only: it is never added to the conversation history sent to the model.
    /// </summary>
    public class AssistantThinkingEvent : AgentEvent
    {
        #region Private-Members

        private string _Text = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="AssistantThinkingEvent"/> class.
        /// </summary>
        public AssistantThinkingEvent()
        {
            EventType = AgentEventTypeEnum.AssistantThinking;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The reasoning text chunk produced by the assistant.
        /// </summary>
        public string Text
        {
            get => _Text;
            set => _Text = value ?? throw new ArgumentNullException(nameof(Text));
        }

        #endregion
    }
}
