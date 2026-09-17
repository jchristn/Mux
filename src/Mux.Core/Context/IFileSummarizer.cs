namespace Mux.Core.Context
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Produces a dense, navigable summary of a large file. Implementations chunk the file, take per-chunk
    /// notes, and reduce them into a final summary that keeps line-range pointers; a cache may short-circuit
    /// the model calls for an unchanged file.
    /// </summary>
    public interface IFileSummarizer
    {
        /// <summary>
        /// Summarizes a file's contents.
        /// </summary>
        /// <param name="path">The file path (for cache addressing and context; never read from disk).</param>
        /// <param name="content">The full file contents.</param>
        /// <param name="chunkLines">The number of lines per chunk.</param>
        /// <param name="modelKey">A key identifying the summarizing model, for cache addressing.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The summary text, carrying line-range pointers.</returns>
        Task<string> SummarizeAsync(string path, string content, int chunkLines, string modelKey, CancellationToken cancellationToken);
    }
}
