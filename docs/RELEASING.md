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
- runs `tools/build-release.ps1` stage by stage (Release build, all tests, zip, installer,
  `SHA256SUMS`), signing in between when signing is set up (see below),
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

The release workflow signs through [SignPath](https://signpath.io) when the repository has
these three settings (Settings > Secrets and variables > Actions), and publishes unsigned
otherwise, saying so in the release notes:

| Setting | Kind | Value |
|---|---|---|
| `SIGNPATH_ORGANIZATION_ID` | variable | the organization id shown in SignPath |
| `SIGNPATH_PROJECT_SLUG` | variable | the slug of the SignPath project |
| `SIGNPATH_API_TOKEN` | secret | an API token of a SignPath user with the Submitter role |

One-time setup in SignPath, once SignPath Foundation has approved the application:

1. A project for this repository, with GitHub Actions linked as its trusted build system (the
   SignPath GitHub App installed on the repository) and origin verification on.
2. Two artifact configurations with the slugs `assemblies` and `installer`, from
   `installer/signpath/assemblies.xml` and `installer/signpath/installer.xml`.
3. The `release-signing` policy, which SignPath Foundation creates, with the maintainer as
   approver and the token's user as submitter.

What happens on a tag, in `.github/workflows/release.yml`:

1. `build-release.ps1 -Stage Build`: Release build, tests, `artifacts/payload`.
2. `Md2OneNote.*.dll` from the payload go to SignPath as one run artifact. The workflow waits
   up to an hour while the approver confirms the request in SignPath (an email arrives). Each
   returned file is checked with `Get-AuthenticodeSignature` and replaces the unsigned one.
3. `-Stage Package`: the zip and the installer are built from the signed payload.
4. The installer goes to SignPath the same way, with a second approval, and is replaced by the
   signed file.
5. `-Stage Checksums`, then the GitHub release.

Not signed: the third-party libraries in the payload (Markdig, ColorCode, WebView2, the
`System.*` shims), which ship as their authors publish them, and the uninstaller Inno Setup
extracts at install time. Neither reaches Windows SmartScreen, which only looks at files that
were downloaded.

To check a download:

```powershell
Get-AuthenticodeSignature .\Md2OneNote-X.Y.Z-setup.exe | Format-List Status, SignerCertificate
```

`signtool verify /pa /v Md2OneNote-X.Y.Z-setup.exe` does the same with the Windows SDK.

The README's "Code signing policy" section carries the wording SignPath Foundation asks for;
keep it when editing the README.
