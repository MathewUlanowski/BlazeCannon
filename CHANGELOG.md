# Changelog

All notable changes to BlazeCannon are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.3.2] - 2026-04-18

### Added
- `CHANGELOG.md` — release notes are now extracted from this file at release time and shown on the GitHub Release page.

### Changed
- Release workflow requires a `## [X.Y.Z]` section in `CHANGELOG.md` before publishing a stable release from `main`. The build fails with a clear error if the section is missing.
- Pre-releases from feature branches fall back to auto-generated notes when no changelog entry is present, so branch pushes stay zero-friction.

## [0.3.1] - 2026-04-18

### Changed
- Windows release asset is now a single `blazecannon-vX.Y.Z-win-x64.exe`. All content (`wwwroot/`, configs) is embedded via `IncludeAllContentForSelfExtract` + `EnableCompressionInSingleFile` — download and run, no unzip step.
- Linux release remains a `.tar.gz` wrapper so the executable bit survives a direct download from GitHub.

### Removed
- PDB symbols are no longer shipped with release binaries (`DebugType=None`) — shrinks artifact size.

## [0.3.0] - 2026-04-18

### Added
- GitHub Actions release workflow that publishes on every push:
  - `main` → stable release tagged `v{Version}`, Docker tags `:latest` and `:{Version}`.
  - Any other branch → pre-release tagged `v{Version}-{branch}.{run}`, with a convenience `:{branch}` Docker tag.
- Multi-platform distribution: self-contained `win-x64` and `linux-x64` binaries attached to each release.
- Container images published to `ghcr.io/mathewulanowski/blazecannon`.

## [0.2.0] - 2026-04-18

### Added
- MITM forward proxy middleware (`MitmProxyMiddleware`) listening on its own port. Handles both WebSocket relay and SignalR long-polling body decode so Blazor traffic is captured even when the client falls back from WebSocket.
- `Host`, `HubPath`, and `Transport` fields on `BlazorMessage`, populated by both transport paths.
- Traffic Inspector UI improvements: draggable/reorderable columns (persisted via `localStorage`), new Host / Blazor Hub / Transport columns, JSON + CSV export buttons that respect the active filters.
- Documentation for using the proxy across Docker Desktop (`host.docker.internal`) and Linux bridge scenarios, including a Mermaid flow diagram.

## [0.1.0] - initial

- Initial project layout with MIT license.
