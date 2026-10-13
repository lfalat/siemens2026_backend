using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ApiService.Infrastructure;
using ApiService.Infrastructure.ExceptionHandlers;
using ApiService.OpenAPI;
using Asp.Versioning;
using Asp.Versioning.Builder;
using Kernel.CQRS;
using Kernel.Errors;
using Kernel.Idempotency;
using Kernel.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ADR-A009: redact classified LoggerMessage arguments before any provider (console, journal or OTLP) sees them.
// HealthyHiresRedaction maps both classes in Kernel.Telemetry to ErasingRedactor and erases unknown
// classifications too. Applicant data is personal data, so this is in place before the first module logs any.
builder.Services.AddHealthyHiresRedaction();
builder.Logging.EnableRedaction();

// Telemetry, health checks, service discovery and HttpClient resilience (Shared/ServiceDefaults).
builder.AddServiceDefaults();

// ADR-A014: SignalR. Hubs are mapped per feature, and only for signed-in staff — applicants never connect.
builder.Services.AddSignalR();

// ADR-A006: HybridCache, in-process (L1) only for now. Registered by the host so every module shares one cache;
// AddHybridCache uses TryAdd, so a module that also calls it changes nothing.
builder.Services.AddHybridCache();

// Connection strings come from the AppHost locally (ConnectionStrings__healthyhires, ConnectionStrings__blobs)
// and from configuration in production. Both integrations add a health check that /health reports.
// The Azure variant signs in to PostgreSQL with the VM's managed identity when the connection string has no
// password (ADR-006, iam.md §4.2); locally the AppHost's connection string carries one, so nothing changes there.
builder.AddAzureNpgsqlDataSource("healthyhires");
builder.AddAzureBlobServiceClient("blobs");

// ADR-A007: the client IP for anonymous applicants comes from X-Forwarded-For, which only Caddy may set. The
// defaults trust loopback alone as a proxy, and Caddy is the only loopback peer: it shares the pod's network
// namespace (container-deployment.md §1.1) and overwrites any X-Forwarded-For a client sends. Trusting the
// header from anywhere else would let callers choose their own rate-limit partition.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor);

// ADR-A007: Rate Limiting. No CDN or WAF sits in front of the application, so this is the only throttling
// layer: per signed-in staff user, and per client IP for anonymous applicants.
builder.Services.AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(RateLimitPolicies.PerCaller, httpContext =>
    {
        // Staff are partitioned by their subject, so colleagues behind one office NAT do not share a budget.
        // JwtBearer maps `sub` to NameIdentifier unless inbound claim mapping is turned off, so accept both.
        string? subject = httpContext.User.FindFirstValue("sub")
                          ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        string partition = subject is { Length: > 0 }
            ? "user:" + subject
            : "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        // Resolved per partition rather than captured once, so the budget follows configuration instead of
        // being baked in at startup (see RateLimitOptions).
        RateLimitOptions budget = httpContext.RequestServices
            .GetRequiredService<IOptions<RateLimitOptions>>().Value;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partition,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = budget.PermitLimit,
                Window = budget.Window,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = budget.QueueLimit,
            });
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        // Tell the client when to come back rather than leaving it to guess — and answer in the same
        // ProblemDetails shape as every other error, instead of a bare string.
        TimeSpan retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan after)
            ? after
            : context.HttpContext.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.Window;
        context.HttpContext.Response.Headers.RetryAfter =
            ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);

        IProblemDetailsService problems =
            context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

        await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status429TooManyRequests,
                Detail = "Too many requests. Retry after the interval in the Retry-After header.",
            },
        });
    };
});

// ADR-A008: native request validation
builder.Services.AddValidation();

// ADR-A003: CQRS. Each module registers its handlers with AddCqrsHandlers(assembly) in its own Add<Module>Module;
// this wraps every command handler, once, in validation and then the event scope. It must come AFTER every
// module's registration: a handler registered later is not decorated.
builder.Services.AddCqrsDecorators();

// ADR-A016: Idempotency for POST/PATCH. A client-supplied Idempotency-Key makes a write at-most-once, with its
// successful response replayed for 1h (per-endpoint override via .WithIdempotencyTtl). Applied by the middleware
// below; endpoints with their own dedup opt out via .DisableIdempotency, and reads exposed as POST are mapped with
// .MapQuery, which opts them out for the same reason (ADR-A015).
builder.Services.AddIdempotency();

// Claims go to Postgres (kernel.idempotency) rather than staying in this process. In memory they die with the
// process, so a deployment or a crash forgets them — and the retry that follows a crash is close to certain,
// because the client never got its response. The context reuses the host's data source, which carries the
// managed-identity sign-in in production. The retention pass runs daily at 03:00 UTC.
builder.Services.AddDbIdempotencyStore(
    (sp, options) => options.UseNpgsql(
        sp.GetRequiredService<NpgsqlDataSource>(),
        npgsql => npgsql.EnableRetryOnFailure()), // ADR-A011 at the database layer
    new TimeOnly(3, 0));

// ADR-A004: API Versioning + OpenAPI
builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.ReportApiVersions = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new QueryStringApiVersionReader("api-version"),
            new HeaderApiVersionReader("X-API-Version"));
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true; // so docs/links show /api/v1/...
    })
    // One OpenAPI document per API version (/openapi/v1.json, ...), each describing only that version.
    .AddOpenApi(options =>
    {
        // Puts `type: string` back on every timestamp. The custom UTC converter is opaque to the schema
        // generator, which otherwise leaves them typeless — and a typeless schema permits anything.
        options.Document.AddSchemaTransformer<DateTimeOffsetSchemaTransformer>();

        // ADR-A003: validation rules live on the CQRS command — mirror them into the request schema the endpoint
        // binds, so the document does not call a value legal that the validation decorator then rejects.
        options.Document.AddSchemaTransformer<CommandValidationSchemaTransformer>();

        // The same rules for path and query parameters, which the framework's generator does not carry across at
        // all — an attribute there is enforced at runtime and invisible in the document.
        options.Document.AddOperationTransformer<ParameterValidationTransformer>();

        // The Idempotency-Key header and the rejections the middleware produces before an endpoint runs, on
        // exactly the operations it covers — without it, Schemathesis (ADR-009) meets undeclared 400/409/422s.
        options.Document.AddOperationTransformer<IdempotencyHeaderTransformer>();

        // Headers an endpoint or group refuses to run without but enforces itself (.RequiresHeader): without this
        // the document describes a request the API then rejects.
        options.Document.AddOperationTransformer<RequiredHeaderTransformer>();

        // The links endpoints declare with .ProducesLink: which operation a value in this response feeds. The only
        // part of the document that describes a sequence, and what Schemathesis's stateful phase chains (ADR-009).
        options.Document.AddOperationTransformer<ResponseLinkTransformer>();

        // ADR-A017: the error codes each operation can answer with, under each error response — after the idempotency
        // transformer, whose responses it adds codes to — and the full catalog as the ErrorCode / FieldErrorCode enums.
        options.Document.AddOperationTransformer<ErrorCodeTransformer>();
        options.Document.AddDocumentTransformer<ErrorCodeCatalogTransformer>();
    });

// Kernel.Exceptions, a stale optimistic-concurrency version and an unparseable request are the caller's to fix:
// answered with their own status and code, never logged as faults (docs/api/errors/errors.md).
builder.Services.AddExceptionHandler<AppExceptionHandler>();

// A NUL byte in any text field is the caller's data, not our fault — without this it reaches PostgreSQL and
// escapes as a 500 (ADR-009). Before the catch-all, which must be last.
builder.Services.AddExceptionHandler<UnstorableTextExceptionHandler>();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Timestamp columns are TIMESTAMPTZ, which Npgsql binds only from a zero-offset DateTimeOffset — but an
    // offset is an ordinary part of an RFC 3339 timestamp. Normalising on the way in keeps that from being a 500.
    options.SerializerOptions.Converters.Add(new UtcDateTimeOffsetConverter());

    // JsonSerializerDefaults.Web accepts a number written as a string ("5.5"), which no HealthyHires client
    // sends. Keeping it costs more than it gives: the schema has to describe those fields as
    // `["number","string"]`, and JSON Schema's `minimum` applies only to the number branch — so the document
    // cannot state a bound on a form of input it still accepts. Reading only; writing is unchanged.
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ProblemDetailsTelemetry.Customize;
});

WebApplication app = builder.Build();

// First, so everything after it — the rate limiter's partition, logs, telemetry — sees the client's address
// instead of Caddy's.
app.UseForwardedHeaders();

app.UseExceptionHandler();

// Gives a body to the error responses the framework produces without one. The routing miss (404), the method
// mismatch (405) and, once sign-in exists, the authentication challenge (401) and authorization failure (403)
// are written by middleware that sets a status and stops — no Content-Type, no payload — while the frontend
// maps API errors from ProblemDetails (ADR-F010). With AddProblemDetails registered above, this writes the same
// shape as every other error. A response that already has a body, such as the rate limiter's 429, passes
// through untouched.
app.UseStatusCodePages();

// ADR-A007: after UseAuthentication once staff sign-in is added, because the policy partitions by the
// authenticated user and the principal does not exist before that point.
app.UseRateLimiter();

// ADR-A016: replay or short-circuit POST/PATCH requests carrying a repeated Idempotency-Key. Keys are scoped to
// the signed-in user, so this stays after UseAuthentication once staff sign-in is added; before the endpoints
// run either way.
app.UseIdempotency();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().WithDocumentPerVersion();
    app.MapScalarApiReference(options =>
    {
        options.AddDocuments(
            app.DescribeApiVersions()
                .Reverse()
                .Select(d => d.GroupName));
    });
}

// /health: every dependency check passes; public through Caddy, so it is rate limited per client IP as well
// as cached (container-deployment.md §1.3). /alive: the process responds at all.
app.MapDefaultEndpoints(health => health.RequireRateLimiting(RateLimitPolicies.PerCaller));

ApiVersionSet versionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .Build();

// ── Module Endpoints ─────────────────────────────────────
RouteGroupBuilder api = app.MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(versionSet)
    .RequireRateLimiting(RateLimitPolicies.PerCaller)
    // What any endpoint here can answer before or around its own handler (ADR-A017).
    .ProducesErrors([.. CommonErrors.AnyEndpoint]);

api.MapGet("/hello", () => "Hello, HealthyHires!");
// ─────────────────────────────────────────────────────────

await app.RunAsync();
