## Summary

<!-- What does this PR change and why? Link the issue it closes: "Closes #123". -->

## Type of change

- [ ] Bug fix
- [ ] Feature
- [ ] Refactor / cleanup
- [ ] Documentation
- [ ] Skill (bundled or pack)
- [ ] Build / CI

## Checklist

- [ ] `dotnet build src/Mux.sln --configuration Release` completes with **zero warnings**
- [ ] Console runner passes on both frameworks:
      `dotnet run --project src/Test.Automated/Test.Automated.csproj --configuration Release --framework net8.0`
      (and `--framework net10.0`)
- [ ] New or changed behavior is covered by a suite in `src/Test.Shared/Suites`
- [ ] Public types/members have XML documentation comments
- [ ] No suppressions added to `.csproj` or `Directory.Build.props`
- [ ] `CHANGELOG.md` updated under `## Unreleased` (user-visible changes)
- [ ] Bundled skill touched → `BUILTIN_SKILLS.md` regenerated with
      `python3 scripts/common/generate-builtin-skills.py`
- [ ] Documentation (`README.md`, `docs/`) updated where behavior changed

## Notes for reviewers

<!-- Trade-offs, follow-up work, areas you are unsure about. -->
