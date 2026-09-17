namespace Mux.Core.Tools
{
    /// <summary>
    /// Provides configurable safety limits for tool execution.
    /// </summary>
    public static class ToolSafetyLimits
    {
        #region Public-Members

        /// <summary>
        /// The maximum file size in bytes that the read_file tool inlines whole. Above this, a file is either
        /// paged (when an offset/limit is given), mapped/summarized, or refused, per the large-file mode.
        /// Defaults to 1 MB.
        /// </summary>
        public static int MaxReadFileBytes = 1_048_576;

        /// <summary>
        /// The absolute maximum file size in bytes that the read_file tool will load into memory at all (to
        /// build a structural map or to page a requested range). A file larger than this is refused outright.
        /// Defaults to 16 MB.
        /// </summary>
        public static int MaxMappableFileBytes = 16_777_216;

        /// <summary>
        /// The maximum output size in bytes for process stdout/stderr before truncation. Defaults to 100 KB.
        /// </summary>
        public static int MaxProcessOutputBytes = 102_400;

        /// <summary>
        /// The default timeout in milliseconds for general tool execution. Defaults to 30 seconds.
        /// </summary>
        public static int DefaultToolTimeoutMs = 30_000;

        /// <summary>
        /// The default timeout in milliseconds for process execution. Defaults to 120 seconds.
        /// </summary>
        public static int DefaultProcessTimeoutMs = 120_000;

        #endregion
    }
}
