# First Module Tests

Three tests check every endpoint the modules map. With no module yet they would pass by checking nothing, so they are added with the first module, ported from PocketDesk. Each place that refers to one is marked `TODO(first-module)`:

```bash
grep -rn "TODO(first-module)" Api docs --exclude-dir=artifacts
```

Give the three one shared list of module assemblies, so a later module is added in one place.

| Test | Fails the build when | Guards |
| --- | --- | --- |
| `DeclaredStatusCodeTests` | an operation with a path parameter does not declare `404`, or one with a body or a query parameter does not declare `400` | [errors.md § Declaring error responses](errors/errors.md#declaring-error-responses) |
| `QueryEndpointOptOutTests` | a `POST`/`PATCH` endpoint dispatches a query without `MapQuery`, or dispatches a command while opted out of idempotency | [idempotency.md § Reads exposed as POST](idempotency/idempotency.md#reads-exposed-as-post) |
| `ResponseLinkDeclarationTests` | a `.ProducesLink(...)` targets an operation that does not exist, leaves a target path parameter unfilled, uses an unsupported expression, or hangs off an undeclared status | [implementation-guide.md §5.1](implementation-guide.md#51-linking-a-response-to-what-it-unlocks) |

When all three are in, delete this page, its link in `docs/README.md`, and the markers.
