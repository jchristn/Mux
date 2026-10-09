namespace Mux.Core.Context
{
    /// <summary>
    /// What a resolved <c>@path</c> mention points at.
    /// </summary>
    public enum FileMentionKindEnum
    {
        /// <summary>A file; its contents (or a structural map of a large file) are attached.</summary>
        File = 0,

        /// <summary>A directory; a listing of its entries is attached.</summary>
        Directory = 1
    }
}
