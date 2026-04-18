# Changelog

All notable changes to BlazeCannon are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.5.0] - 2026-04-18

### Changed — UX
- **Traffic Inspector is now the single stop for captured-message editing + replay.** Click a row in the traffic table and the detail panel on the right expands into the full editor. `/replay` is gone, "Replay" is out of the sidebar, and the stage-for-replay workflow is retired — selection *is* staging. The detail panel carries both tabs (see below) plus metadata, close-on-Esc, and a close-X in the header.

### Added
- **Detail-panel editor.** The merged panel hosts a PrimeNG `p-tabView` with two tabs:
  - **Raw** — editable textarea (read-only binary preview for MessagePack messages), Send button posts `POST /api/replay/send` through the live MITM session. Unchanged behaviour from the old Replay page, just relocated.
  - **Decoded** — for `Invocation` messages you can edit the hub method, invocation ID, and arguments (as a JSON array with live validation) in a human-readable form, then send them back through the live MITM session. A **Use MessagePack** toggle defaults on for binary messages and off for text — match what the target expects. Disabled for non-Invocation message types this release; a future release will expand coverage.
- **`POST /api/replay/encode-and-send`** — server-side encoding endpoint. Accepts `{ messageType, hubMethod, invocationId, arguments[], useMessagePack, sessionId }`, encodes the frame, and pipes it through the existing replay path. Returns `{ sentAt, byteLength, wireFormatBase64, rawText }` for auditability. Structured errors: 400 for malformed requests (non-Invocation type, empty hubMethod, non-array arguments), 409 for no-active-session.
- **`BlazorProtocolEncoder.EncodeInvocationMessagePack`** and `EncodeInvocationText` — first-class SignalR frame builders for Invocations. The MessagePack path produces `[VarInt length][MessagePack array]` frames byte-compatible with what Blazor Server's `blazorpack` hub expects; the text path produces a `{…}\x1E`-terminated JSON frame for text-protocol hubs. Decoder round-trip verified.
- **Replay over HTTP long-polling.** The MITM proxy now tracks long-poll sessions seen on `/_blazor?id=…` (host, hub path, session id) and `ReplayMessageAsync` falls back to a direct `POST` at the target's long-poll endpoint when no WebSocket session is open. `ActiveSessionCount` counts WS + long-poll together, `SessionOpened` fires on first registration, and the any-open-session fallback now correctly POSTs to the matched session's id (not the caller's unknown id). Replay works against real-world targets that fail WebSocket upgrade and fall back to long-polling — the same scenarios that originally drove the long-poll decode in v0.2.0.

### Known gaps (deferred)
- Decoded editing only supports **Invocation**. StreamItem / Completion / StreamInvocation / CancelInvocation / Ping / Close / Handshake are rejected with a 400 and shown as a disabled tab in the UI.
- MessagePack extension types (custom ext, DateTime ext-255) not in the encoder — avoid injecting extension-typed values.
- Binary (`bin`) arguments can't be expressed through the JSON textarea; the Raw tab is still the path for raw-byte tweaks.
- `streamIds` is always emitted as `[]` — no streaming support.

## [0.4.1] - 2026-04-18

### Documentation
- README rewritten for the post-migration architecture: updated Mermaid diagram (Angular → API → proxy), Projects table now lists `BlazeCannon.Api` and `web/`, replaced `BlazeCannon.App` references in run commands with `BlazeCannon.Api`, and bumped example version tags to `v0.4.0`.
- New "Dev — run the backend and frontend side-by-side" section covering `dotnet run --project BlazeCannon.Api` alongside `cd web && npm start`, including a note on the Angular dev proxy forwarding `/api` and `/hubs`.
- Production build snippet showing the single-file publish flags (`IncludeAllContentForSelfExtract`, `EnableCompressionInSingleFile`) plus `npm run build`.
- Chromium launch snippet now includes `--proxy-bypass-list="<-loopback>"` with a callout explaining why it's required when the target runs on the host loopback — without the flag, Chrome silently skips the proxy for `localhost` / `127.0.0.1`.
- "Reaching the target" table reshaped around BlazeCannon's runtime (native vs Docker) rather than assuming a container.

## [0.4.0] - 2026-04-18

### Changed — **BREAKING**
- Split the UI from the backend. `BlazeCannon.App` (Blazor Server) has been **removed entirely**. In its place:
  - **`BlazeCannon.Api`** — ASP.NET Core Web API + SignalR hub. Runs on the same dual-port Kestrel scheme as before (UI/API port 8080, MITM proxy port 5001). All captured-traffic, replay, proxy-status, and target-config interactions are now HTTP endpoints under `/api/*`.
  - **`web/`** — new Angular 17 app (standalone components + PrimeNG + `@microsoft/signalr`). Implements Dashboard, Traffic Inspector, and Replay at feature parity with the old Blazor UI (column reorder, filters, JSON/CSV export, staged replay queue, session-filtered live traffic). Scanner / Payload Workbench / Browser Engine are placeholder routes pending port.
- Rationale: the old architecture had BlazeCannon's own Blazor Server UI using the same `blazorpack` SignalR protocol it's built to attack — decoupling the UI lets the API stay headless and the frontend evolve independently.

### Added
- SignalR hub at `/hubs/traffic` broadcasting `MessageIntercepted`, `SessionOpened`, `SessionClosed`, `TrafficCleared`, and `StageChanged` events to subscribed clients.
- Typed API controllers (`TrafficController`, `ReplayController`, `ProxyController`, `TargetController`) with camelCase JSON + string enums.
- `TrafficFilter` service centralizing direction / type / session / search / limit query parameters across list and export endpoints.
- Angular dev proxy (`proxy.conf.json`) forwards `/api/**` and `/hubs/**` (with `ws: true`) to the .NET API, so `ng serve` and `dotnet run` can run side-by-side during development.

### Removed
- `BlazeCannon.App` project and all its Razor pages, shared components, services, and wwwroot assets. This is a breaking change — there is no in-place upgrade path; users pull the new Docker image or binaries from the `v0.4.0` release.

### Fixed
- Traffic Inspector table now scrolls properly when entries overflow the viewport. Replaced PrimeNG's `scrollHeight="flex"` (which silently broke through the nested flex + grid ancestors) with a calc-based fixed scroll region.

### Added — UX
- Arrow Up / Arrow Down navigate row selection in the Traffic Inspector instead of scrolling the page. Keys are ignored while a filter input is focused, and the newly-selected row is scrolled into view.

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
