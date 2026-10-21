# Decision Log (ADR) for Frontend Application

This document captures architecture decisions specific to the React frontend application. It intentionally excludes platform-wide infrastructure decisions that belong in the main ADR log.

Each entry follows the same structure:

- Title
- Status
- Context
- Decision
- Consequences
- Dates & Owner

---

## ADR-F001: Application Platform — React, TypeScript and Vite

Status: Accepted

Context:

- HealthyHires is used only through web browsers ([ADD §2](ADD.md#2-purpose)); there are no mobile or desktop clients and no offline requirement.

Decision:

- Build the frontend as a single-page application with React 19, TypeScript in strict mode and Vite, using npm with a committed lockfile.
- No offline mode and no PWA: the application requires a connection to the API.

Consequences:

- One codebase and one build output: static files served by Caddy ([ADR-F013](#adr-f013-build-and-delivery--static-bundle-served-by-caddy)).
- TypeScript types for API calls come from the OpenAPI document, so most contract breaks fail at compile time ([ADR-F005](#adr-f005-api-client--generated-from-openapi)).

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F002: Application Architecture — Feature-First, Mirroring Backend Modules

Status: Accepted

Context:

- The backend is a modular monolith ([ADR-A001](ADR-ASPNET.md#adr-a001-application-architecture--modular-monolith)); the frontend should stay easy to navigate as features grow.

Decision:

- Organize `src/` into `app/` (providers, router, layout), `shared/` (API client, UI building blocks, utilities) and `features/<module>/`, one folder per backend module.
- Data flow: **component → hook (TanStack Query) → generated API client → API**.

Consequences:

- Features must not import each other's internals; shared code is promoted to `shared/` only when a second consumer exists.
- A backend module and its frontend feature can be found by the same name.

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F003: State Management — TanStack Query for Server State

Status: Accepted

Context:

- Almost all state in HealthyHires is server data (job offers, applications, files); only a little is purely client-side UI state.

Decision:

- Use TanStack Query for all server state: fetching, caching, invalidation and mutations.
- Keep client state in React state and context; add no global store library unless a real need appears.

Consequences:

- Loading, error and retry handling are consistent across screens.
- Query keys and invalidation rules must be defined per feature to avoid stale data after mutations.

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F004: Routing — React Router with Public and Staff Areas

Status: Accepted

Context:

- The application has two audiences: anonymous applicants and signed-in staff ([ADD §2](ADD.md#2-purpose)).

Decision:

- Use React Router with two route trees: a public area (job listings, application form) and a staff area under `/staff` that requires sign-in.
- Lazy-load the staff area, so applicants never download staff code or the authentication library.

Consequences:

- The public area must work without MSAL loaded.
- Route guards only improve UX; the API remains the authority for every permission check.

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F005: API Client — Generated from OpenAPI

Status: Accepted

Context:

- The API publishes an OpenAPI document as its contract ([ADR-A004](ADR-ASPNET.md#adr-a004-api-style--rest-openapi-scalar-and-versioning)); hand-written request and response types drift from it.

Decision:

- Generate TypeScript types from the OpenAPI document with `openapi-typescript` and call the API through `openapi-fetch`.
- CI regenerates the types and fails if the committed client differs from the current document.

Consequences:

- API changes surface as TypeScript compile errors in the frontend.
- Middleware in the client attaches the access token and an `Idempotency-Key` to commands ([ADR-A016](ADR-ASPNET.md#adr-a016-idempotency--idempotency-key-middleware)).
- Read-only `POST` endpoints ([ADR-A015](ADR-ASPNET.md#adr-a015-read-only-endpoints-with-a-request-body--post-now-query-later)) get no idempotency key.

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F006: Authentication — MSAL React with Entra ID

Status: Accepted

Context:

- Staff sign in with their company Entra ID accounts ([ADR-004](ADR.md#adr-004-identity-provider--microsoft-entra-id-oidc)); applicants never sign in.

Decision:

- Use `@azure/msal-react` with the authorization-code flow and PKCE, as a public client without a secret ([iam.md §2](iam.md#2-staff-users--microsoft-entra-id)).
- Store the MSAL cache in `sessionStorage` and request access tokens silently before each API call.

Consequences:

- Tokens are readable by JavaScript, so XSS protection matters: a strict Content Security Policy is set by Caddy ([ADR-F013](#adr-f013-build-and-delivery--static-bundle-served-by-caddy)).
- Closing the browser tab ends the local session; the Entra session usually signs the user back in without a password prompt.
- Logout uses MSAL's logout redirect, which also ends the Entra session in that browser.

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F007: Applicant Flow — ALTCHA and Direct Blob Uploads

Status: Accepted

Context:

- Applicants submit applications anonymously with a CV and cover letter; the backend protects this with ALTCHA and direct uploads through SAS URLs ([ADR-022](ADR.md#adr-022-bot-protection--altcha-on-the-application-form), [ADR-A010](ADR-ASPNET.md#adr-a010-file-storage--azure-blob-storage-via-azure-sdk)).

Decision:

- Use the official `altcha` web component on the application form; the challenge is solved in the browser before submission.
- Upload files directly to Blob Storage with `fetch` and the SAS URL returned by the API, then confirm the upload to the API.

Consequences:

- The form shows upload progress and a "scanning" state until ClamAV has checked the file.
- Each submission uses a fresh `crypto.randomUUID()` as its idempotency key, reused only when retrying the same submission.
- File size and type are checked in the browser for fast feedback, but the API's checks remain authoritative.

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F008: Real-Time Communication — @microsoft/signalr

Status: Accepted

Context:

- The backend pushes real-time updates with SignalR ([ADR-A014](ADR-ASPNET.md#adr-a014-real-time-communication--signalr)), e.g. when a new application arrives.

Decision:

- Use the official `@microsoft/signalr` client in the staff area, passing the access token through `accessTokenFactory`.
- Real-time events invalidate the matching TanStack Query cache entries instead of updating state directly.

Consequences:

- The connection exists only for signed-in staff and is stopped on logout.
- The UI must keep working, with manual refresh, when the connection drops.

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F009: UI System — Fluent UI React v9

Status: Accepted

Context:

- Staff work in Microsoft 365 every day; the product needs accessible, consistent components without building a design system from scratch.

Decision:

- Use Fluent UI React v9 as the component library for both the public and staff areas, with a HealthyHires theme.

Consequences:

- The staff area looks familiar to Microsoft 365 users, and components come with accessibility built in.
- Custom components are built from Fluent primitives and tokens rather than a second styling system.
- Light and dark themes come from Fluent's theme tokens.

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F010: Forms and Validation — React Hook Form and Zod

Status: Accepted

Context:

- The application form and staff forms need client-side validation that matches the API's rules ([ADR-A008](ADR-ASPNET.md#adr-a008-validation--native-aspnet--net-10-validation)).

Decision:

- Use React Hook Form for form state and Zod schemas for validation.
- Map API validation errors (RFC 9457 problem details) back to the matching form fields.

Consequences:

- Client validation is a convenience; the API stays the authority, and its errors are always shown.
- Zod schemas must be kept in line with the API's validation rules when either changes.

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F011: Localization — Slovak Only

Status: Accepted

Context:

- The UI is used by a Slovak company and its Slovak applicants; strings appear in forms, validation messages and notifications.

Decision:

- The UI is Slovak only, with no translation layer.

Consequences:

- Strings are written in Slovak where they are used.
- API errors are shown as the Slovak `detail` and `errors` the API writes ([ADR-A017](ADR-ASPNET.md#adr-a017-error-messages--slovak-detail-written-by-the-api)).
- Adding a language later means introducing a translation layer then.

Dates & Owner:

- Decided: 21. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F012: Testing — Vitest, React Testing Library and Playwright

Status: Accepted

Context:

- Components and hooks need fast tests; complete flows such as sign-in and application upload need a real browser.

Decision:

- Use Vitest with React Testing Library for unit and component tests, and Mock Service Worker (MSW) to mock the API.
- Use Playwright for end-to-end tests against the local stack ([ADR-019](ADR.md#adr-019-end-to-end-testing--playwright)).

Consequences:

- MSW handlers use the generated API types, so mocks break when the contract changes.
- The direct upload and ClamAV flow is covered only by Playwright, because it needs Azurite and ClamAV.

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F013: Build and Delivery — Static Bundle Served by Caddy

Status: Accepted

Context:

- There is no CDN; Caddy is the public edge ([ADR-003](ADR.md#adr-003-edge--reverse-proxy--caddy)) and already proxies the API.

Decision:

- Build the frontend with Vite in CI and copy the static bundle into the Caddy image, which serves it on the same hostname as `/api` ([container-deployment.md §1.2](container-deployment.md#12-caddy-healthyhires-caddy)).
- Load environment-specific settings (Entra tenant and client IDs, API scope) at startup from a `config.json` served by Caddy, so one build runs in every environment.
- Caddy sets the security headers: Content Security Policy, `X-Content-Type-Options` and `Referrer-Policy`.

Consequences:

- Same origin means no CORS for the API; only Blob Storage needs CORS for direct uploads and downloads.
- Hashed asset file names are cached long-term, while `index.html` and `config.json` are never cached.
- The CSP must allow `login.microsoftonline.com` for sign-in and the Blob Storage endpoint for uploads and downloads.

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F014: Code Quality — ESLint, Prettier and Dependency Scanning

Status: Accepted

Context:

- The frontend pulls in many npm packages, and consistent code style keeps reviews focused on behaviour.

Decision:

- Use ESLint with `typescript-eslint` and the React Hooks rules, and Prettier for formatting; both run in CI.
- npm dependencies are scanned by Trivy together with the images ([ADR-008](ADR.md#adr-008-security--supply-chain-tools--trivy-openscap-aide-cosign)).

Consequences:

- Lint or type errors fail the build.
- Critical and High findings in npm dependencies block a release, like findings in images.

Dates & Owner:

- Decided: 11. 10. 2026
- Owner: Marek Dvorsky

---

## ADR-F015: Local Development — Vite Dev Server with an API Proxy

Status: Accepted

Context:

- In production Caddy serves the app and the API on one origin ([ADR-F013](#adr-f013-build-and-delivery--static-bundle-served-by-caddy)); locally the Vite dev server and the API run on different ports, and the frontend must not depend on the backend's run.

Decision:

- Run the frontend on its own with the Vite dev server, which proxies `/api` and `/hubs` to `API_URL` (default `http://localhost:5180`); `vite preview` uses the same proxy.

Consequences:

- The browser sees one origin, as in production: the frontend calls relative paths everywhere, and the API needs no CORS policy.
- The frontend never starts or waits for the backend; without a running API, calls fail until MSW handlers mock them ([ADR-F012](#adr-f012-testing--vitest-react-testing-library-and-playwright)).
- The dev server cannot apply the production CSP, because hot reload needs an inline script; the CSP is checked against a build through the local Caddy ([development-architecture.md §3.3](development-architecture.md#33-production-like-edge-optional)).

Dates & Owner:

- Decided: 12. 10. 2026
- Owner: Marek Dvorsky

---
