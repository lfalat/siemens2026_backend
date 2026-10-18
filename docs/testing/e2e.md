# End-to-End Tests

Browser tests of the whole application with Playwright for .NET ([ADR-019](../architecture/ADR.md#adr-019-end-to-end-testing--playwright)): the React frontend, Entra ID sign-in, and the API behind it. They live in `tests/e2e/` (project `E2E.csproj`, in `healthyhires.slnx`) and run on xUnit v3, like the backend tests.

The tests start nothing themselves. They drive a browser against a stack that is already running, so they test what a person would see.

## 1. One-time setup

Three commands do everything; nobody has to type a `dotnet` or `npx` command by hand.

| Who | Runs | What it does |
| --- | --- | --- |
| Whoever already has the test users | `just e2e-export` | Writes the users from their user secrets to `e2e-users.json` in the repository root |
| A teammate, once | `just e2e-setup` | Installs the test browser, imports `e2e-users.json` and deletes it; without the file, asks for each user instead |
| Everyone, every time | `just e2e` | Checks the browser and that the app is running, then runs the tests (§2) |

To set up a teammate: run `just e2e-export`, send them `e2e-users.json` **privately** (it holds passwords and authenticator secrets, so not through a channel that keeps history), have them put it in their repository root and run `just e2e-setup`, then delete your copy. They need only the .NET 10 SDK, Node.js and `just`, which the backend and frontend already need; the setup says what is missing.

### 1.1 The test users

Staff tests sign in as dedicated Entra ID test users, one per app role (`Recruiter`, `HiringManager`, `Admin`), never as a real person ([iam.md §8](../architecture/iam.md#8-local-development)). Each needs:

1. The app role, assigned under *Enterprise applications* → **HealthyHires API** → *Users and groups*.
2. A permanent password: sign in once by hand and finish any first-sign-in prompts.
3. An authenticator app registered for MFA, and that registration's **secret key** (the TOTP secret), from which the tests compute the codes.

The secret key is shown only while an authenticator is being registered, on the QR-code screen under *Can't scan image?*. Microsoft Authenticator never shows it again. For a user already paired by scanning the QR code, register a second authenticator to get one:

1. Sign in as the test user at <https://mysignins.microsoft.com/security-info>.
2. *Add sign-in method* → *Authenticator app* → *I want to use a different authenticator app* → *Next*.
3. On the QR-code screen, choose *Can't scan image?* and copy the **secret key**.
4. Add the same key to any authenticator app (it can be the phone's existing one) and enter its current code to finish.
5. Make **Authenticator app or hardware token – code** the default sign-in method. The tests can switch away from a push notification, but a code as the default is one step less to break.

The first person to have the users runs `just e2e-setup` without a file and types them in. They land in that person's .NET user secrets for `tests/e2e`, as `E2E:Users:<role>:Username`, `:Password` and `:TotpSecret`, outside the repository. Environment variables override them (`E2E__Users__Admin__Username`, …), which is how CI would pass them. Only the roles a run uses are needed: the applicant tests run without any user, and a staff test whose role has no user fails with *E2E:Users:&lt;role&gt;:Username is not set*.

## 2. Running

Start the app, each in its own terminal, and leave both running:

```bash
just aspire-up
```

```bash
just app
```

Then run the tests:

```bash
just e2e
```

It checks first that the frontend and the API answer, and says which one to start if not. A test passes with a green `succeeded`; a failure prints what was expected and what the page showed.

| To | Do |
| --- | --- |
| Watch the browser | `HEADED=1 just e2e` |
| Test the production headers and CSP through the local Caddy | `just app-build`, `just edge`, then `E2E__BaseUrl=http://localhost:8088 just e2e` |
| Run one class or test | `just e2e -- --filter-class '*StaffHomePageTests'` or `just e2e -- --filter-method '*greeted*'` |

`E2E:BaseUrl` defaults to `http://localhost:5173`, the Vite dev server. It must be a registered SPA redirect URI of the HealthyHires SPA app registration, as `http://localhost:5173` already is ([azure-deployment-guide.md §4](../operations/azure-deployment-guide.md#4-register-the-app-in-the-company-entra-tenant)); for the local Caddy, add `http://localhost:8088` too.

## 3. How sign-in works

The first staff test to use a role signs in as that role's user through the real Entra sign-in page, exactly as a person would: `/staff` redirects to Entra, the setup fills in the username and password, a code computed from the TOTP secret when Entra asks for one (under security defaults it does not on every sign-in), and answers *Stay signed in?*, and Entra redirects back. That happens **once per role per run** (`StaffSession`, an xUnit assembly fixture); every later page for that role gets a fresh browser context with the session copied in. The one exception is `SignInOutTests`: signing out ends the Entra session, so it signs the Recruiter in a second time in its own context. A user signing in twice in a run never reuses an authenticator code, which Entra would refuse; the second sign-in waits for the next code if needed.

The session is copied in two parts, because MSAL keeps its tokens in **sessionStorage** ([ADR-F006](../architecture/ADR-FRONTEND.md#adr-f006-authentication--msal-react-with-entra-id)) and Playwright's storage state does not include sessionStorage:

- the **storage state**: the cookies, including Entra's session cookie;
- a copy of **sessionStorage**, which `StaffTest` puts back with an init script before the frontend's code runs.

Both stay in memory; nothing with a token is written to disk.

A failed sign-in is not retried: every test of that role reports the same error. Repeated wrong codes would get the test user locked out.

## 4. Writing tests

Tests are grouped by who uses the page, in folders that mirror the frontend's route trees:

```text
tests/e2e/
├─ Infrastructure/      E2ESettings and StaffRole, Entra (Microsoft's sign-in pages), StaffSession, the base classes
├─ Applicants/          Public pages; no sign-in
└─ Staff/               Pages under /staff; signed in as the test user
```

An applicant test derives from `E2EPageTest` and uses its `Page`. A staff test derives from `StaffTest`, takes the session in its constructor, and asks for a page per role with `SignInAsAsync`:

```csharp
public class StaffHomePageTests(StaffSession session) : StaffTest(session)
{
    [Theory]
    [InlineData(StaffRole.Recruiter)]
    [InlineData(StaffRole.HiringManager)]
    [InlineData(StaffRole.Admin)]
    public async Task Every_role_is_greeted_by_name(string role)
    {
        IPage page = await SignInAsAsync(role);

        await page.GotoAsync("/staff");

        await Expect(page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("^Welcome, ") })).ToBeVisibleAsync();
    }
}
```

A test about permissions can open two pages, e.g. `SignInAsAsync(StaffRole.Admin)` and `SignInAsAsync(StaffRole.Recruiter)`, and check that only one of them can act.

- **Navigate with relative paths** (`/staff`); the base URL comes from `E2E:BaseUrl`.
- **Find elements the way a user does:** `GetByRole`, `GetByLabel`, `GetByText`. Avoid CSS classes, which change with styling.
- **Assert with `Expect(...)`**, which retries until the page settles, rather than reading a value once and comparing it.
- **Assert something only the backend can produce** where it matters. The greeting above comes from `GET /api/v1/me`, so it proves the token reached the API, not only that the page rendered.
- **Each test gets its own context**, so tests run in parallel and must not depend on each other's state. Data a test creates in the database stays there until the AppHost restarts, which recreates the database ([aspire-dev-stack.md](../architecture/aspire-dev-stack.md)); give it unique names.
- Test names use underscores and read as a sentence, as in `Api/Tests`.

## 5. Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| *Executable doesn't exist at …/ms-playwright/…* | The browser is missing; `just e2e` installs it, so this only happens with a plain `dotnet test`. Run `just e2e-setup` |
| Staff tests of one role fail with *E2E:Users:&lt;role&gt;:… is not set* | That role's user is missing: `just e2e-setup` (§1) |
| A failed sign-in, in general | The error names the Entra page it stopped on, quotes its text and saves a screenshot under `artifacts/bin/E2E/debug/sign-in-failures/`. `HEADED=1 just e2e` shows it live |
| *Entra wants this user to set up MFA first* | The user has no authenticator registered in Entra, whatever is on a phone. Register one with its secret key (§1.1), then `just e2e-setup` |
| *Entra refused the authenticator code* | The stored secret key is not this user's (`just e2e-setup` again), or the computer's clock is off. The tests never retry a code, so the user is not locked out |
| Sign-in times out on another Entra page | A prompt the setup does not know: a password change or consent. Sign in once by hand as the test user and finish it |
| Sign-in fails after it worked before | Microsoft changed its sign-in page; update the selectors in `Entra` |
| *AADSTS50105* | The test user has no app role on the HealthyHires API (§1.1) |
| *AADSTS50011*, redirect URI mismatch | `E2E:BaseUrl` is not a registered SPA redirect URI (§2) |
| The staff page loads but the greeting never appears | The API is not running, or rejects the token: check `just aspire-up` and the API's logs in the Aspire dashboard |
