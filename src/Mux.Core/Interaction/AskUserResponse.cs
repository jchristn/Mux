namespace Mux.Core.Interaction
{
    using System.Collections.Generic;

    /// <summary>
    /// The user's answer to an <see cref="AskUserRequest"/>: the chosen option labels, free text typed under
    /// "Other", or a dismissal.
    /// </summary>
    public sealed class AskUserResponse
    {
        #region Public-Members

        /// <summary>The labels of the chosen options (empty when the user typed an answer or dismissed). Never null.</summary>
        public List<string> Selected { get; set; } = new List<string>();

        /// <summary>The free-text answer typed under "Other", or null.</summary>
        public string? OtherText { get; set; }

        /// <summary>Whether the user dismissed the question without answering.</summary>
        public bool Dismissed { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>Creates an answer that picks options.</summary>
        /// <param name="labels">The chosen labels.</param>
        /// <returns>The response.</returns>
        public static AskUserResponse Choose(params string[] labels)
        {
            return new AskUserResponse { Selected = new List<string>(labels) };
        }

        /// <summary>Creates a free-text answer.</summary>
        /// <param name="text">The typed answer.</param>
        /// <returns>The response.</returns>
        public static AskUserResponse Other(string text)
        {
            return new AskUserResponse { OtherText = text };
        }

        /// <summary>Creates a dismissal.</summary>
        /// <returns>The response.</returns>
        public static AskUserResponse Dismiss()
        {
            return new AskUserResponse { Dismissed = true };
        }

        #endregion
    }
}
