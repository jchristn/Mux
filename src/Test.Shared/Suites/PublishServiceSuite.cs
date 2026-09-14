namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Publisher;
    using Mux.Publisher.Publishing;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="PublishService"/>: the self-contained publish argument list carries
    /// exactly the flags the packaging standard mandates, pack args are well-formed, and SHA-256 matches a
    /// known vector.
    /// </summary>
    public static class PublishServiceSuite
    {
        /// <summary>Builds the publish-service suite descriptor.</summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "PublishService",
                "Self-contained publish args, pack args, and checksums",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("PublishService", "PublishArgsCarryMandatedFlags", "Publish args match the standard's invocation", (CancellationToken ct) =>
                    {
                        List<string> args = PublishService.BuildPublishArgs("src/Mux.Desktop/Mux.Desktop.csproj", "net10.0", "win-x64", "out", "1.2.3");
                        string joined = string.Join(" ", args);
                        MuxAssert.Contains("publish", joined, "publish verb");
                        MuxAssert.Contains("-c Release", joined, "release config");
                        MuxAssert.Contains("-f net10.0", joined, "framework");
                        MuxAssert.Contains("-r win-x64", joined, "runtime");
                        MuxAssert.Contains("--self-contained true", joined, "self-contained");
                        MuxAssert.Contains("-p:PublishSingleFile=true", joined, "single file");
                        MuxAssert.Contains("-p:IncludeNativeLibrariesForSelfExtract=true", joined, "native libs self-extract");
                        MuxAssert.Contains("-p:DebugType=none", joined, "no debug symbols");
                        MuxAssert.Contains("-p:Version=1.2.3", joined, "version stamped");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PublishService", "PackArgsAreWellFormed", "Pack args target Release and an output dir", (CancellationToken ct) =>
                    {
                        List<string> args = PublishService.BuildPackArgs("src/Mux.Cli/Mux.Cli.csproj", "out/nuget", "9.9.9");
                        string joined = string.Join(" ", args);
                        MuxAssert.Contains("pack", joined, "pack verb");
                        MuxAssert.Contains("-c Release", joined, "release config");
                        MuxAssert.Contains("-p:Version=9.9.9", joined, "version");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PublishService", "Sha256MatchesKnownVector", "SHA-256 of 'abc' matches the known digest", (CancellationToken ct) =>
                    {
                        string digest = PublishService.ComputeSha256(Encoding.ASCII.GetBytes("abc"));
                        MuxAssert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", digest, "sha256(abc)");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("PublishService", "EnvExpansionResolvesSecrets", "Env references expand at execution time", (CancellationToken ct) =>
                    {
                        System.Environment.SetEnvironmentVariable("MUX_PUB_TEST_KEY", "s3cr3t");
                        MuxAssert.AreEqual("s3cr3t", Orchestrator.ExpandEnvironment("$MUX_PUB_TEST_KEY"), "bare var");
                        MuxAssert.AreEqual("a-s3cr3t-b", Orchestrator.ExpandEnvironment("a-${MUX_PUB_TEST_KEY}-b"), "braced var");
                        MuxAssert.AreEqual("plain", Orchestrator.ExpandEnvironment("plain"), "no expansion");
                        MuxAssert.AreEqual(string.Empty, Orchestrator.ExpandEnvironment("$MUX_PUB_UNSET_XYZ"), "undefined -> empty");
                        System.Environment.SetEnvironmentVariable("MUX_PUB_TEST_KEY", null);
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
