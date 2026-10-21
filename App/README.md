# HealthyHires App

React 19 + TypeScript + Vite frontend. Architecture decisions: [ADR-FRONTEND](../docs/architecture/ADR-FRONTEND.md).

## Structure ([ADR-F002](../docs/architecture/ADR-FRONTEND.md#adr-f002-application-architecture--feature-first-mirroring-backend-modules))

```text
src/
├── main.tsx          # entry point
├── app/              # app shell
│   ├── providers/    # Fluent, TanStack Query, i18n, MSAL providers
│   ├── router/       # public routes and lazy-loaded /staff routes
│   └── layout/       # page layouts
├── shared/           # code used by two or more features
│   ├── api/          # generated OpenAPI client and middleware
│   ├── ui/           # UI building blocks on Fluent primitives
│   └── lib/          # utilities
└── features/         # one folder per backend module
```

Features do not import each other's internals. Data flows **component → hook (TanStack Query) → generated API client → API**.

## Scripts

| Command          | Purpose                                |
| ---------------- | -------------------------------------- |
| `npm run dev`    | Start the dev server                   |
| `npm run build`  | Type-check and build the static bundle |
| `npm run lint`   | ESLint                                 |
| `npm run format` | Prettier                               |

## Local development

`npm run dev` starts the Vite dev server on its own and proxies `/api` and `/hubs` to the API at `API_URL`
(default `http://localhost:5180`), so the app stays same-origin as behind Caddy in production. Setup,
configuration and troubleshooting: [Getting Started with the Frontend](../docs/app/getting-started.md).
