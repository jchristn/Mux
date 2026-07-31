namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// Runs a single skill command deterministically and returns the result in the same shape as
    /// <c>run_process</c> (<c>stdout</c>, <c>stderr</c>, <c>exit_code</c>, <c>timed_out</c>). Inline body
    /// blocks are written to a temporary file and executed by the declared interpreter; bundled scripts run
    /// in place. The command's timeout bounds the run, the process tree is killed on timeout, and output is
    /// truncated to the shared safety limit.
    /// </summary>
    public sealed class SkillExecutor
    {
        #region Public-Methods

        /// <summary>
        /// Executes one of a skill's commands.
        /// </summary>
        /// <param name="toolCallId">The tool-call identifier to echo into the result.</param>
        /// <param name="skill">The owning skill. Must not be null.</param>
        /// <param name="command">The command to run. Must not be null.</param>
        /// <param name="arguments">Arguments appended to the command's script. May be null.</param>
        /// <param name="workingDirectory">The working directory for the run.</param>
        /// <param name="cancellationToken">A token to cancel the run.</param>
        /// <returns>A <see cref="ToolResult"/> with the process output, or an error result on failure.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skill"/> or <paramref name="command"/> is null.</exception>
        public async Task<ToolResult> ExecuteAsync(
            string toolCallId,
            Skill skill,
            SkillCommand command,
            IReadOnlyList<string>? arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));
            if (command == null) throw new ArgumentNullException(nameof(command));

            string? tempFile = null;
            try
            {
                string scriptPath = ResolveScriptPath(skill, command, out tempFile);

                ProcessStartInfo startInfo = SkillInterpreterResolver.BuildStartInfo(command.Interpreter, scriptPath, arguments);
                startInfo.WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? skill.DirectoryPath : workingDirectory;
                startInfo.Environment["MUX_SKILL_NAME"] = skill.Manifest.Name;
                startInfo.Environment["MUX_SKILL_DIR"] = skill.DirectoryPath;
                startInfo.Environment["MUX_SKILL_COMMAND"] = command.Name;

                return await RunProcessAsync(toolCallId, startInfo, command.TimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "skill_run_error", message = ex.Message })
                };
            }
            finally
            {
                if (tempFile != null)
                {
                    TryDeleteFile(tempFile);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static string ResolveScriptPath(Skill skill, SkillCommand command, out string? tempFile)
        {
            tempFile = null;

            if (!string.IsNullOrWhiteSpace(command.BlockId))
            {
                if (!skill.CodeBlocks.TryGetValue(command.BlockId!, out string? code))
                {
                    throw new InvalidOperationException($"Skill '{skill.Manifest.Name}' has no code block '{command.BlockId}'.");
                }

                string extension = SkillInterpreters.FileExtension(command.Interpreter);
                string path = Path.Combine(Path.GetTempPath(), "mux-skill-" + Guid.NewGuid().ToString("N") + extension);
                File.WriteAllText(path, code);
                tempFile = path;
                return path;
            }

            if (!string.IsNullOrWhiteSpace(command.ScriptPath))
            {
                string full = Path.GetFullPath(Path.Combine(skill.DirectoryPath, command.ScriptPath!));
                if (!File.Exists(full))
                {
                    throw new FileNotFoundException($"Skill '{skill.Manifest.Name}' script '{command.ScriptPath}' was not found.", full);
                }

                return full;
            }

            throw new InvalidOperationException($"Command '{command.Name}' declares neither a script nor a block.");
        }

        private static async Task<ToolResult> RunProcessAsync(string toolCallId, ProcessStartInfo startInfo, int timeoutMs, CancellationToken cancellationToken)
        {
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;

                StringBuilder stdout = new StringBuilder();
                StringBuilder stderr = new StringBuilder();

                process.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
                {
                    if (e.Data != null)
                    {
                        stdout.AppendLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
                {
                    if (e.Data != null)
                    {
                        stderr.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool timedOut = false;

                using (CancellationTokenSource timeoutCts = new CancellationTokenSource(timeoutMs))
                using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    try
                    {
                        await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        timedOut = timeoutCts.IsCancellationRequested;
                        TryKill(process);

                        if (!timedOut)
                        {
                            throw;
                        }
                    }
                }

                int exitCode = timedOut ? -1 : process.ExitCode;
                string stdoutStr = Truncate(stdout.ToString());
                string stderrStr = Truncate(stderr.ToString());

                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = !timedOut && exitCode == 0,
                    Content = JsonSerializer.Serialize(new
                    {
                        stdout = stdoutStr,
                        stderr = stderrStr,
                        exit_code = exitCode,
                        timed_out = timedOut
                    })
                };
            }
        }

        private static string Truncate(string value)
        {
            int max = ToolSafetyLimits.MaxProcessOutputBytes;
            if (value.Length > max)
            {
                return value.Substring(0, max) + "\n[truncated — output exceeded " + max + " bytes]";
            }

            return value;
        }

        private static void TryKill(Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Best-effort kill.
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of the materialized block.
            }
        }

        #endregion
    }
}
