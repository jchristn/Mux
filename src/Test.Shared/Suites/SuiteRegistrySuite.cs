namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the suite registry itself: every registered suite belongs to exactly one named group,
    /// suite ids are unique, and group ids are well-formed. Keeps <see cref="MuxSuites.All"/> and
    /// <see cref="MuxSuites.Groups"/> from drifting apart as suites are added. Runs on every runner
    /// like any other suite.
    /// </summary>
    public static class SuiteRegistrySuite
    {
        /// <summary>The suite identifier used in results and filters.</summary>
        public const string SuiteId = "SuiteRegistry";

        /// <summary>
        /// Creates the registry-verification suite with fresh descriptors.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(SuiteId, "Suite registry: group coverage, id uniqueness, and group hygiene", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(SuiteId, "GroupsCoverEverySuite", "The union of all groups equals the suite list, so every suite is reachable by group", (CancellationToken ct) =>
                {
                    HashSet<string> all = SuiteIds(MuxSuites.All);
                    HashSet<string> grouped = new HashSet<string>(StringComparer.Ordinal);
                    foreach (MuxSuites.GroupInfo group in MuxSuites.Groups)
                    {
                        grouped.UnionWith(SuiteIds(group.CreateSuites()));
                    }

                    List<string> ungrouped = new List<string>();
                    foreach (string id in all)
                    {
                        if (!grouped.Contains(id)) ungrouped.Add(id);
                    }

                    List<string> unknown = new List<string>();
                    foreach (string id in grouped)
                    {
                        if (!all.Contains(id)) unknown.Add(id);
                    }

                    MuxAssert.IsTrue(ungrouped.Count == 0, "suites missing from every group: " + string.Join(", ", ungrouped));
                    MuxAssert.IsTrue(unknown.Count == 0, "groups reference unregistered suites: " + string.Join(", ", unknown));
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor(SuiteId, "SuiteIdsAreUnique", "No suite id appears twice in the registry", (CancellationToken ct) =>
                {
                    AssertNoDuplicates(MuxSuites.All, "registry");
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor(SuiteId, "SuitesAppearInExactlyOneGroup", "A suite belongs to exactly one group so group runs never overlap", (CancellationToken ct) =>
                {
                    Dictionary<string, string> owners = new Dictionary<string, string>(StringComparer.Ordinal);
                    List<string> repeats = new List<string>();
                    foreach (MuxSuites.GroupInfo group in MuxSuites.Groups)
                    {
                        AssertNoDuplicates(group.CreateSuites(), "group " + group.Id);
                        foreach (string id in SuiteIds(group.CreateSuites()))
                        {
                            if (owners.TryGetValue(id, out string? owner))
                            {
                                repeats.Add(id + " (" + owner + ", " + group.Id + ")");
                            }
                            else
                            {
                                owners[id] = group.Id;
                            }
                        }
                    }

                    MuxAssert.IsTrue(repeats.Count == 0, "suites in more than one group: " + string.Join(", ", repeats));
                    return Task.CompletedTask;
                }),

                new TestCaseDescriptor(SuiteId, "GroupIdsAreWellFormed", "Group ids are unique, lowercase kebab-case, and resolve through FindGroup", (CancellationToken ct) =>
                {
                    HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    Regex wellFormed = new Regex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.Compiled);
                    foreach (MuxSuites.GroupInfo group in MuxSuites.Groups)
                    {
                        MuxAssert.IsTrue(seen.Add(group.Id), "duplicate group id " + group.Id);
                        MuxAssert.IsTrue(wellFormed.IsMatch(group.Id), "group id is not lowercase kebab-case: " + group.Id);
                        MuxAssert.IsTrue(group.Description.Length > 0, "group " + group.Id + " has no description");
                        MuxAssert.AreEqual(group.Id, MuxSuites.FindGroup(group.Id.ToUpperInvariant())!.Id, "FindGroup is case-insensitive for " + group.Id);
                        MuxAssert.IsTrue(MuxSuites.Group(group.Id).Count > 0, "group " + group.Id + " has no suites");
                    }

                    MuxAssert.IsTrue(MuxSuites.FindGroup("no-such-group") == null, "unknown group id returns null");
                    MuxAssert.Throws<ArgumentException>(() => MuxSuites.Group("no-such-group"), "unknown group id throws with the valid ids");
                    return Task.CompletedTask;
                })
            });
        }

        private static HashSet<string> SuiteIds(IEnumerable<TestSuiteDescriptor> suites)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TestSuiteDescriptor suite in suites)
            {
                ids.Add(suite.SuiteId);
            }

            return ids;
        }

        private static void AssertNoDuplicates(IReadOnlyList<TestSuiteDescriptor> suites, string scope)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TestSuiteDescriptor suite in suites)
            {
                MuxAssert.IsTrue(seen.Add(suite.SuiteId), "duplicate suite id " + suite.SuiteId + " in " + scope);
            }
        }
    }
}
