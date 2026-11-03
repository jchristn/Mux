namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Mux.Core.Skills;

    /// <summary>
    /// Seeds the production catalog once in memory, then gives script tests private copies of only
    /// the skills they execute. Seeding/upgrade tests still exercise the real full-catalog seeder.
    /// </summary>
    internal static class SkillScriptFixture
    {
        private static readonly Lazy<Dictionary<string, Dictionary<string, (byte[] Bytes, UnixFileMode? Mode)>>> Files = new(ReadCatalog);

        internal static void EnsureSkill(string directory, string id)
        {
            string target = Path.Combine(directory, id);
            if (Directory.Exists(target)) return;
            foreach (var file in Files.Value[id])
            {
                string path = Path.Combine(target, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, file.Value.Bytes);
                if (!OperatingSystem.IsWindows() && file.Value.Mode.HasValue)
                    File.SetUnixFileMode(path, file.Value.Mode.Value);
            }
        }

        private static Dictionary<string, Dictionary<string, (byte[] Bytes, UnixFileMode? Mode)>> ReadCatalog()
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-script-catalog-" + Guid.NewGuid().ToString("N"));
            try
            {
                DefaultSkillLibrary.SeedInto(root);
                var result = new Dictionary<string, Dictionary<string, (byte[] Bytes, UnixFileMode? Mode)>>(StringComparer.Ordinal);
                foreach (string skill in Directory.EnumerateDirectories(root))
                {
                    var files = new Dictionary<string, (byte[] Bytes, UnixFileMode? Mode)>(StringComparer.Ordinal);
                    foreach (string path in Directory.EnumerateFiles(skill, "*", SearchOption.AllDirectories))
                        files[Path.GetRelativePath(skill, path)] = (File.ReadAllBytes(path), OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(path));
                    result[Path.GetFileName(skill)] = files;
                }
                return result;
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
