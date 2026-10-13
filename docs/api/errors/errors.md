# Error Model

Every error the API returns is an RFC 7807 problem document (`application/problem+json`). That includes the ones ASP.NET would otherwise send as an empty body. A handler signals failure by **throwing** a `Kernel.Exceptions` type: the status code and the response shape come from the exception, not from the endpoint. Every problem carries a stable `code`, which the frontend translates into Slovak, Czech or English ([ADR-F011](../../architecture/ADR-FRONTEND.md#adr-f011-localization--react-i18next-for-sk-cz-and-en)).

Three exception handlers translate exceptions into responses. They are registered in `Program.cs` in this order, and the first one that recognises the exception answers:

1. `AppExceptionHandler` handles three kinds of failure:
   - an `AppException`;
   - EF Core's `DbUpdateConcurrencyException`;
   - the framework's `BadHttpRequestException`.

   All three are the caller's to fix. The handler answers with the exception's status and code, does **not** pass the exception on to the catch-all, and never logs it as a fault.
2. `UnstorableTextExceptionHandler` turns PostgreSQL's `22021` (a NUL byte in a text value) into a `400`. Without it, that error would surface as a `500`.
3. `UnhandledExceptionHandler` comes last, so the others get the first chance. It logs the exception with the method and route template, then returns a bare `500` with `server.error` and nothing from the exception.

## Response shape

Every problem has the RFC 7807 fields plus the additions made by `ProblemDetailsTelemetry.Customize`, which is registered in `AddProblemDetails`:

| Field | Value |
| --- | --- |
| `status` | The exception's status code |
| `title` | Filled in from the status by the ProblemDetails writer |
| `detail` | The exception message. It is English and may include ids, so it is **not** a translation key. |
| `type` | Derived from the status, unless the exception sets its own; see [Problem types](#problem-types) |
| `code` | A stable machine-readable code for _this_ error, on every problem; see [Error codes](#error-codes) |
| `instance` | The request path |
| `traceId` | The W3C trace id from `Activity.Current`, added to every problem |

`traceId` links an error a user reports to its trace in Azure Monitor ([observability-architecture.md](../../architecture/observability-architecture.md)). Every error response has one, including a `500`. It is what makes an opaque "something went wrong" report something you can act on.

## The exception taxonomy

All of these live in `Shared/Kernel/Exceptions/Exceptions.cs` and derive from `AppException`. Each one carries a status code, an optional problem `type` and an `ErrorCode`. Every type has a default code. Where the reason is specific, a throw site replaces it with a code from its module's catalog:

```csharp
throw new ConflictException("Applications are closed for this offer.") { Code = RecruitmentErrors.OfferClosed };
```

| Exception | Status | Default `code` | When |
| --- | --- | --- | --- |
| `ValidationException` | 400 | `request.validation_failed` | A command's DataAnnotations rule or a handler's cross-field rule failed. Adds `errors` and `errorCodes`; see [Validation errors](#validation-errors). |
| `BadRequestException` | 400 | `request.invalid` | The request is wrong in a way binding and validation cannot detect. |
| `ForbiddenException` | 403 | `auth.forbidden` | The caller is signed in, but their role does not allow the action. |
| `NotFoundException` | 404 | `request.not_found` | The resource does not exist, or the caller cannot see it. |
| `ConflictException` | 409 | `request.conflict` | The request conflicts with the current state, e.g. a duplicate. The throw site names the specific reason. |
| `TooManyRequestsException` | 429 | none; the constructor requires a code | The application has reached a limit and the request has to wait. Sets `Retry-After` in whole seconds and, when the end of the wait is known, a `retryAt` timestamp in the body. |
| `ExternalServiceException` | 502 | `upstream.failed` | A dependency answered, but the answer was unusable. |
| `ServiceUnavailableException` | 503 | `upstream.unavailable` | A dependency could not be reached. Sets `Retry-After`. |

Two exceptions that are not `AppException`s are handled alongside these:

- `DbUpdateConcurrencyException`: a row's optimistic-concurrency `version` changed between this request's read and its save ([conventions.md](../../db/conventions.md#optimistic-locking-version)). It is answered with `409 concurrency.conflict`.
- `BadHttpRequestException`: an unparseable body, a bad route or query value, or a body that is too large. It keeps its own status, usually `400`, and gets that status's default code (`request.malformed`, `request.payload_too_large`, and so on).

Any other exception is a genuine fault: `500`, logged, and nothing from the exception appears in the response.

### Distinctions clients depend on

These choices are part of the API contract. Changing them changes how clients behave.

**502 versus 503.** A `502` means a dependency rejected the request, so repeating it unchanged will probably be rejected again. A `503` means the dependency could not be reached, so the same request is worth retrying shortly. Hence the `Retry-After` header, which defaults to 5 seconds because these outages are usually a restart. Both keep an outage of something we don't control out of the `500` bucket, which means "this service has a bug".

**429 from two places.** The rate limiter answers `request.rate_limited` for the per-caller HTTP budget ([ADR-A007](../../architecture/ADR-ASPNET.md#adr-a007-rate-limiting--aspnet-core-rate-limiting)). `TooManyRequestsException` is for limits the application itself enforces, and each such limit has its own code. A client that only reads the status does the right thing by honouring `Retry-After`. A client that wants to show a useful message needs the code.

**409 for concurrency.** `concurrency.conflict` tells the client to reload the resource and try again. A `ConflictException` with a module code tells it that the request itself cannot succeed in the current state.

**Errors are never cached by the idempotency middleware.** Responses produced by throwing are written by the exception handlers, which sit outside the idempotency middleware. Only responses an endpoint _returns_ are remembered, so a retry after an error runs the endpoint again. See [idempotency.md](../idempotency/idempotency.md).

## Validation errors

Validation errors come from three places. Two of them raise a `ValidationException` and produce the same response.

**Command validation** is where a feature's rules live ([implementation-guide.md §6](../implementation-guide.md#6-validation)). The `ValidationDecorator` runs the DataAnnotations attributes on a command before its handler executes. When a rule fails, it raises a `ValidationException` naming the attribute's code for each failure. A failing `Required` short-circuits that property's other rules, so a missing value reports `required` alone. Queries are not validated.

**Handler validation** covers the cross-field rules attributes cannot express. The handler raises a `ValidationException` itself, in one of two ways:

- the `ValidationErrors` accumulator, to collect every problem in one pass and then call `ThrowIfAny()`;
- `ValidationException.ForField` for a single problem.

Either way, a `ValidationException` carries two field-keyed maps that are **index-aligned**: `errorCodes[field][i]` is the code for `errors[field][i]`.

```json
{
  "status": 400,
  "title": "Bad Request",
  "detail": "One or more validation errors occurred.",
  "instance": "/api/v1/job-offers",
  "traceId": "00-a1b2...-01",
  "code": "request.validation_failed",
  "errors": { "SalaryMin": ["The minimum salary is above the maximum."] },
  "errorCodes": { "SalaryMin": ["job_offer.salary_range_inverted"] }
}
```

A command rule's per-field code is the attribute's (see below); a handler's is a `FieldErrorCode` from the module's catalog. A client translates `errorCodes` and falls back to the English `errors` message when it has no translation for a code.

**Parameter validation** is the third place: the framework's own validation of endpoint parameters ([ADR-A008](../../architecture/ADR-ASPNET.md#adr-a008-validation--native-aspnet--net-10-validation)), for an attribute on a route or query value. It answers `request.validation_failed` with `errors` but **no `errorCodes`**, because the framework does not record which attribute failed. Keep rules on the command where you can; a parameter rule is for a value that never reaches one.

### Validation attributes

`Kernel.Validation` adds two attributes the framework lacks:

| Attribute | Rejects | Why |
| --- | --- | --- |
| `NotEmpty` | `Guid.Empty` | `Required` cannot reject it: a non-nullable `Guid` is never `null`, so a body that omits the property binds to all zeros and passes. Keep `Required` alongside it, because that is what puts the property in the schema's `required` list. |
| `DecimalPrecision(precision, scale)` | A `decimal` that does not fit `numeric(precision, scale)` | Without it the database silently rounds away extra decimal places, and a value too large for the column fails the insert with a `500`. Trailing zeros don't count. |

`ValidationRuleErrors` (`Kernel.Errors`) maps each DataAnnotations attribute to a one-word field code: `required`, `not_empty`, `string_length`, `range`, `pattern`, `allowed_values` and so on. A custom attribute names its own code by implementing `IValidationErrorCode`, as `NotEmpty` and `DecimalPrecision` do; one that doesn't falls back to `invalid`. The validation decorator writes these codes, and the OpenAPI document lists them under the `400` of every operation that dispatches the command.

## Error codes

`AppException.Code` sets the response's `code`: a stable, machine-readable key that a multilingual client translates on. **Every problem carries a `code`**, so a client translates on `code` and never on the status ([ADR-A017](../../architecture/ADR-ASPNET.md#adr-a017-error-codes--a-typed-catalog-declared-per-endpoint-generated-into-openapi)).

`code` is independent of `type`. `type` is a URI that classifies a _kind_ of problem for branching logic; `code` is the short key for translation.

### Typed catalogs

A code is never a string literal at a throw site. Two Kernel types (`Kernel.Errors`) keep apart the two places a code can appear, so the compiler refuses one where the other belongs:

- `ErrorCode(Status, Value, Description)` is a problem's top-level `code`, answered with `Status`. An exception refuses a code whose status differs from its own.
- `FieldErrorCode(Value, Description)` is a value under `errorCodes[field]` of a `400`.

Each module declares its codes in one catalog at its Domain root, named `<Module>Errors` (for example `RecruitmentErrors`). They are `public static readonly` fields named after the value: `job_offer.closed` becomes `OfferClosed`. Values have the form `area.reason`, in lower snake case. The `Description` is one English sentence, which the OpenAPI document shows next to the code.

The codes the host and Kernel produce are in Kernel:

- `CommonErrors` holds the exception defaults above, plus the codes written outside the exception handlers:

  | Code | Status | Answered by |
  | --- | --- | --- |
  | `request.malformed` | 400 | an unparseable body or route/query value |
  | `request.validation_failed` | 400 | a `ValidationException`, or parameter validation |
  | `request.rate_limited` | 429 | the per-caller rate limiter |
  | `request.not_found` / `request.method_not_allowed` | 404 / 405 | routing |
  | `request.payload_too_large` / `request.unsupported_media_type` | 413 / 415 | the framework |
  | `auth.unauthenticated` | 401 | authentication, on staff endpoints |
  | `auth.forbidden` | 403 | an authorization policy, or a `ForbiddenException` |
  | `concurrency.conflict` | 409 | a stale optimistic-concurrency version, on any write |
  | `idempotency.key_missing` / `idempotency.key_too_long` / `idempotency.in_progress` / `idempotency.payload_mismatch` | 400 / 400 / 409 / 422 | the idempotency middleware |
  | `upstream.failed` / `upstream.unavailable` | 502 / 503 | a dependency |
  | `server.error` | 500 | an unhandled fault |

- `ValidationRuleErrors` holds the DataAnnotations rule codes.

`CommonErrors.ForStatus` maps a status to its default code. `ProblemDetailsTelemetry.Customize` applies it to any problem written without a `code`, so even a response that no exception produced still carries one.

Codes are part of the API contract, because the frontend's translation files are keyed on them. Until the first release they can be renamed freely. After it, renaming one is a breaking change.

### Declaring what an endpoint answers

An endpoint lists the codes it can answer with `.ProducesErrors(...)`. That also declares the problem response for each code's status, so the endpoint does not need a separate `ProducesProblem` for those statuses:

```csharp
group.MapPost("/job-offers/{id:guid}/applications", Handler)
     .ProducesErrors(CommonErrors.NotFound, RecruitmentErrors.OfferClosed);
```

Some codes are declared once, not on every endpoint:

- **On the versioned API group:** `CommonErrors.AnyEndpoint`, which is `request.malformed` and `request.rate_limited`. A staff group adds `auth.unauthenticated` once it requires sign-in. Anonymous applicant endpoints never answer it, which is why it is not in `AnyEndpoint`.
- **By rule, in `ErrorCodeDeclarationExtensions.Declared`:**
  - `concurrency.conflict` on every write except a `MapQuery` read;
  - `auth.forbidden` wherever an authorization policy applies;
  - `request.validation_failed` wherever a field code is declared.
- **Only in the document:** the rule codes of the command an endpoint dispatches (read off the `ICommandHandler` its delegate takes), and the idempotency rejections on the operations that middleware covers.

### In the OpenAPI document

- **Under each error response of an operation**, `ErrorCodeTransformer` adds two things:
  - a table of each code and its meaning in the response description, which is what Scalar shows;
  - the bare values in `x-error-codes`, plus `x-field-error-codes` on a `400` for the per-field codes.

  A status the operation never declared but a rule adds, such as a `409` for `concurrency.conflict`, gets its response here.
- **Under Components / Schemas**, `ErrorCodeCatalogTransformer` publishes `ErrorCode` and `FieldErrorCode`. Each is a string enum of every code with an `x-enum-descriptions` map. They are referenced from `code` and `errorCodes` of the problem schemas, so the frontend can generate its list of translation keys from them.

### Keeping declarations honest

`ErrorCodeCatalogTests` checks every catalog listed in it:

- no two codes of one kind share a value;
- every problem code has the form `area.reason`;
- every code has a description;
- every problem code is answered with an error status.

Add each module's catalog to that list when the module is created.

The check that every code an endpoint can _reach_ is declared, and nothing unreachable is declared, needs endpoints to analyse. It arrives with the first module as `ErrorCodeDeclarationTests` (`TODO(first-module)`, [first-module-tests.md](../first-module-tests.md)). The same goes for `DeclaredStatusCodeTests`: every operation with a path parameter declares a `404`, and every operation with a body or a query parameter declares a `400`.

### Adding a code

1. Add it to the module's catalog: an `ErrorCode` with its status, or a `FieldErrorCode`, with a one-sentence description.
2. Use it at the throw site.
3. Add it to `.ProducesErrors(...)` on every endpoint that can reach it.
4. Add its translation to the frontend's SK, CZ and EN files.

## Problem types

`AppException.ProblemType` sets the `type` URI. Almost every exception leaves it unset: don't mint a URI nobody reads.

Set one only when a client must tell apart two problems that share a status code **and act differently on each**. The status is too coarse to distinguish them, and matching on `detail` text is not a contract. None exists today. Adding one is a contract addition, so list it here:

| Type URI | Meaning |
| --- | --- |
| _none yet_ | |

## Errors not produced by throwing

Several responses are written outside the exception handlers. Each is shaped the same way and carries its `code`, so a client never has to parse two error formats:

- **429** from the rate limiter (`request.rate_limited`): written through `IProblemDetailsService` in its `OnRejected` callback, with `Retry-After` in the header.
- **400 / 409 / 422** from the idempotency middleware (`idempotency.*`); see [idempotency.md](../idempotency/idempotency.md).
- **401, 403, 404, 405**: the framework writes these without a body, and `UseStatusCodePages` fills one in with the status's default code.
- **400** from parameter validation (`request.validation_failed`), with `errors` but no `errorCodes`.
- **500** from `UnhandledExceptionHandler` (`server.error`).

## Adding an exception type

1. **Add the type** to `Shared/Kernel/Exceptions/Exceptions.cs`, deriving from `AppException` with its status code. No handler change is needed, because `AppExceptionHandler` reads the status from the base type.
2. **Add a row** to [the taxonomy table](#the-exception-taxonomy) above.
3. **Pass its default code** to the `AppException` constructor: an existing `CommonErrors` code, or a new one added there. A throw site replaces it where the reason is specific, and the handler writes it without any per-type change.
4. **Set `ProblemType` only if needed**: only when a client has to tell this problem apart from another one with the same status. If you set one, add it to [Problem types](#problem-types).
5. **Extra response fields need a handler case.** If the type needs a response field beyond `detail` and `code` (as `ValidationException` does with `errors` and `errorCodes`, or `TooManyRequestsException` with `retryAt`), add an explicit case to `AppExceptionHandler`. The handler does not discover extensions by reflection.

## Tests

| Tests | What they pin |
| --- | --- |
| `AppExceptionHandlerTests` | Each exception's status, `code`, `errors`/`errorCodes`, `Retry-After` and `retryAt`, plus a code with the wrong status being refused |
| `UnhandledExceptionHandlerTests` | A `500` with `server.error` and no exception text; the log entry has the route template but not the path |
| `UnstorableTextExceptionHandlerTests` | PostgreSQL really answers `22021` for a NUL, and only that becomes a `400` |
| `ErrorResponseShapeTests` | The framework's 401/403/404/405 become problem documents with `traceId` and `code`, and a problem that already has a body is left alone |
| `ErrorCodeTransformerTests` | Declared and rule-added codes appear under the right responses, and the catalog enums appear in the document |
| `ErrorCodeCatalogTests` | Catalog values are unique, well-formed, described and have error statuses; `ForStatus` agrees with each code's status |
| `ValidationErrorsTests`, `ValidationRuleErrorsTests`, `NotEmptyAttributeTests`, `DecimalPrecisionAttributeTests` | The accumulator, the attribute-to-code map and the two custom attributes |
