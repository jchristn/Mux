namespace Test.Shared.Support
{
    using System;
    using System.IO;
    using Mux.Core.Memory;

    /// <summary>
    /// A temporary layout for memory tests: a config folder holding a <see cref="MemoryStore"/>, and a project folder
    /// named <c>my-app</c> that looks like a git repository (it has a <c>.git</c> folder and a <c>src</c> subfolder).
    /// Disposing deletes everything.
    /// </summary>
    public sealed class MemoryFixture : IDisposable
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Creates the folders and the store.
        /// </summary>
        public MemoryFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "mux-memory-" + Guid.NewGuid().ToString("N"));
            Config = Path.Combine(Root, "config");
            Project = Path.Combine(Root, "my-app");
            Directory.CreateDirectory(Path.Combine(Project, ".git"));
            Directory.CreateDirectory(Path.Combine(Project, "src"));
            Directory.CreateDirectory(Config);
            Store = new MemoryStore(Path.Combine(Config, "memory"));
        }

        #endregion

        #region Public-Members

        /// <summary>The temporary root.</summary>
        public string Root { get; }

        /// <summary>The config directory (set <c>MUX_CONFIG_DIR</c> to it for CLI and REST tests).</summary>
        public string Config { get; }

        /// <summary>The project directory.</summary>
        public string Project { get; }

        /// <summary>The store rooted at <c>Config/memory</c>.</summary>
        public MemoryStore Store { get; }

        #endregion

        #region Public-Methods

        /// <summary>Deletes the temporary folders.</summary>
        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch (Exception) { }
        }

        #endregion
    }
}
