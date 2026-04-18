# CLAUDE.md

Project-specific guidance for Claude Code working in this repo.

## Releases

Every push to every branch runs `.github/workflows/release.yml` and publishes artifacts. The rules:

- **`main`** → stable release. Tag: `v{Version}` (from `Directory.Build.props`). Docker tags: `:latest`, `:{Version}`, `:v{Version}`.
- **Any other branch** → pre-release. Tag: `v{Version}-{branch}.{run_number}`. Docker tags include a `:{branch}` convenience tag.

### Two gates on main (workflow fails if either is missed)

Before pushing or merging to `main`, **both** must be true:

1. **Version bumped** — `<Version>` in `Directory.Build.props` must not match an existing git tag. The workflow checks for `refs/tags/v{Version}` and fails if it already exists.
2. **CHANGELOG entry exists** — `CHANGELOG.md` must have a `## [{Version}]` section. The workflow extracts that section and uses it as the GitHub Release body. No section → workflow fails.

Pre-releases from feature branches don't require a CHANGELOG entry; they fall back to auto-generated notes.

### Release checklist when changing the codebase for a main merge

1. Decide the bump — patch for fixes and non-code changes, minor for features, major for breaking changes.
2. Update `Directory.Build.props` — change all four occurrences (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`). `AssemblyVersion` and `FileVersion` take a 4-part form (`0.3.2.0`).
3. Add a `## [X.Y.Z] - YYYY-MM-DD` section to `CHANGELOG.md` above the previous release. Use the Keep-a-Changelog subsections (`### Added`, `### Changed`, `### Fixed`, `### Removed`).
4. Commit + push. The workflow handles tagging, GHCR push, binary builds, and GitHub Release creation.

### Artifacts produced per release

- `blazecannon-v{Version}-win-x64.exe` — fully bundled single Windows executable (no unzip, no .NET install).
- `blazecannon-v{Version}-linux-x64.tar.gz` — self-contained Linux binary wrapped to preserve the `+x` bit.
- `ghcr.io/mathewulanowski/blazecannon:{tag}` — OCI image (linux/amd64).

### Not to do

- Don't create a git tag manually — the workflow creates it. Manual tags will collide with the next push.
- Don't edit `Directory.Build.props` without also adding the matching `CHANGELOG.md` section in the same commit. The two must move together.
- Don't use `generate_release_notes` as a substitute for the CHANGELOG. Auto-generation is the pre-release fallback; stable releases must be human-written.
