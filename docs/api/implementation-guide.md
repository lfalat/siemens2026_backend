# Implementation Guide

How the HealthyHires API is put together — a modular monolith of vertical-slice CQRS features — and how to add a module, add a feature, and follow the conventions the codebase lives by.

Written for two audiences:

- **New to the codebase:** the mental model and where things live (§1–§3).
- **Building something:** step-by-step recipes and the rules to follow while doing it (§4–§11).

Source of truth for this document:

- `Api/ApiService/Program.cs`: the composition root, where modules are wired into the host.
- `Api/Shared/Kernel/*`: the cross-cutting contracts every module depends on (CQRS, decorators, endpoints, errors, scheduling).
- `Api/Modules/*`: `RecruitmentModule` (job offers, applications, CVs, the HR workflow), `FilesModule` and `AuditModule`. They are empty projects for now, so the examples below use the Recruitment features they will hold.

Cross-cutting concerns have their own guides; this document links to them instead of repeating them:

- [Error Model](errors/errors.md): problem responses, the exception taxonomy, error codes and their catalogs.
- [Idempotency](idempotency/idempotency.md): at-most-once `POST`/`PATCH` through the `Idempotency-Key` header, applied by host middleware.
- [Scheduling](scheduling/scheduling.md): daily and periodic background workers.
- [Database conventions](../db/conventions.md): naming, keys, audit columns, the outbox table.
- [Identity & Access Management](../architecture/iam.md): staff sign-in with Entra ID, anonymous applicants.

The decisions behind all of this are in [ADR-ASPNET.md](../architecture/ADR-ASPNET.md), referenced inline as `ADR-Axxx`.

## Table of contents

1. [Mental model](#1-mental-model)
2. [Solution layout](#2-solution-layout)
3. [Anatomy of a module](#3-anatomy-of-a-module)
4. [How to create a module](#4-how-to-create-a-module)
5. [How to create a feature](#5-how-to-create-a-feature)
6. [Validation](#6-validation)
7. [The decorator chain](#7-the-decorator-chain)
8. [Entities and persistence](#8-entities-and-persistence)
9. [Cross-module communication](#9-cross-module-communication)
10. [Authentication and authorization](#10-authentication-and-authorization)
11. [Conventions cheat-sheet](#11-conventions-cheat-sheet)
12. [Testing](#12-testing)
13. [Checklists](#13-checklists)

## 1. Mental model

The API is a **modular monolith** ([ADR-A001](../architecture/ADR-ASPNET.md#adr-a001-application-architecture--modular-monolith)): one deployable process, split into modules that are as isolated as if they were separate services, so any module can later be extracted with little churn.

Four ideas hold it together:

- **Modules are vertical slices, not layers.** A module owns everything for its domain — endpoints, business logic, entities, database schema — top to bottom. There is no shared service or data layer cutting across modules.
- **Every request is a command or a query** ([ADR-A003](../architecture/ADR-ASPNET.md#adr-a003-application-architecture--vertical-slice-cqrs-and-decorators)). Writes are `ICommandHandler`s, reads are `IQueryHandler`s, each in its own feature folder with the endpoint that maps it. Cross-cutting behaviour is added by decorators around the handlers, without a mediator.
- **Modules never reference each other.** Everything shared lives in `Shared/Kernel` as contracts. Modules coordinate at runtime through the transactional outbox, never by calling into one another ([§9](#9-cross-module-communication)).
- **Each module owns its own schema.** One PostgreSQL schema per module (`recruitment`, `files`, `audit`) in the shared `healthyhires` database, and one `DbContext` per module. A module reads and writes only its own tables.

There is deliberately **no Application layer and no repository pattern**. The EF Core `DbContext` is the unit of work and the repository; handlers use it directly.

## 2. Solution layout

```text
Api/
├─ ApiService/            The host: Program.cs, the HTTP pipeline, rate limiting, versioning,
│                         OpenAPI transformers, exception handlers.
├─ Modules/               One class library per module — the vertical slices.
│  ├─ RecruitmentModule/  The core: job offers, applications, CVs, the HR workflow.
│  ├─ FilesModule/        Uploads to Blob Storage, scanning, download URLs.
│  └─ AuditModule/        The audit trail, fed from the other modules' outboxes.
├─ Shared/
│  ├─ Kernel/             The ONLY thing modules may share:
│  │  ├─ CQRS/            ICommand, ICommandHandler, IQueryHandler + registration.
│  │  ├─ Decorators/      EventScopeDecorator, ValidationDecorator.
│  │  ├─ Endpoints/       IEndpoint, MapEndpoints, RequiresHeader, ProducesLink.
│  │  ├─ Errors/          ErrorCode, FieldErrorCode, CommonErrors, ProducesErrors.
│  │  ├─ Events/          EventScope, the ambient feature event name.
│  │  ├─ Exceptions/      The exceptions that map to HTTP status codes.
│  │  ├─ Idempotency/     The Idempotency-Key middleware, MapQuery.
│  │  ├─ Scheduling/      Daily and periodic worker hosts.
│  │  ├─ Telemetry/       [PersonalData] / [SensitiveData] log classifications.
│  │  └─ Validation/      [NotEmpty], [DecimalPrecision].
│  └─ ServiceDefaults/    Telemetry, health checks, service discovery, HttpClient resilience.
└─ Tests/                 One project mirroring the source layout. Real PostgreSQL (§12).
```

`Program.cs` composes them: every module exposes `Add<Name>Module()` to register its services and `Map<Name>Module()` to map its endpoints, and the host calls each. The ordering rules:

- Every `Add<Name>Module()` call comes first. **`AddCqrsDecorators()` runs once, after all of them**, so every command handler is wrapped exactly once. A handler registered after it is not decorated.
- The module endpoints are mapped on the versioned `/api/v{version}` group, which already carries the version set, the per-caller rate limit and `CommonErrors.AnyEndpoint`.

## 3. Anatomy of a module

A module has three top-level areas, `Domain`, `Features` and `Infrastructure`, plus its composition root:

```text
RecruitmentModule/
├─ RecruitmentModule.cs                   Composition root: AddRecruitmentModule / MapRecruitmentModule.
│
├─ Domain/                                What the module IS.
│  ├─ Entity/                             Database entities ONLY — one class per table.
│  │  ├─ JobOffer.cs
│  │  └─ Application.cs
│  ├─ EntityTypeConfiguration/            One IEntityTypeConfiguration<T> per entity.
│  │  └─ JobOfferConfiguration.cs
│  ├─ RecruitmentVocabulary.cs            Constants and string-value sets.
│  └─ RecruitmentErrors.cs                The module's error-code catalog.
│
├─ Features/                              What the module DOES — the vertical slices.
│  └─ JobOfferFeatures/
│     └─ CreateJobOffer/                  One folder per feature.
│        ├─ CreateJobOfferRequest.cs
│        ├─ CreateJobOfferHandler.cs      Command + Response + Handler.
│        └─ CreateJobOfferEndpoint.cs
│
└─ Infrastructure/                        Module-internal services and plumbing.
   ├─ RecruitmentDbContext.cs             The module's EF Core context.
   ├─ <Area>/                             A self-contained subsystem: its workers, clients, options, telemetry.
   ├─ Authorization/                      Authorization that needs the module's own data.
   ├─ Files/                              The file kinds this module owns.
   ├─ Messaging/                          Reactions to other modules' changes and events.
   ├─ Scheduling/                         Scheduled workers.
   ├─ Services/                           Module-internal domain services.
   └─ Validation/                         Validation the command attributes can't express.
```

### What each `Infrastructure` folder is for

- **`<Area>/`**: a subsystem that bundles its own workers, external clients, policies, options and telemetry, where splitting the files by role would scatter one mechanism across folders — an external integration, or a background process with its own rules. It is named after the area, not the role (`Scanning/` for the ClamAV pass in the Files module), and may have subfolders. Its workers stay in it, not in `Scheduling/`. Use it only when the parts depend on each other; a lone service still goes in `Services/` and a lone timer job in `Scheduling/`.
- **`Authorization/`**: authorization beyond a role policy on the endpoint ([§10](#10-authentication-and-authorization)) — when deciding whether the caller may touch a resource needs the module's own data or rules.
- **`Files/`**: the definitions of the file kinds this module owns, such as a CV, with their allowed content types and size limits. The Files module enforces them generically. The contract is `TODO` until the Files module exists.
- **`Messaging/`**: the module's reactions to changes and events from *other* modules, matched on the outbox contract strings, never on a code reference ([§9](#9-cross-module-communication)).
- **`Scheduling/`**: standalone `IScheduledWorker`s — work that runs on a timer instead of in response to a request, such as pruning expired application drafts ([scheduling.md](scheduling/scheduling.md)). A worker belonging to an `<Area>/` lives there instead.
- **`Services/`**: reusable logic shared by several features that isn't an endpoint, handler or entity. All such services go in this one flat folder; don't give each its own descriptively-named folder.
- **`Validation/`**: validation too complex for the attributes on a command — cross-field checks, or rules that need the database or reference data ([§6](#6-validation)).

The general rule: **shared services live in `Infrastructure/`; there is no `Common` folder.** Two more placement rules are easy to get wrong:

- **`Domain/Entity/` is for database entities only**, classes that map to a table. Nothing else goes there.
- **Constants and string-value sets go in the vocabulary class at the `Domain` root** (`RecruitmentVocabulary.cs`), not next to entities or features. It holds `ModuleTypes.ModuleName` (the schema and route-group name) and `ModuleEvents` (the feature event names).

## 4. How to create a module

A module is a class library under `Api/Modules/<Name>Module` plus a database schema.

**1. Create the project and the schema.** Add the project to `Api/Api.slnx` and `healthyhires.slnx`, reference `Shared/Kernel`, and reference the module from `ApiService`. Package versions go in `Api/Directory.Packages.props`, never in the project file. The tables live in a schema named after the module (singular `snake_case`), created by `configs/postgres/init/01-create-db.sql` — EF Core does not create them ([ADR-D002](../db/decision-log.md#adr-d002--the-schema-is-an-init-ddl-script-until-production), [§8](#8-entities-and-persistence)). Document the schema in `docs/db/modules/`, bump the schema version in `docs/db/README.md` and add a `docs/db/changelog.md` entry.

**2. Add the `Domain`.** Entities under `Domain/Entity/`, an `IEntityTypeConfiguration<T>` per entity under `Domain/EntityTypeConfiguration/`, and at the `Domain` root:

- `<Name>Vocabulary.cs`, starting with `ModuleTypes.ModuleName`;
- `<Name>Errors.cs`, the module's error-code catalog ([errors.md § Typed catalogs](errors/errors.md#typed-catalogs)). Add it to the catalog list in `ErrorCodeCatalogTests`.

**3. Add the `DbContext`** under `Infrastructure/`:

```csharp
public sealed class RecruitmentDbContext(DbContextOptions<RecruitmentDbContext> options) : DbContext(options)
{
    public DbSet<JobOffer> JobOffers => Set<JobOffer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleTypes.ModuleName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RecruitmentDbContext).Assembly);
    }
}
```

The outbox (`<module>.outbox_message`) joins the context when it lands in the Kernel ([§9](#9-cross-module-communication)).

**4. Write the composition root**, `<Name>Module.cs`:

```csharp
public static class RecruitmentModule
{
    public static IServiceCollection AddRecruitmentModule(this IServiceCollection services)
    {
        // The host registers one NpgsqlDataSource ("healthyhires"); it carries the managed-identity sign-in in
        // production, so every module context reuses it instead of a connection string of its own.
        services.AddDbContextPool<RecruitmentDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<NpgsqlDataSource>(),
                npgsql => npgsql.EnableRetryOnFailure())); // ADR-A011 at the database layer

        services.AddCqrsHandlers(typeof(RecruitmentModule).Assembly); // every handler in the module, scoped

        // Scheduled workers and Infrastructure/Services go here.
        return services;
    }

    public static IEndpointRouteBuilder MapRecruitmentModule(this IEndpointRouteBuilder api)
    {
        api.MapGroup($"/{ModuleTypes.ModuleName}")
            .WithTags(ModuleTypes.ModuleName)
            .MapEndpoints(typeof(RecruitmentModule).Assembly);
        return api;
    }
}
```

- `AddCqrsHandlers` scans the assembly with Scrutor and registers every `ICommandHandler<>`, `ICommandHandler<,>` and `IQueryHandler<,>`, public or not, as **scoped**, so a handler shares the request's `DbContext`. You never register a handler by hand.
- `MapEndpoints` creates every concrete `IEndpoint` in the assembly and maps it on the group, in type-name order, so the OpenAPI document keeps a stable order between builds. An endpoint class needs a parameterless constructor; request-time services go into the route delegate, not the class.

**5. Wire it into the host.** In `Program.cs`, call `builder.Services.AddRecruitmentModule();` **before** `AddCqrsDecorators()`, and `api.MapRecruitmentModule();` in the module-endpoints block on the versioned group.

That is the whole module. Everything else is features.

## 5. How to create a feature

A feature is **one folder**, `Features/<Area>/<Feature>/`, holding three files.

**`<Feature>Request.cs`**: the HTTP request body, a `record`. It is the public wire contract and carries the `<example>` XML docs for OpenAPI. It holds no validation attributes; the schema gets them from the command ([§6](#6-validation)).

**`<Feature>Handler.cs`**: the **command** (or **query**), the **response** and the **handler**, kept together.

- The **command** implements `ICommand` and names its feature event: `public static string EventName => ModuleEvents.JobOfferCreated;`, a lower `snake_case`, past-tense constant from the vocabulary, with no module prefix. The compiler requires it. While the command runs, the name is ambient ([§7](#7-the-decorator-chain)), so the audit trail and other modules can tell *which feature* made a change.
- The command carries the **validation**, as DataAnnotations attributes ([§6](#6-validation)). Queries are not validated.
- The **handler** implements `ICommandHandler<TCommand, TResult>`, the void `ICommandHandler<TCommand>`, or `IQueryHandler<TQuery, TResult>`. It takes its `DbContext` and services through the primary constructor and works against the `DbContext` directly.
- It **signals failure by throwing** a `Kernel.Exceptions` type: `NotFoundException` → 404, `ConflictException` → 409, `ValidationException` → 400, and so on. A specific reason gets a code from the module's catalog: `new ConflictException(…) { Code = RecruitmentErrors.OfferClosed }`. Add a new code to the catalog first; a string is not accepted ([errors.md § Error codes](errors/errors.md#error-codes)).

**`<Feature>Endpoint.cs`**: an `internal sealed class` implementing `IEndpoint`. Its `MapEndpoint(IEndpointRouteBuilder group)` maps the route, builds the command from the request and route values **by hand** ([ADR-A012](../architecture/ADR-ASPNET.md#adr-a012-dto-mapping--manual-mapping)), dispatches it through the injected handler and returns the result. Nothing in the module root changes: the `MapEndpoints` scan finds it.

### The shape

```csharp
// CreateJobOfferHandler.cs
public sealed record CreateJobOfferCommand : ICommand
{
    public static string EventName => ModuleEvents.JobOfferCreated; // "job_offer_created"

    [Required, StringLength(200)] public string Title { get; init; } = string.Empty;
    [Range(0d, double.MaxValue, MinimumIsExclusive = true)] public decimal? SalaryMin { get; init; }
    [Range(0d, double.MaxValue, MinimumIsExclusive = true)] public decimal? SalaryMax { get; init; }
}

public sealed record CreateJobOfferResponse(Guid Id, string Title);

internal sealed class CreateJobOfferHandler(RecruitmentDbContext db)
    : ICommandHandler<CreateJobOfferCommand, CreateJobOfferResponse>
{
    public async Task<CreateJobOfferResponse> HandleAsync(CreateJobOfferCommand command, CancellationToken ct = default)
    {
        // A rule spanning two fields: attributes cannot say it, so the handler does.
        if (command.SalaryMin > command.SalaryMax)
            throw ValidationException.ForField(nameof(command.SalaryMin), RecruitmentErrors.SalaryRangeInverted,
                "The minimum salary is above the maximum.");

        JobOffer offer = new() { Id = Guid.CreateVersion7(), Title = command.Title };
        db.JobOffers.Add(offer);
        await db.SaveChangesAsync(ct);
        return new CreateJobOfferResponse(offer.Id, offer.Title);
    }
}
```

```csharp
// CreateJobOfferEndpoint.cs
internal sealed class CreateJobOfferEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapPost("/job-offers", async (
                CreateJobOfferRequest request,
                ICommandHandler<CreateJobOfferCommand, CreateJobOfferResponse> handler,
                CancellationToken ct) =>
            {
                CreateJobOfferCommand command = new() // manual map (ADR-A012)
                {
                    Title = request.Title,
                    SalaryMin = request.SalaryMin,
                    SalaryMax = request.SalaryMax,
                };
                CreateJobOfferResponse result = await handler.HandleAsync(command, ct);
                return Results.Created($"/job-offers/{result.Id}", result);
            })
            .WithName("CreateJobOffer")
            .Produces<CreateJobOfferResponse>(StatusCodes.Status201Created)
            .ProducesErrors(RecruitmentErrors.SalaryRangeInverted);
}
```

### Rules for handlers and endpoints

- **Take ids from the route**, never from the body. The route is authoritative.
- **Give every endpoint a name** with `.WithName(...)`. It becomes the `operationId`, which is what a link targets.
- **Declare the success response** with `.Produces<T>()`, and **every error code** the handler and the services it calls can answer with `.ProducesErrors(...)`, which also declares each code's status. The rule codes of the command's attributes, `concurrency.conflict` on a write, and the idempotency rejections are added for you ([errors.md § Declaring what an endpoint answers](errors/errors.md#declaring-what-an-endpoint-answers)).
- **Declare the statuses no handler raises:** a route with a path parameter can answer `404` (the id may name nothing), so declare `CommonErrors.NotFound` even when the handler never throws it. A body or a query parameter can answer `400`, which the versioned group already declares through `request.malformed`.
- **A read that needs a request body** is mapped with `MapQuery`, not `MapPost`, and dispatches an `IQueryHandler` ([idempotency.md § Reads exposed as POST](idempotency/idempotency.md#reads-exposed-as-post)).
- **Handlers and endpoints are `internal`.** The scans find non-public types, so nothing outside the module can call one directly ([ADR-A001](../architecture/ADR-ASPNET.md#adr-a001-application-architecture--modular-monolith)).
- **Log through `[LoggerMessage]`** only, with an event id from the module's range, and mark every argument carrying applicant data `[PersonalData]` or `[SensitiveData]`. A direct `LogX` call or an interpolated template is a build error ([development-architecture.md §1.1](../architecture/development-architecture.md#11-backend-api)).

Two things the host already guarantees, so a handler does not have to. Every error response carries a `ProblemDetails` body, including the ones no handler produces (`401`, `403`, a routing `404`, `405`). And every `DateTimeOffset` in a body is normalised to UTC before the command is built, because the columns are `TIMESTAMPTZ`; never re-normalise.

### 5.1 Linking a response to what it unlocks

A response that hands back an id is the only place a caller can learn it, because ids are server-generated UUIDs. `.ProducesLink(...)` states this in the document as an OpenAPI *link*, the one construct in OpenAPI that describes a sequence rather than a single call. Schemathesis's stateful phase follows them to chain calls ([contract-testing.md](../architecture/contract-testing.md)), and Scalar shows them as the next call to make.

```csharp
.WithName("GetJobOffer")
.Produces<GetJobOfferResponse>(StatusCodes.Status200OK)
.ProducesLink(StatusCodes.Status200OK, "GetApplication",
    ("jobOfferId", "$request.path.jobOfferId"),
    ("applicationId", "$response.body#/applications/0/id"));
```

- **The first argument** is the response status carrying the value, usually `200` or `201`. The endpoint must declare that status; a link on an undeclared status is left out of the document.
- **The second argument** is the target's `operationId`. The link is named after it, so declare several links by calling `ProducesLink` more than once.
- **The parameters** map each of the target's path parameters to a runtime expression, in one of two forms:
  - `$request.path.<name>`: a path parameter of *this* request, carried through.
  - `$response.body#/<pointer>`: a JSON pointer into this response's body. In `applications/0/id` the `0` is literal: a link names one value, so a list offers its first row.
- **Fill every path parameter of the target.** A link to `/job-offers/{jobOfferId}/applications/{applicationId}` that supplies only the application id leaves the caller inventing an offer, and the call is answered `404`.
- **Link within the module.** A link is only a string, but naming another module's operation still makes this feature depend on that module's contract.

`ResponseLinkTransformer` writes the links into the document. Nothing at compile time relates a link's strings to the endpoint on the other side, so `ResponseLinkDeclarationTests` will check every link against the endpoint it names (`TODO(first-module)`, [first-module-tests.md](first-module-tests.md)).

### 5.2 Rules on route and query values

A validation attribute on a route or query parameter is checked by the framework at runtime ([ADR-A008](../architecture/ADR-ASPNET.md#adr-a008-validation--native-aspnet--net-10-validation)), but the framework does not put it in the document:

```csharp
group.MapGet("/job-offers/{jobOfferId:guid}/applications", (
    Guid jobOfferId,
    [AllowedValues("new", "screening", "interview", "closed")] string? status,
    [Range(1, 100)] int? pageSize, …) => …);
```

`ParameterValidationTransformer` copies those rules onto the parameters, using the same rules as the request body. Such a failure is answered `request.validation_failed` without per-field codes. Keep rules on the command where you can, and on a parameter only when the value never reaches one.

### 5.3 Declaring a required header

A header the endpoint refuses to run without, but enforces itself in a filter or middleware, is invisible to the document: a generated client never sends it, and a request that satisfies the whole schema is answered `400`. Declare it, on the group when that is where the enforcement lives:

```csharp
group.RequiresHeader("X-Client-Instance", "Identifies the browser tab the draft belongs to.", format: "uuid");
```

`RequiredHeaderTransformer` adds it as a required header parameter, with the `400` a missing or malformed value produces. A header already declared some other way, such as `Idempotency-Key`, is left alone.

## 6. Validation

A command's rules are plain DataAnnotations attributes on its properties: `[Required]`, `[StringLength]`, `[Range]`, `[AllowedValues]`, and the Kernel's `[NotEmpty]` and `[DecimalPrecision]`.

When an attribute fails, `ValidationDecorator` throws a `ValidationException` (`400`) carrying the English message and the attribute's code under `errorCodes` ([errors.md § Validation errors](errors/errors.md#validation-errors)). A failing `[Required]` short-circuits that property's other rules, so a missing value reports `required` and nothing else. Class-level attributes and `IValidatableObject` are not consulted; a rule spanning several fields belongs in the handler, or in `Infrastructure/Validation/` when it needs the database.

The attributes live on the command, but the OpenAPI document describes the request DTO the endpoint binds. **`CommandValidationSchemaTransformer` joins the two by name**: it copies each rule from `<Feature>Command` or `<Feature>Query` onto the matching property of `<Feature>Request`, provided both are in the same namespace. Properties are matched by name, ignoring case.

A rule left out of the schema is a value the document calls legal and the API rejects, which is exactly what a Schemathesis run reports ([ADR-009](../architecture/ADR.md#adr-009-api-testing--contract-validation--schemathesis)). So:

- **Express a rule as an attribute wherever an attribute can say it**, and keep the request and command names paired. "Strictly positive" is `[Range(0d, double.MaxValue, MinimumIsExclusive = true)]`, which becomes `exclusiveMinimum` rather than a `minimum` that advertises `0`. A fixed set is `[AllowedValues]`, built from the vocabulary constants the handler compares against.
- **A `decimal` `[Range]` sets `ParseLimitsInInvariantCulture = true`** (and `ConvertValueInInvariantCulture = true`); otherwise it throws under a comma-decimal culture.
- **Queries are not validated.** An attribute on a query only documents a rule; the handler still has to enforce it. Put the attribute and the check side by side, referencing the same constants.

## 7. The decorator chain

Every command handler is wrapped like this, outermost first:

```text
EventScopeDecorator  →  ValidationDecorator  →  the handler
```

- **`EventScopeDecorator`** makes the command's `EventName` the ambient `EventScope.Current` while the command runs, and restores the previous one afterwards. Anything that records a change during the command, such as an outbox row or an audit entry, reads the name from there instead of every handler passing it along.
- **`ValidationDecorator`** validates the command ([§6](#6-validation)). An invalid command never reaches the handler.

Query handlers are not decorated: a query changes nothing, so it has no rules to enforce before it runs and no event to name.

### Adding a decorator

1. **Write it for both shapes** in `Kernel.Decorators`: a class implementing `ICommandHandler<TCommand>` that takes the inner `ICommandHandler<TCommand>`, and the same for `ICommandHandler<TCommand, TResult>`.
2. **Register it in `AddCqrsDecorators`** with `services.TryDecorate(...)` for each shape. Scrutor wraps whatever is registered at that point, so **a decorator added later sits further out**; place it in the list by where it belongs in the chain.
3. **Test the order** in `CqrsRegistrationTests`, the way the existing chain is tested.

## 8. Entities and persistence

Persistence is EF Core on PostgreSQL ([ADR-A005](../architecture/ADR-ASPNET.md#adr-a005-persistence--postgresql-with-entity-framework-core)). The table rules are in [conventions.md](../db/conventions.md); what they mean for an entity:

- **The SQL script owns the schema, not EF.** Tables are created by `configs/postgres/init/01-create-db.sql` ([ADR-D002](../db/decision-log.md#adr-d002--the-schema-is-an-init-ddl-script-until-production)), so a configuration maps onto an existing table with `b.ToTable("job_offer", t => t.ExcludeFromMigrations())` and names each column with `HasColumnName("snake_case")`. Change the script, the module's table documentation and the configuration in the same change.
- **Ids are application-assigned v7 GUIDs:** `Id = Guid.CreateVersion7()` in the handler, configured `ValueGeneratedNever()`.
- **Audit columns:** `created_at` is a column default, `created_by` is set by the handler from the acting user, `updated_at` and `version` are maintained by the audit trigger, `updated_by` by the handler. `Version` is a `short` concurrency token: `IsConcurrencyToken().ValueGeneratedOnAddOrUpdate()`. A stale write raises `DbUpdateConcurrencyException`, which the host answers with `409 concurrency.conflict`.
- **References to another module's rows are logical:** a plain `Guid` with no foreign key and no navigation property ([conventions.md § Cross-service references](../db/conventions.md#cross-service--cross-module-references-soft-references)).

Handlers use the `DbContext` directly: `db.JobOffers.Add(...)`, projections with `.AsNoTracking()` for reads, `await db.SaveChangesAsync(ct)`. **No repository wrapper**; the context already is one.

## 9. Cross-module communication

> **The rule: a module never references another module.** No project reference, no `using` of another module's namespace, no calling its handlers. This is what keeps modules extractable.

When a module needs to react to something in another, it goes through the **transactional outbox**: each producing module writes `<module>.outbox_message` rows in the same transaction as its change, and a dispatcher delivers them to the consumers registered in other modules ([conventions.md § Outbox pattern](../db/conventions.md#outbox-pattern-outbox_message)). Three kinds of row share it:

- **`row_change`**: captured automatically for every insert, update or delete of an audited entity. A consumer subscribes by the *contract strings* `(module, entity_type, action)`, never by a type — e.g. Files reacting to a `recruitment` / `application` / `delete`.
- **`domain_event`**: a fact a row change can't express, such as "a job offer's deadline has passed", staged by the owning module in the same unit of work. Consumers subscribe by `(module, event)`.
- **`audit_event`**: an audited action that changes nothing, such as a staff member downloading a CV. The Audit module consumes `row_change` and `audit_event` rows.

Delivery is **at-least-once**, so every consumer is **idempotent**, keyed on the outbox row's `id`, and receives a batch in occurrence order. Consumers live in the module's `Infrastructure/Messaging/` and are registered by an assembly scan in `Add<Name>Module`.

The outbox, change capture and the consumer contracts are not in the Kernel yet (`TODO`); they arrive with the Audit module.

If two modules need a small piece of *shared vocabulary* — a contract type, a role name, an interface the host resolves — it goes in `Shared/Kernel`, the only sanctioned shared surface.

## 10. Authentication and authorization

Full detail in [iam.md](../architecture/iam.md). There are two kinds of caller:

- **Staff** sign in with Entra ID and send a bearer token. Their app role (`Worker`, `Admin` or `Owner`) arrives in the `roles` claim, and their `oid` identifies them in the database, e.g. as `created_by`. An endpoint declares what it needs with `.RequireAuthorization(...)`, and `auth.forbidden` is declared for it by rule. A validated token already proves the user exists, so **never re-query whether the acting user exists**.
- **Applicants** are anonymous. Their endpoints are protected by ALTCHA, the per-IP rate limit and short-lived SAS URLs, not by identity, and never answer `auth.unauthenticated`.

Staff sign-in, the role policies and the current-user accessor are not wired yet (`TODO`); until they are, take the acting user as a parameter of the command rather than inventing one.

## 11. Conventions cheat-sheet

- **Vertical slices, CQRS.** One feature = one folder = Request + Command/Response/Handler + Endpoint. Writes are `ICommandHandler`, reads are `IQueryHandler`.
- **No Application layer, no repository.** Use the `DbContext` directly.
- **Manual DTO mapping** ([ADR-A012](../architecture/ADR-ASPNET.md#adr-a012-dto-mapping--manual-mapping)). No AutoMapper.
- **Validation lives on the command** as DataAnnotations; the decorator enforces it, and the request schema copies it. Queries aren't validated.
- **Every command names its event** with a `ModuleEvents` constant: lower `snake_case`, past tense, no module prefix.
- **Codes come from a catalog**, never a string, and every reachable one is declared with `.ProducesErrors(...)`.
- **A response carrying an id another route takes declares `.ProducesLink(...)`**, filling every path parameter of the target and staying inside the module.
- **Explicit types, not `var`**, including in tests. Anonymous-typed projections are the exception.
- **`Domain/Entity` is database entities only; constants live in the `Domain`-root vocabulary; shared services live in `Infrastructure`.** There is no `Common` folder.
- **Modules never reference modules.** Coordinate through the outbox; share only through `Shared/Kernel`.
- **Reuse over parallel abstractions.** Reuse an existing command, query or contract instead of duplicating its logic.
- **Logs go through `[LoggerMessage]`** with applicant data classified.
- **Ids are `Guid.CreateVersion7()`; tables are `ExcludeFromMigrations`**, owned by the SQL script.

## 12. Testing

Tests live in `Api/Tests`, mirroring the source layout: `Tests/<Name>Module/Features/…` for a module, `Tests/Shared/` for the Kernel, `Tests/ApiService/` for the host. They run on xUnit v3 with `dotnet test --solution Api/Api.slnx`.

Tests run against a **real PostgreSQL**, not an in-memory provider. The shared container builds a template database from `configs/postgres/init/01-create-db.sql`, and each schema-backed test gets a fresh clone (`PostgresTestHost.CreateDatabase`). The practical consequence: **seed data must satisfy the real schema** — foreign keys, check constraints and triggers all fire — so seed the rows a handler depends on, not a minimal stub. Blob Storage tests use Azurite through Testcontainers ([ADR-A010](../architecture/ADR-ASPNET.md#adr-a010-file-storage--azure-blob-storage-via-azure-sdk)).

The Kernel tests that pin the CQRS and endpoint machinery:

| Tests | What they pin |
| --- | --- |
| `CqrsRegistrationTests` | The Scrutor scan registers each handler shape, `internal` ones included, as scoped. Command handlers are wrapped once, with the event scope outside validation. Queries are left alone. Decorating with nothing registered is a no-op. |
| `ValidationDecoratorTests` | An invalid command never reaches its handler and carries a code per failure; `required` reports alone; the void form validates too |
| `CommandValidationSchemaTransformerTests` | Exclusive bounds, open upper bounds, enums, lengths and messages carry over from the command to the request schema |
| `EndpointRegistrationTests` | Every `IEndpoint` in an assembly is mapped on the group, in type-name order |
| `ResponseLinkTransformerTests` | A declared link reaches the document with its target and expressions, several links are named after their targets, and one on an undeclared status is skipped |
| `RequiredHeaderTransformerTests` | A group's header reaches every endpoint in it, with its `400`, and nothing outside the group; a header already declared is not added twice |
| `ParameterValidationTransformerTests` | Allowed values, ranges and lengths on route and query parameters reach the document, and a parameter without rules is untouched |
| `ErrorCodeTransformerTests` | A dispatched command's rule codes are listed under the operation's `400` |

`StubHandler` and `StubVoidHandler` (`Tests/Shared/CQRS/`) stand in for an inner handler in a decorator test. `CommandValidation` (`Tests/Shared/Decorators/`) runs the decorator's validator on any object, for an attribute test.

The first module also brings four tests that check every mapped endpoint — declared codes, declared statuses, `MapQuery` opt-outs and links ([first-module-tests.md](first-module-tests.md)).

## 13. Checklists

### Add a feature

- [ ] `Features/<Area>/<Feature>/` folder created.
- [ ] `<Feature>Request.cs`: the wire contract with `<example>` docs, no validation attributes.
- [ ] `<Feature>Handler.cs`: Command or Query (with DataAnnotations and `EventName` for a command), Response, `internal` Handler.
- [ ] Handler uses the module `DbContext` directly and throws `Kernel.Exceptions` with codes from the module catalog.
- [ ] `<Feature>Endpoint.cs`: `internal sealed class : IEndpoint`, manual mapping, ids from the route, `.WithName(...)`, `.Produces<T>()`, `.ProducesErrors(...)`, authorization on staff endpoints.
- [ ] `MapQuery` instead of `MapPost` for a read with a body.
- [ ] `.ProducesLink(...)` if the response carries an id another operation takes in its route.
- [ ] New error codes translated in the frontend's SK, CZ and EN files.
- [ ] Tests under `Tests/<Name>Module/Features`, with seed data that satisfies the real schema.

### Add a module

- [ ] Project `Api/Modules/<Name>Module` in both solutions, referencing `Shared/Kernel`, referenced by `ApiService`.
- [ ] Schema and tables in `configs/postgres/init/01-create-db.sql`; documented in `docs/db/modules/`, schema version bumped, changelog entry.
- [ ] `Domain/Entity`, `Domain/EntityTypeConfiguration`, `<Name>Vocabulary.cs` with `ModuleTypes.ModuleName`, `<Name>Errors.cs` added to `ErrorCodeCatalogTests`.
- [ ] `Infrastructure/<Name>DbContext.cs` with the module's default schema.
- [ ] `<Name>Module.cs`: `Add<Name>Module` (pooled `DbContext` on the shared data source, `AddCqrsHandlers`) and `Map<Name>Module`.
- [ ] `Program.cs`: `Add<Name>Module()` before `AddCqrsDecorators()`, `Map<Name>Module()` on the versioned group.
