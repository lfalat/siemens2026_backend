# Error Model

Every error the API returns is an RFC 7807 problem document (`application/problem+json`). That includes the ones ASP.NET would otherwise send as an empty body. A handler signals failure by **throwing** a `Kernel.Exceptions` type: the status code and the response shape come from the exception, not from the endpoint. The frontend is Slovak only, so every problem's `detail` is a **Slovak message for the user**, which the frontend shows as is ([ADR-A017](../../architecture/ADR-ASPNET.md#adr-a017-error-messages--slovak-detail-written-by-the-api)).

Three exception handlers translate exceptions into responses. They are registered in `Program.cs` in this order, and the first one that recognises the exception answers:

1. `AppExceptionHandler` handles three kinds of failure:
   - an `AppException`;
   - EF Core's `DbUpdateConcurrencyException`;
   - the framework's `BadHttpRequestException`.

   All three are the caller's to fix. The handler answers with the exception's status and message, does **not** pass the exception on to the catch-all, and never logs it as a fault.
2. `UnstorableTextExceptionHandler` turns PostgreSQL's `22021` (a NUL byte in a text value) into a `400`. Without it, that error would surface as a `500`.
3. `UnhandledExceptionHandler` comes last, so the others get the first chance. It logs the exception with the method and route template, then returns a bare `500` with a generic message and nothing from the exception.

## Response shape

Every problem has the RFC 7807 fields plus the additions made by `ProblemDetailsTelemetry.Customize`, which is registered in `AddProblemDetails`:

| Field | Value |
| --- | --- |
| `status` | The exception's status code |
| `title` | Filled in from the status by the ProblemDetails writer. English; not for display. |
| `detail` | The Slovak message the frontend shows. **Every problem has one**; see [Messages](#messages) |
| `type` | Derived from the status, unless the exception sets its own; see [Problem types](#problem-types) |
| `instance` | The request path |
| `traceId` | The W3C trace id from `Activity.Current`, added to every problem |

`traceId` links an error a user reports to its trace in Azure Monitor ([observability-architecture.md](../../architecture/observability-architecture.md)). Every error response has one, including a `500`. It is what makes an opaque "something went wrong" report something you can act on.

## The exception taxonomy

All of these live in `Shared/Kernel/Exceptions/Exceptions.cs` and derive from `AppException`. Each one carries a status code, an optional problem `type` and its message, which becomes `detail`. Every type except `TooManyRequestsException` has a default message from `ErrorMessages`. Where the reason is specific, the throw site passes its own:

```csharp
throw new ConflictException("Na túto ponuku sa už nedá prihlásiť, pretože je uzavretá.");
```

| Exception | Status | Default message | When |
| --- | --- | --- | --- |
| `ValidationException` | 400 | `ValidationFailed` | A command's DataAnnotations rule or a handler's cross-field rule failed. Adds `errors`; see [Validation errors](#validation-errors). |
| `BadRequestException` | 400 | `Invalid` | The request is wrong in a way binding and validation cannot detect. |
| `ForbiddenException` | 403 | `Forbidden` | The caller is signed in, but their role does not allow the action. |
| `NotFoundException` | 404 | `NotFound` | The resource does not exist, or the caller cannot see it. |
| `ConflictException` | 409 | `Conflict` | The request conflicts with the current state, e.g. a duplicate. The throw site says which. |
| `TooManyRequestsException` | 429 | none; the constructor requires one | The application has reached a limit and the request has to wait. Sets `Retry-After` in whole seconds and, when the end of the wait is known, a `retryAt` timestamp in the body. |
| `ExternalServiceException` | 502 | `UpstreamFailed` | A dependency answered, but the answer was unusable. |
| `ServiceUnavailableException` | 503 | `UpstreamUnavailable` | A dependency could not be reached. Sets `Retry-After`. |

Two exceptions that are not `AppException`s are handled alongside these:

- `DbUpdateConcurrencyException`: a row's optimistic-concurrency `version` changed between this request's read and its save ([conventions.md](../../db/conventions.md#optimistic-locking-version)). It is answered with `409` and `ConcurrencyConflict`.
- `BadHttpRequestException`: an unparseable body, a bad route or query value, or a body that is too large. It keeps its own status, usually `400`, and gets that status's default message. Its own text is English and technical, so it is not passed on.

Any other exception is a genuine fault: `500`, logged, and nothing from the exception appears in the response.

### Distinctions clients depend on

These choices are part of the API contract. Changing them changes how clients behave.

**502 versus 503.** A `502` means a dependency rejected the request, so repeating it unchanged will probably be rejected again. A `503` means the dependency could not be reached, so the same request is worth retrying shortly. Hence the `Retry-After` header, which defaults to 5 seconds because these outages are usually a restart. Both keep an outage of something we don't control out of the `500` bucket, which means "this service has a bug".

**429 from two places.** The rate limiter answers the per-caller HTTP budget ([ADR-A007](../../architecture/ADR-ASPNET.md#adr-a007-rate-limiting--aspnet-core-rate-limiting)). `TooManyRequestsException` is for limits the application itself enforces, and its message says which limit was hit. Either way the client honours `Retry-After`.

**409 for concurrency.** The concurrency message tells the user to reload and try again. A `ConflictException` says why the request itself cannot succeed in the current state.

**Errors are never cached by the idempotency middleware.** Responses produced by throwing are written by the exception handlers, which sit outside the idempotency middleware. Only responses an endpoint _returns_ are remembered, so a retry after an error runs the endpoint again. See [idempotency.md](../idempotency/idempotency.md).

## Messages

`detail` is shown to the user exactly as the API writes it, so a message:

- is Slovak, with diacritics, written for the person using the app;
- says what happened and, where there is one, what to do next ("Skúste to znova o chvíľu.");
- names only what the user knows: a username they typed, not an internal id or a stack of technical causes.

`ErrorMessages` (`Kernel.Errors`) holds the messages the host and Kernel answer with: the exception defaults above, plus the ones written outside the exception handlers.

| Message | Status | Answered by |
| --- | --- | --- |
| `Malformed` | 400 | an unparseable body or route/query value |
| `ValidationFailed` | 400 | a `ValidationException`, or parameter validation |
| `UnstorableText` | 400 | `UnstorableTextExceptionHandler` |
| `Unauthenticated` | 401 | authentication, on staff endpoints |
| `Forbidden` | 403 | an authorization policy, or a `ForbiddenException` |
| `NotFound` / `MethodNotAllowed` | 404 / 405 | routing |
| `ConcurrencyConflict` | 409 | a stale optimistic-concurrency version, on any write |
| `PayloadTooLarge` / `UnsupportedMediaType` | 413 / 415 | the framework |
| `RateLimited` | 429 | the per-caller rate limiter |
| `UpstreamFailed` / `UpstreamUnavailable` | 502 / 503 | a dependency |
| `ServerError` | 500 | an unhandled fault; asks the user to quote the `traceId` |

`ErrorMessages.ForStatus` maps a status to its default message. `ProblemDetailsTelemetry.Customize` applies it to any problem written without a `detail`, so even a response that no exception produced still has a message to show.

The JSON writer escapes non-ASCII characters (`\u00FD`), so read `detail` by parsing the body, not by matching its raw text.

## Validation errors

Validation errors come from three places. Two of them raise a `ValidationException` and produce the same response.

**Command validation** is where a feature's rules live ([implementation-guide.md §6](../implementation-guide.md#6-validation)). The `ValidationDecorator` runs the DataAnnotations attributes on a command before its handler executes. When a rule fails, it raises a `ValidationException` with the rule's Slovak message for each failure. A failing `Required` short-circuits that property's other rules, so a missing value reports only that it is required. Queries are not validated.

**Handler validation** covers the cross-field rules attributes cannot express. The handler raises a `ValidationException` itself, in one of two ways:

- the `ValidationErrors` accumulator, to collect every problem in one pass and then call `ThrowIfAny()`;
- `ValidationException.ForField` for a single problem.

Either way, the messages are keyed by field under `errors`, and the frontend shows each next to its field ([ADR-F010](../../architecture/ADR-FRONTEND.md#adr-f010-forms-and-validation--react-hook-form-and-zod)):

```json
{
  "status": 400,
  "title": "Bad Request",
  "detail": "Niektoré údaje nie sú vyplnené správne.",
  "instance": "/api/v1/job-offers",
  "traceId": "00-a1b2...-01",
  "errors": { "SalaryMin": ["Minimálny plat nemôže byť vyšší ako maximálny."] }
}
```

**Parameter validation** is the third place: the framework's own validation of endpoint parameters ([ADR-A008](../../architecture/ADR-ASPNET.md#adr-a008-validation--native-aspnet--net-10-validation)), for an attribute on a route or query value. It answers with the Slovak `detail`, but its `errors` are the framework's **English** messages, because the framework words them itself. Keep rules on the command where you can; a parameter rule is for a value that never reaches one.

### Validation messages

`ValidationMessages` (`Kernel.Errors`) words each failing DataAnnotations attribute in Slovak, since the framework's messages are English and name the C# property. It picks the message in this order:

1. The attribute's own `ErrorMessage`, when the command's author set one. Write it in Slovak.
2. A Slovak wording of the built-in rule: `Required`, `StringLength`, `Range`, `EmailAddress` and so on. Length rules count characters for a text and items for a collection.
3. A custom attribute's own `FormatErrorMessage`. Write it in Slovak.

`Kernel.Validation` adds two attributes the framework lacks:

| Attribute | Rejects | Why |
| --- | --- | --- |
| `NotEmpty` | `Guid.Empty` | `Required` cannot reject it: a non-nullable `Guid` is never `null`, so a body that omits the property binds to all zeros and passes. Keep `Required` alongside it, because that is what puts the property in the schema's `required` list. |
| `DecimalPrecision(precision, scale)` | A `decimal` that does not fit `numeric(precision, scale)` | Without it the database silently rounds away extra decimal places, and a value too large for the column fails the insert with a `500`. Trailing zeros don't count. |

## Declaring error responses

An endpoint declares the problem responses its handler, and the services it calls, can answer with `.ProducesProblem(status)`:

```csharp
group.MapPost("/job-offers/{id:guid}/applications", Handler)
     .ProducesProblem(StatusCodes.Status404NotFound)
     .ProducesProblem(StatusCodes.Status409Conflict); // the offer is closed
```

Some statuses are declared once, not on every endpoint:

- **On the versioned API group:** `400` and `429`, for an unparseable request and the rate limit. A staff group adds `401` through `.RequireStaff()`. Anonymous applicant endpoints never answer it.
- **By rule, in `ProblemResponseTransformer`:**
  - `409` on every write except a `MapQuery` read, for a stale version;
  - `403` wherever an authorization policy applies;
  - `400` where the dispatched command has validation rules (read off the `ICommandHandler` its delegate takes).
- **By `IdempotencyHeaderTransformer`:** the middleware's `400`, `409` and `422` on the operations it covers.

Schemathesis fails on a status the document does not list (ADR-009), so every status an endpoint can reach must be declared one of these ways. `DeclaredStatusCodeTests` (`TODO(first-module)`, [first-module-tests.md](../first-module-tests.md)) will check that every operation with a path parameter declares a `404`, and every operation with a body or a query parameter declares a `400`.

## Problem types

`AppException.ProblemType` sets the `type` URI. Almost every exception leaves it unset: don't mint a URI nobody reads.

Set one only when a client must tell apart two problems that share a status code **and act differently on each**. The status is too coarse to distinguish them, and matching on `detail` text is not a contract. None exists today. Adding one is a contract addition, so list it here:

| Type URI | Meaning |
| --- | --- |
| _none yet_ | |

## Errors not produced by throwing

Several responses are written outside the exception handlers. Each is shaped the same way and carries a Slovak `detail`, so a client never has to parse two error formats:

- **429** from the rate limiter (`RateLimited`): written through `IProblemDetailsService` in its `OnRejected` callback, with `Retry-After` in the header.
- **400 / 409 / 422** from the idempotency middleware; see [idempotency.md](../idempotency/idempotency.md).
- **401, 403, 404, 405**: the framework writes these without a body, and `UseStatusCodePages` fills one in with the status's default message.
- **400** from parameter validation (`ValidationFailed`), with English `errors`.
- **500** from `UnhandledExceptionHandler` (`ServerError`).

## Adding an exception type

1. **Add the type** to `Shared/Kernel/Exceptions/Exceptions.cs`, deriving from `AppException` with its status code. No handler change is needed, because `AppExceptionHandler` reads the status from the base type.
2. **Add a row** to [the taxonomy table](#the-exception-taxonomy) above.
3. **Give it a default message**: an existing `ErrorMessages` constant, or a new one added there, as the constructor's default `message`. A throw site passes its own where the reason is specific, and the handler writes it without any per-type change.
4. **Set `ProblemType` only if needed**: only when a client has to tell this problem apart from another one with the same status. If you set one, add it to [Problem types](#problem-types).
5. **Extra response fields need a handler case.** If the type needs a response field beyond `detail` (as `ValidationException` does with `errors`, or `TooManyRequestsException` with `retryAt`), add an explicit case to `AppExceptionHandler`. The handler does not discover extensions by reflection.

## Tests

| Tests | What they pin |
| --- | --- |
| `AppExceptionHandlerTests` | Each exception's status, `detail`, `errors`, `Retry-After` and `retryAt`; the framework's English text replaced by a Slovak default |
| `UnhandledExceptionHandlerTests` | A `500` with the generic message and no exception text; the log entry has the route template but not the path |
| `UnstorableTextExceptionHandlerTests` | PostgreSQL really answers `22021` for a NUL, and only that becomes a `400` |
| `ErrorResponseShapeTests` | The framework's 401/403/404/405 become problem documents with `traceId` and the status's Slovak `detail`, and a problem that already has a body is left alone |
| `ProblemResponseTransformerTests` | The rule-added `409`, `403` and `400` appear where they apply, and an endpoint's own response is kept |
| `ValidationErrorsTests`, `ValidationMessagesTests`, `NotEmptyAttributeTests`, `DecimalPrecisionAttributeTests` | The accumulator, the attribute-to-message map and the two custom attributes |
