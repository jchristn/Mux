namespace Mux.Publisher.Publishing
{
    using System;
    using System.Formats.Tar;
    using System.IO;
    using System.IO.Compression;
    using Mux.Publisher.Channels;

    /// <summary>
    /// Produces distributable archives of a self-contained publish directory: a <c>.zip</c> for Windows
    /// runtimes and a <c>.tar.gz</c> for Unix runtimes (preserving the executable bit that Homebrew and
    /// direct-download users depend on). Each archive gets a SHA-256 that Scoop/Homebrew manifests read.
    /// </summary>
    public static class ArchiveService
    {
        /// <summary>
        /// Creates the conventional archive for a publish directory and returns its path.
        /// </summary>
        /// <param name="publishDir">The self-contained publish directory to archive.</param>
        /// <param name="rid">The runtime identifier (selects zip vs tar.gz).</param>
        /// <param name="outputPath">The archive path to write.</param>
        /// <returns>The archive path.</returns>
        public static string Create(string publishDir, string rid, string outputPath)
        {
            if (publishDir == null) throw new ArgumentNullException(nameof(publishDir));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(outputPath)) File.Delete(outputPath);

            if (ChannelHelpers.OsForRid(rid) == TargetOs.Windows)
            {
                ZipFile.CreateFromDirectory(publishDir, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            else
            {
                string tarPath = outputPath + ".tmp.tar";
                if (File.Exists(tarPath)) File.Delete(tarPath);
                TarFile.CreateFromDirectory(publishDir, tarPath, includeBaseDirectory: false);
                using (FileStream tarStream = File.OpenRead(tarPath))
                using (FileStream gzStream = File.Create(outputPath))
                using (GZipStream gzip = new GZipStream(gzStream, CompressionLevel.Optimal))
                {
                    tarStream.CopyTo(gzip);
                }

                File.Delete(tarPath);
            }

            return outputPath;
        }
    }
}
