namespace Test.Shared.Support
{
    using System.Collections.Generic;

    /// <summary>
    /// How a test answers an <c>ask_user</c> question during a streamed turn.
    /// </summary>
    internal sealed class ParityAnswer
    {
        /// <summary>The choice labels to select.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>True to skip the question.</summary>
        public bool Skip { get; set; }
    }
}
