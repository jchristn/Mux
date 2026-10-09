namespace Mux.Core.Skills.Packaging
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using Mux.Core.Models;

    /// <summary>
    /// Imports Claude-format skill folders (a single skill folder, a folder holding many, or a git repository) into a
    /// mux skills directory or a pack folder, normalizing each one with <see cref="SkillImportNormalizer"/> and
    /// validating the result. Imported content is data: nothing in it is ever executed. Existing destination folders are
    /// left alone unless <c>force</c> is set.
    /// </summary>
    public sealed class SkillImporter
    {
        #region Private-Members

        private static readonly HashSet<string> _MarkdownExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".mdx" };

        private static readonly HashSet<string> _TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".py", ".sh", ".bash", ".zsh", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".ps1", ".rb", ".pl", ".json", ".yaml", ".yml",
            ".toml", ".txt", ".csv", ".tsv", ".html", ".htm", ".css", ".xml", ".ini", ".cfg", ".sql", ".r", ".go", ".rs", ".java", ".cs"
        };

        private static readonly HashSet<string> _SkippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "__pycache__", ".venv", "venv"
        };

        private static readonly string[] _BundleFolders = { "scripts", "references", "assets", "templates" };

        private static readonly Regex _UnsafeId = new Regex("[^a-z0-9-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether <paramref name="source"/> looks like a git URL rather than a local path.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <returns>True for http(s), ssh, and git@ URLs.</returns>
        public static bool IsGitUrl(string? source)
        {
            string value = (source ?? string.Empty).Trim();
            return value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".git", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Turns a folder name into a mux skill id: lowercase letters, digits, and single hyphens.
        /// </summary>
        /// <param name="name">The folder name.</param>
        /// <returns>The id, or empty when nothing usable is left.</returns>
        public static string ToSkillId(string? name)
        {
            string id = _UnsafeId.Replace((name ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-'), "-");
            while (id.Contains("--", StringComparison.Ordinal)) id = id.Replace("--", "-", StringComparison.Ordinal);
            return id.Trim('-');
        }

        /// <summary>
        /// Finds every skill folder (a folder with a <c>SKILL.md</c>) under <paramref name="root"/>, skipping dot-folders
        /// (for example a <c>.gemini</c> mirror or <c>.git</c>) and dependency folders. A <paramref name="root"/> that is
        /// itself a skill folder is returned alone.
        /// </summary>
        /// <param name="root">The folder.</param>
        /// <returns>The skill folders, sorted.</returns>
        public static List<string> FindSkillFolders(string root)
        {
            List<string> folders = new List<string>();
            if (!Directory.Exists(root))
            {
                return folders;
            }

            if (File.Exists(Path.Combine(root, "SKILL.md")))
            {
                folders.Add(Path.GetFullPath(root));
                return folders;
            }

            Stack<string> pending = new Stack<string>();
            pending.Push(Path.GetFullPath(root));
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                foreach (string sub in Directory.EnumerateDirectories(current))
                {
                    string name = Path.GetFileName(sub);
                    if (name.StartsWith(".", StringComparison.Ordinal) || _SkippedDirectories.Contains(name))
                    {
                        continue;
                    }

                    if (File.Exists(Path.Combine(sub, "SKILL.md")))
                    {
                        folders.Add(sub);
                    }
                    else
                    {
                        pending.Push(sub);
                    }
                }
            }

            folders.Sort(StringComparer.Ordinal);
            return folders;
        }

        /// <summary>
        /// Detects the license of a source tree from its LICENSE file.
        /// </summary>
        /// <param name="root">The source root.</param>
        /// <returns><c>MIT</c>, <c>Apache-2.0</c>, <c>BSD</c>, <c>GPL</c>, or null when unknown.</returns>
        public static string? DetectLicense(string root)
        {
            foreach (string name in new[] { "LICENSE", "LICENSE.md", "LICENSE.txt", "LICENCE" })
            {
                string path = Path.Combine(root, name);
                if (!File.Exists(path)) continue;
                string text = File.ReadAllText(path);
                if (text.Contains("MIT License", StringComparison.OrdinalIgnoreCase) || text.Contains("Permission is hereby granted, free of charge", StringComparison.Ordinal)) return "MIT";
                if (text.Contains("Apache License", StringComparison.OrdinalIgnoreCase)) return "Apache-2.0";
                if (text.Contains("BSD", StringComparison.Ordinal)) return "BSD";
                if (text.Contains("GNU GENERAL PUBLIC LICENSE", StringComparison.OrdinalIgnoreCase)) return "GPL";
            }

            return null;
        }

        /// <summary>
        /// Imports the skills found in <paramref name="source"/>.
        /// </summary>
        /// <param name="source">A local folder or a git URL.</param>
        /// <param name="destination">The skills directory (or, with <paramref name="pack"/>, a packs folder that gets a <c>&lt;pack&gt;/</c> subfolder when <paramref name="packLayout"/> is set).</param>
        /// <param name="pack">The pack the skills belong to (sets their category), or null.</param>
        /// <param name="packLayout">Whether to write <c>&lt;destination&gt;/&lt;pack&gt;/&lt;id&gt;/</c> with a <c>pack.json</c>, instead of <c>&lt;destination&gt;/&lt;id&gt;/</c>.</param>
        /// <param name="dryRun">Whether to only report what would happen.</param>
        /// <param name="force">Whether to replace existing destination folders.</param>
        /// <param name="cancellationToken">Cancels a clone.</param>
        /// <returns>One report per skill found.</returns>
        /// <exception cref="ArgumentException">Thrown when the source or destination is blank, or the local source does not exist.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a git clone fails.</exception>
        public List<SkillImportReport> Import(string source, string destination, string? pack, bool packLayout, bool dryRun, bool force, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("A source folder or git URL is required.", nameof(source));
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("A destination folder is required.", nameof(destination));
            if (packLayout && string.IsNullOrWhiteSpace(pack)) throw new ArgumentException("A pack id is required to write the pack layout.", nameof(pack));

            string? clone = null;
            try
            {
                string root;
                string sourceLabel;
                if (IsGitUrl(source))
                {
                    clone = Path.Combine(Path.GetTempPath(), "mux-skill-import-" + Guid.NewGuid().ToString("N"));
                    string commit = Clone(source.Trim(), clone, cancellationToken);
                    root = clone;
                    sourceLabel = source.Trim() + (commit.Length > 0 ? "@" + commit : string.Empty);
                }
                else
                {
                    root = Path.GetFullPath(source);
                    if (!Directory.Exists(root)) throw new ArgumentException("The source folder '" + source + "' does not exist.", nameof(source));
                    sourceLabel = string.Empty;
                }

                string? license = DetectLicense(root) ?? DetectLicense(Path.GetDirectoryName(root) ?? root);
                string packId = ToSkillId(pack);
                string targetRoot = packLayout ? Path.Combine(destination, packId) : destination;
                List<SkillImportReport> reports = new List<SkillImportReport>();
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string folder in FindSkillFolders(root))
                {
                    string relative = Path.GetRelativePath(root, folder).Replace('\\', '/');
                    string id = ToSkillId(Path.GetFileName(folder));
                    SkillImportReport report = new SkillImportReport { Id = id, SourcePath = relative == "." ? root : relative, Target = Path.Combine(targetRoot, id) };
                    reports.Add(report);
                    if (id.Length == 0)
                    {
                        report.Status = "skipped";
                        report.Errors.Add("the folder name has no usable characters for a skill id");
                        continue;
                    }

                    if (!seen.Add(id))
                    {
                        report.Status = "skipped";
                        report.Errors.Add("another skill in this source already uses the id '" + id + "'");
                        continue;
                    }

                    if (Directory.Exists(report.Target) && !force)
                    {
                        report.Status = "skipped";
                        report.Errors.Add("the destination already exists; pass --force to replace it");
                        continue;
                    }

                    string skillSource = sourceLabel.Length > 0 ? sourceLabel + (relative == "." ? string.Empty : "#" + relative) : string.Empty;
                    Dictionary<string, byte[]> files = Normalize(folder, id, packId, relative, skillSource, license, report);
                    if (dryRun)
                    {
                        report.Status = "would-import";
                        continue;
                    }

                    if (Directory.Exists(report.Target))
                    {
                        Directory.Delete(report.Target, true);
                    }

                    SkillFileWriter.WriteAll(report.Target, files, id);
                    Skill loaded = new SkillLoader(targetRoot).Load(report.Target);
                    report.Errors.AddRange(loaded.Validation.Errors);
                    report.Status = loaded.IsValid ? "imported" : "invalid";
                }

                if (packLayout && !dryRun && reports.Exists((SkillImportReport r) => r.Status == "imported" || r.Status == "invalid"))
                {
                    WritePackJson(targetRoot, packId, sourceLabel, license);
                }

                return reports;
            }
            finally
            {
                if (clone != null)
                {
                    TryDelete(clone);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, byte[]> Normalize(string folder, string id, string packId, string relative, string source, string? license, SkillImportReport report)
        {
            Dictionary<string, byte[]> raw = EmbeddedSkillFiles.ReadDirectory(folder);
            List<string> bundleFolders = new List<string>();
            foreach (string name in _BundleFolders)
            {
                if (Directory.Exists(Path.Combine(folder, name))) bundleFolders.Add(name);
            }

            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            int scriptDashes = 0;
            UTF8Encoding utf8 = new UTF8Encoding(false);
            foreach (KeyValuePair<string, byte[]> file in raw)
            {
                string[] segments = file.Key.Split('/');
                if (Array.Exists(segments, (string s) => _SkippedDirectories.Contains(s)))
                {
                    continue;
                }

                string extension = Path.GetExtension(file.Key);
                if (_MarkdownExtensions.Contains(extension))
                {
                    string text = utf8.GetString(file.Value).TrimStart('﻿');
                    string normalized = SkillImportNormalizer.NormalizeMarkdown(text, bundleFolders, out List<string> changes, out List<string> flags);
                    if (string.Equals(file.Key, "SKILL.md", StringComparison.Ordinal))
                    {
                        string? existing = SkillImportNormalizer.ReadFrontmatterValue(normalized, "category");
                        string category = !string.IsNullOrWhiteSpace(existing)
                            ? existing!
                            : SkillImportNormalizer.InferCategory(packId, relative, SkillImportNormalizer.ReadFrontmatterValue(normalized, "description"));
                        report.Category = category;
                        normalized = SkillImportNormalizer.EnsureFrontmatter(normalized, id, category, source.Length > 0 ? source : null, license, out List<string> added);
                        changes.AddRange(added);
                    }

                    foreach (string change in changes) report.Changes.Add(file.Key + ": " + change);
                    foreach (string flag in flags) report.Flags.Add(file.Key + " " + flag);
                    files[file.Key] = utf8.GetBytes(normalized);
                }
                else if (_TextExtensions.Contains(extension) || Path.GetFileName(file.Key).IndexOf('.') < 0)
                {
                    string text = utf8.GetString(file.Value);
                    string normalized = SkillImportNormalizer.NormalizeScript(text, out int changed);
                    scriptDashes += changed;
                    files[file.Key] = changed > 0 ? utf8.GetBytes(normalized) : file.Value;
                }
                else
                {
                    files[file.Key] = file.Value;
                }
            }

            if (scriptDashes > 0)
            {
                report.Changes.Add("scripts and data files: dashes replaced with hyphens (" + scriptDashes + ")");
            }

            return files;
        }

        private static void WritePackJson(string packFolder, string packId, string source, string? license)
        {
            string path = Path.Combine(packFolder, "pack.json");
            if (File.Exists(path))
            {
                return;
            }

            Directory.CreateDirectory(packFolder);
            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                id = packId,
                title = char.ToUpperInvariant(packId[0]) + packId.Substring(1),
                description = "Imported skills.",
                category = SkillCategories.Normalize(packId) ?? SkillCategories.General,
                source,
                license = license ?? string.Empty
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json + "\n");
        }

        private static string Clone(string url, string target, CancellationToken cancellationToken)
        {
            RunGit(new[] { "clone", "--depth", "1", "--quiet", url, target }, null, cancellationToken, 300000);
            try
            {
                return RunGit(new[] { "rev-parse", "HEAD" }, target, cancellationToken, 30000).Trim();
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
        }

        private static string RunGit(string[] arguments, string? workingDirectory, CancellationToken cancellationToken, int timeoutMs)
        {
            ProcessStartInfo info = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (workingDirectory != null) info.WorkingDirectory = workingDirectory;
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            Process process;
            try
            {
                process = Process.Start(info) ?? throw new InvalidOperationException("git could not be started.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new InvalidOperationException("git is not installed or not on PATH; clone the repository yourself and import the folder.");
            }

            using (process)
            {
                System.Threading.Tasks.Task<string> output = process.StandardOutput.ReadToEndAsync();
                System.Threading.Tasks.Task<string> error = process.StandardError.ReadToEndAsync();
                using (cancellationToken.Register(() => { try { process.Kill(true); } catch (Exception) { } }))
                {
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(true); } catch (Exception) { }
                        throw new InvalidOperationException("git " + arguments[0] + " timed out.");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("git " + arguments[0] + " failed: " + error.Result.Trim());
                }

                return output.Result;
            }
        }

        private static void TryDelete(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(folder, true);
            }
            catch (Exception)
            {
                // A leftover temp clone is harmless.
            }
        }

        #endregion
    }
}
