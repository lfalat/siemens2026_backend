# Getting Started with the Frontend

How to set up and run the React frontend in `App/` on a developer machine. Run every command in `App/` unless it says otherwise. The decisions behind this setup are in [ADR-FRONTEND.md](../architecture/ADR-FRONTEND.md); this page is the how-to.

## 1. Prerequisites

- **Node.js** `^20.19.0` or `>=22.12.0`, which Vite 8 requires, with npm.
- **The API**, only for screens that call it. The frontend starts without it (§4). Run the backend separately, either through the Aspire AppHost or as `ApiService` on its own ([development-architecture.md §3.1](../architecture/development-architecture.md#31-aspire-backend-with-its-local-dependencies)). Nothing in the frontend starts or waits for it.

## 2. Install dependencies

```bash
npm ci
```

`npm ci` installs exactly what `package-lock.json` records ([ADR-F001](../architecture/ADR-FRONTEND.md#adr-f001-application-platform--react-typescript-and-vite)). Run it after cloning and whenever the lockfile changes. Use `npm install <package>` only to add or upgrade a package, and commit the updated lockfile with it.

## 3. Configure the API address (optional)

The dev server reads one setting, from `App/.env` or the environment:

| Variable | Default | Purpose |
| --- | --- | --- |
| `API_URL` | `http://localhost:5180` | Where the dev server proxies `/api` and `/hubs` |

The default is the API's HTTP port both under the AppHost and when `ApiService` runs on its own, so most setups need no `.env` at all. To point elsewhere, copy the template and edit it:

```bash
cp .env.example .env
```

`.env` is gitignored. `API_URL` has no `VITE_` prefix, so Vite keeps it in `vite.config.ts` and never puts it in the bundle.

The Entra ID settings the app will need for staff sign-in are not configured here: they come at runtime from `/config.json`, which Caddy generates in production ([ADR-F013](../architecture/ADR-FRONTEND.md#adr-f013-build-and-delivery--static-bundle-served-by-caddy)).

## 4. Run the dev server

```bash
npm run dev
```

Open `http://localhost:5173`. Vite prints the address; if 5173 is taken it picks the next free port. Changes reload in the browser immediately.

### 4.1 How API calls reach the backend

In production Caddy serves the app and the API on one origin. The dev server keeps that true locally ([ADR-F015](../architecture/ADR-FRONTEND.md#adr-f015-local-development--vite-dev-server-with-an-api-proxy)):

| Path | Goes to |
| --- | --- |
| `/api/*` | `API_URL`, e.g. `http://localhost:5180/api/v1/...` |
| `/hubs/*` | `API_URL`, WebSocket upgrades included (SignalR, [ADR-F008](../architecture/ADR-FRONTEND.md#adr-f008-real-time-communication--microsoftsignalr)) |
| everything else | the app itself |

What this means when writing code:

- **Call relative paths** such as `/api/v1/job-offers`, never `http://localhost:5180/...`. The same code then works on the dev server, in `vite preview` and behind Caddy.
- **No CORS is involved.** The browser only talks to the dev server, so the API has no CORS policy and needs none.

### 4.2 Working without the backend

Without a running API, every `/api` call answers **502**, and the terminal running Vite logs `http proxy error` with `ECONNREFUSED`. Start the API, or check `API_URL` if it is already running. Mock Service Worker handlers will let screens run against mocked responses instead, once the first features call the API ([ADR-F012](../architecture/ADR-FRONTEND.md#adr-f012-testing--vitest-react-testing-library-and-playwright)).

## 5. Check a production build

```bash
npm run build
npm run preview
```

`npm run build` type-checks and writes the static bundle to `App/dist`. `npm run preview` serves that bundle on `http://localhost:4173`, with the same `/api` and `/hubs` proxy as the dev server.

Neither the dev server nor `vite preview` applies the production headers and Content Security Policy; hot reload alone needs an inline script the CSP forbids. To check those, or to run ZAP, Playwright or k6, serve the build through the local Caddy ([development-architecture.md §3.3](../architecture/development-architecture.md#33-production-like-edge-optional)).

## 6. Lint and format

```bash
npm run lint
npm run format
```

- `npm run lint` runs ESLint with the TypeScript and React Hooks rules ([ADR-F014](../architecture/ADR-FRONTEND.md#adr-f014-code-quality--eslint-prettier-and-dependency-scanning)).
- `npm run format` rewrites files with Prettier; `npm run format:check` only reports, as CI does.
- Lint and type errors fail the build, so run both before review.

Vitest, React Testing Library and Playwright ([ADR-F012](../architecture/ADR-FRONTEND.md#adr-f012-testing--vitest-react-testing-library-and-playwright)) are not set up yet; their commands will be added here with the first tests.

## 7. Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `/api` calls answer 502; Vite logs `http proxy error … ECONNREFUSED` | No API on `API_URL` | Start the API, or correct `API_URL` in `.env` and restart the dev server |
| A changed `API_URL` has no effect | `vite.config.ts` is read once, at startup | Restart `npm run dev` |
| The app opens on a port other than 5173 | 5173 was already in use | Use the address Vite prints, or stop the other process |
| `npm ci` fails with `package.json` and `package-lock.json` out of sync | A dependency was added without updating the lockfile | Run `npm install`, then commit `package-lock.json` |
