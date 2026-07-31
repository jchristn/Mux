namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;

    /// <summary>The default .NET build and quality skills seeded into ~/.mux/skills.</summary>
    public static class DefaultDotnetSkills
    {
        /// <summary>Returns the .NET default skills, keyed by id.</summary>
        /// <returns>The skills as id to SKILL.md content.</returns>
        public static IReadOnlyDictionary<string, string> All()
        {
            Dictionary<string, string> skills = new Dictionary<string, string>(StringComparer.Ordinal);

            skills["dotnet-build"] = DefaultSkillBuilder.Build(
                "dotnet-build", "Build the .NET solution", "Build the current .NET project or solution.", true, "dotnet, build",
                "The user wants to build the current .NET project or solution.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("release", "Build in Release.", "pwsh",
@"$ErrorActionPreference = 'Stop'
dotnet build -c Release --nologo
"),
                    new DefaultSkillCommandDef("debug", "Build in Debug.", "pwsh",
@"$ErrorActionPreference = 'Stop'
dotnet build -c Debug --nologo
")
                });

            skills["dotnet-test"] = DefaultSkillBuilder.Build(
                "dotnet-test", "Run the .NET tests", "Run the test suite for the current .NET project or solution.", true, "dotnet, test",
                "The user wants to run the .NET test suite, optionally filtered.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("all", "Run every test.", "pwsh",
@"$ErrorActionPreference = 'Stop'
dotnet test --nologo
"),
                    new DefaultSkillCommandDef("filter", "Run tests matching a filter expression.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -eq 0) { Write-Error 'A filter expression is required.'; exit 1 }
$filter = $args -join ' '
dotnet test --nologo --filter $filter
")
                });

            skills["dotnet-format"] = DefaultSkillBuilder.Build(
                "dotnet-format", "Format the .NET code", "Apply or verify code style using dotnet format.", true, "dotnet, style",
                "The user wants to format the code or verify that it is already formatted.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("apply", "Apply formatting fixes in place.", "pwsh",
@"$ErrorActionPreference = 'Stop'
dotnet format
"),
                    new DefaultSkillCommandDef("verify", "Fail if any formatting changes are needed.", "pwsh",
@"$ErrorActionPreference = 'Stop'
dotnet format --verify-no-changes
")
                });

            skills["dotnet-restore"] = DefaultSkillBuilder.Build(
                "dotnet-restore", "Restore .NET dependencies", "Restore NuGet packages for the current project or solution.", true, "dotnet, build",
                "The user wants to restore NuGet dependencies.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("restore", "Restore all packages.", "pwsh",
@"$ErrorActionPreference = 'Stop'
dotnet restore
")
                });

            skills["dotnet-outdated"] = DefaultSkillBuilder.Build(
                "dotnet-outdated", "Inspect package health", "List outdated or vulnerable NuGet packages.", false, "dotnet, deps",
                "The user wants to see which dependencies are outdated or vulnerable.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("list", "List outdated packages.", "pwsh",
@"$ErrorActionPreference = 'Stop'
dotnet list package --outdated
"),
                    new DefaultSkillCommandDef("vulnerable", "List packages with known vulnerabilities.", "pwsh",
@"$ErrorActionPreference = 'Stop'
dotnet list package --vulnerable
")
                });

            skills["dotnet-pack"] = DefaultSkillBuilder.Build(
                "dotnet-pack", "Pack NuGet packages", "Produce NuGet packages from the current project or solution.", true, "dotnet, packaging",
                "The user wants to create NuGet packages.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("pack", "Pack in Release.", "pwsh",
@"$ErrorActionPreference = 'Stop'
dotnet pack -c Release --nologo
")
                });

            skills["dotnet-publish"] = DefaultSkillBuilder.Build(
                "dotnet-publish", "Publish the .NET app", "Publish the application, framework-dependent or self-contained.", true, "dotnet, packaging",
                "The user wants to publish the application for deployment.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("framework-dependent", "Publish a framework-dependent build.", "pwsh",
@"$ErrorActionPreference = 'Stop'
dotnet publish -c Release --nologo
"),
                    new DefaultSkillCommandDef("self-contained", "Publish a self-contained build for a runtime identifier.", "pwsh",
@"$ErrorActionPreference = 'Stop'
if ($args.Count -eq 0) { Write-Error 'A runtime identifier is required (e.g. win-x64, linux-x64).'; exit 1 }
$rid = $args[0]
dotnet publish -c Release --nologo --self-contained -r $rid
")
                });

            skills["ci-repro"] = DefaultSkillBuilder.Build(
                "ci-repro", "Reproduce the CI pipeline", "Run the Release build and tests the way CI does.", true, "dotnet, ci",
                "The user wants to reproduce the CI build and test run locally.",
                new List<DefaultSkillCommandDef>
                {
                    new DefaultSkillCommandDef("run", "Build in Release, then run the tests.", "pwsh",
@"$ErrorActionPreference = 'Stop'
dotnet build -c Release --nologo
dotnet test -c Release --nologo
")
                });

            return skills;
        }
    }
}
