# Releasing

A release is a tag. CI builds, tests, packages and publishes it; nothing is built by hand.

## Before tagging

1. `dotnet test` is green on `main` and the CI badge agrees.
2. The add-in has been tried in OneNote from a Release build:
   `.\tools\register.ps1 -Install -Configuration Release`, then the checks in the README's
   "Tested with" table.
3. `CHANGELOG.md`: rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD`, add a fresh empty
   `## [Unreleased]` above it, and add the compare link at the bottom.
4. `Directory.Build.props`: set `<Version>` to `X.Y.Z` (or `X.Y.Z-preview.N`). The release
   workflow refuses a tag that does not match it.
5. Commit: `chore(release): X.Y.Z`.

## Tagging

```powershell
git tag -a vX.Y.Z -m "Md2OneNote X.Y.Z"
git push origin main vX.Y.Z
```

The Release workflow then:

- checks the tag against `<Version>`,
- runs `tools/build-release.ps1` (Release build, all tests, zip, `SHA256SUMS`),
- takes the release notes from the matching `CHANGELOG.md` section,
- creates the GitHub release, marked as a pre-release when the version has a suffix.

## After the release

- Open the release page and check the assets are there and the notes read well.
- Install from the published zip on a machine that has never seen a dev build (Windows Sandbox
  is enough) and import `tests/Md2OneNote.Core.Tests/Samples/kitchen-sink.md`.
- Announce where it makes sense.

## Version numbers

Semantic versioning. A change to the page output that makes previously imported files import
differently is a minor bump at least; a new registry key or a changed install location is a
major bump, because the uninstaller of the previous version does not know about it.

## Signing

Releases are not signed yet. When signing is set up (SignPath or Azure Trusted Signing), the
signing step goes between "Build, test, package" and "Create the GitHub release" in
`.github/workflows/release.yml`, and this section says which files are signed and how to verify
(`signtool verify /pa /v`).
