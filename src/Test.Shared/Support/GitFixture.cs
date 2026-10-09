namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// A throwaway git repository for skill tests: initializes with a fixed identity and default branch, writes
    /// files, commits, and branches. Commands run with a hermetic environment (no global or system config) so a
    /// developer's git settings cannot change the results.
    /// </summary>
    public sealed class GitFixture
    {
        #region Private-Members

        private readonly string _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Creates and initializes a repository in the directory.
        /// </summary>
        /// <param name="directory">The repository directory; created when missing.</param>
        /// <param name="branch">The initial branch name. Defaults to main.</param>
        public GitFixture(string directory, string branch = "main")
        {
            _Directory = directory;
            System.IO.Directory.CreateDirectory(directory);
            Run("init", "-q", "-b", branch);
            Run("config", "user.email", "test@example.com");
            Run("config", "user.name", "Mux Test");
            Run("config", "commit.gpgsign", "false");
        }

        #endregion

        #region Public-Members

        /// <summary>The repository directory.</summary>
        public string Directory => _Directory;

        #endregion

        #region Public-Methods

        /// <summary>Whether git is on PATH.</summary>
        /// <returns>True when git can be started.</returns>
        public static bool IsAvailable()
        {
            try
            {
                using (Process process = Process.Start(new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!)
                {
                    process.WaitForExit(10000);
                    return process.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Writes a file relative to the repository root, creating folders as needed.</summary>
        /// <param name="relative">The relative path.</param>
        /// <param name="content">The content.</param>
        /// <returns>This fixture.</returns>
        public GitFixture Write(string relative, string content)
        {
            string path = Path.Combine(_Directory, relative);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return this;
        }

        /// <summary>Stages everything and commits.</summary>
        /// <param name="message">The commit message.</param>
        /// <returns>The new commit's full hash.</returns>
        public string Commit(string message)
        {
            Run("add", "-A");
            Run("commit", "-q", "-m", message);
            return Run("rev-parse", "HEAD").Trim();
        }

        /// <summary>Runs git and returns standard output; throws when git fails.</summary>
        /// <param name="arguments">The arguments.</param>
        /// <returns>Standard output.</returns>
        /// <exception cref="InvalidOperationException">Thrown when git exits non-zero.</exception>
        public string Run(params string[] arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo("git")
            {
                WorkingDirectory = _Directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            using (Process process = Process.Start(info)!)
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("git " + string.Join(" ", arguments) + " failed: " + error);
                }

                return output;
            }
        }

        #endregion
    }
}
