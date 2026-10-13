# HealthyHires

Talent acquisition web application: anonymous applicants browse job offers and apply, and staff sign in
with Microsoft Entra ID to manage offers and applications. Start with the [documentation index](docs/README.md).

Common commands are wrapped in a [Just](https://github.com/casey/just) file; `just` lists them. Each
recipe is a documented command from [Development Architecture §3](docs/architecture/development-architecture.md#3-local-development),
so everything also works without `just`.

## Run the backend with Aspire

[Aspire](https://aspire.dev/)

### `just aspire-up`

Starts the API with its local dependencies and opens the Aspire dashboard with logs, traces and resource
health. Needs Docker or Podman and no secrets.

- PostgreSQL, Azurite (Blob Storage), Mailpit (e-mail) and ClamAV
- API Service, with its Scalar reference linked from the dashboard

The AppHost runs the backend only — the frontend is started on its own, so neither side's run depends on
the other. Ports, credentials, start order and troubleshooting: [Aspire Development Stack](docs/architecture/aspire-dev-stack.md).

## Run the frontend

### `just app`

Starts the Vite dev server, which proxies `/api` and `/hubs` to `API_URL` (default `http://localhost:5180`).
Needs Node and `npm ci` in `App/` once, and nothing from the backend toolchain. See
[Getting Started with the Frontend](docs/app/getting-started.md).

## Production-like edge (optional)

### `just app-build`, then `just edge`

Serves the built frontend and the API on one origin at `http://localhost:8088` through Caddy, with the
production routes, headers and Content Security Policy. Needs a running API. Use it to check the CSP or to
run ZAP, Playwright or k6 against the stack. Stop it with Ctrl+C.

## Deploy to Azure

The whole Azure setup is in `infra/`, `deploy/` and `scripts/azure/`, and works on any subscription from a
per-person `infra/azure.env`. Follow the [Azure Deployment Guide](docs/operations/azure-deployment-guide.md);
the `azure-*` recipes wrap its steps.

## Other Just commands

`build`, `test`, `clamav-reset`, `diagrams`, `md-tables`.

## Compress Markdown tables

Use `scripts/compress_markdown_tables.py` (or `just md-tables [path]`) to normalize table cells to one
space of padding. Separator cells use as many dashes as their column heading, with a minimum of three
dashes to keep the table valid:

```bash
python3 scripts/compress_markdown_tables.py docs/example.md
python3 scripts/compress_markdown_tables.py docs/
python3 scripts/compress_markdown_tables.py --stdout docs/example.md
```

By default, the script replaces the provided file. When given a directory, it recursively updates all
`.md` and `.markdown` files. Use `--stdout` to preview one file without changing it. When no input is
provided, the script reads Markdown from standard input and writes the result to standard output.
