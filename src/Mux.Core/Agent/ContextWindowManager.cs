namespace Mux.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Manages context window budgeting by estimating token usage and trimming
    /// conversation history to stay within the model's context window limits.
    /// </summary>
    public class ContextWindowManager
    {
        #region Private-Members

        private int _ContextWindowSize;
        private double _TokenEstimationRatio;
        private int _SafetyMarginPercent;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ContextWindowManager"/> class.
        /// </summary>
        /// <param name="contextWindowSize">The total context window size in tokens.</param>
        /// <param name="tokenEstimationRatio">The character-to-token ratio for estimation (e.g. 3.5 means ~3.5 chars per token).</param>
        /// <param name="safetyMarginPercent">The percentage of the context window to reserve as a safety margin (e.g. 15 for 15%).</param>
        public ContextWindowManager(int contextWindowSize, double tokenEstimationRatio, int safetyMarginPercent)
        {
            _ContextWindowSize = contextWindowSize;
            _TokenEstimationRatio = tokenEstimationRatio > 0 ? tokenEstimationRatio : 3.5;
            _SafetyMarginPercent = Math.Clamp(safetyMarginPercent, 0, 50);
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The total context window size in tokens.
        /// </summary>
        public int ContextWindowSize
        {
            get => _ContextWindowSize;
        }

        /// <summary>
        /// The character-to-token estimation ratio.
        /// </summary>
        public double TokenEstimationRatio
        {
            get => _TokenEstimationRatio;
        }

        /// <summary>
        /// The safety margin percentage reserved from the context window.
        /// </summary>
        public int SafetyMarginPercent
        {
            get => _SafetyMarginPercent;
        }

        /// <summary>
        /// The effective token limit after applying the safety margin.
        /// </summary>
        public int EffectiveLimit
        {
            get => (int)(_ContextWindowSize * (1.0 - _SafetyMarginPercent / 100.0));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Estimates the number of tokens in a string based on the character-to-token ratio.
        /// </summary>
        /// <param name="text">The text to estimate tokens for.</param>
        /// <returns>The estimated token count.</returns>
        public int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return (int)(text.Length / _TokenEstimationRatio);
        }

        /// <summary>
        /// Estimates the total number of tokens across a list of conversation messages.
        /// Sums the estimated tokens for all message content fields.
        /// </summary>
        /// <param name="messages">The conversation messages to estimate.</param>
        /// <returns>The total estimated token count.</returns>
        public int EstimateTokens(List<ConversationMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                return 0;
            }

            int total = 0;
            foreach (ConversationMessage message in messages)
            {
                if (!string.IsNullOrEmpty(message.Content))
                {
                    total += EstimateTokens(message.Content);
                }

                if (message.ToolCalls != null)
                {
                    foreach (ToolCall toolCall in message.ToolCalls)
                    {
                        total += EstimateTokens(toolCall.Name);
                        total += EstimateTokens(toolCall.Arguments);
                    }
                }
            }

            return total;
        }

        /// <summary>
        /// Trims the conversation history to fit within the effective context window limit.
        /// Keeps the system message (first message if it is a system role) and always preserves
        /// the last two messages. Removes oldest non-system messages first.
        /// </summary>
        /// <param name="messages">The conversation messages to trim.</param>
        /// <returns>A new list of messages that fits within the effective limit.</returns>
        public List<ConversationMessage> TrimToFit(List<ConversationMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                return new List<ConversationMessage>();
            }

            int effectiveLimit = EffectiveLimit;
            int totalTokens = EstimateTokens(messages);

            if (totalTokens <= effectiveLimit)
            {
                return new List<ConversationMessage>(messages);
            }

            List<ConversationMessage> result = new List<ConversationMessage>(messages);

            bool hasSystemMessage = result.Count > 0 && result[0].Role == RoleEnum.System;
            int protectedStart = hasSystemMessage ? 1 : 0;

            while (EstimateTokens(result) > effectiveLimit && result.Count > protectedStart + 2)
            {
                result.RemoveAt(protectedStart);
            }

            return result;
        }

        /// <summary>
        /// Determines whether the conversation is approaching the context window limit.
        /// Returns true if the estimated token count exceeds 80% of the effective limit.
        /// </summary>
        /// <param name="messages">The conversation messages to check.</param>
        /// <returns>True if approaching the limit; false otherwise.</returns>
        public bool IsApproachingLimit(List<ConversationMessage> messages)
        {
            int effectiveLimit = EffectiveLimit;
            int totalTokens = EstimateTokens(messages);
            double threshold = effectiveLimit * 0.8;
            return totalTokens > threshold;
        }

        #endregion
    }
}
