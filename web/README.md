# BlazeCannon Web

Angular UI for the BlazeCannon MITM proxy.

## Stack

- Angular 17 (standalone components, no NgModules)
- PrimeNG 17 with the `lara-dark-blue` theme
- `@microsoft/signalr` for the live traffic hub
- SCSS for styling

## Development

```bash
cd web
npm install
npm start
```

`npm start` runs `ng serve --proxy-config proxy.conf.json`, which forwards:

- `/api/**` → `http://localhost:8080`
- `/hubs/**` → `http://localhost:8080` (with WebSocket upgrade)

Start the BlazeCannon backend on port `8080` first, then open
`http://localhost:4200` in your browser.

## Build

```bash
npm run build
```

Produces `dist/blazecannon-web/` (browser bundle). The .NET host is expected
to serve these files in follow-up work — this scaffold does not yet integrate
with the .NET Dockerfile.

## Layout

```
src/app/
  app.component.*        — shell with sidebar + router-outlet
  app.config.ts          — standalone providers (router, http, animations)
  app.routes.ts          — lazy route table
  models/                — shared TS types (BlazorMessage etc.)
  services/              — SignalRService + typed HTTP clients
  pages/
    dashboard/           — GET /api/proxy/status + live counters
    traffic-inspector/   — p-table with reorderable cols, filters, export
    replay/              — 3-pane editor + session-filtered live feed
    placeholder/         — "Coming soon" for Scanner, Workbench, Browser
```

## Backend contract

See the task brief. This UI assumes:

- REST base: `/api`
- SignalR hub: `/hubs/traffic`
- Enum values serialized as strings (e.g. `"ClientToServer"`, `"RenderBatch"`)
- `BlazorMessage` JSON in camelCase matching `src/app/models/blazor-message.model.ts`
