using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // ADR-A011: retries, timeouts and circuit breaking for every outgoing HttpClient call.
            http.AddStandardResilienceHandler();

            // Resolves names like https+http://apiservice to the endpoints the AppHost assigned.
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                // Module meters are added here as modules define them.
                metrics.AddAspNetCoreInstrumentation()
                    .AddMeter("HealthyHires.Idempotency") // Kernel.Idempotency.IdempotencyMetrics.MeterName
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                // Database and Blob Storage spans come from the Aspire client integrations (Aspire.Azure.Npgsql,
                // Aspire.Azure.Storage.Blobs), which register their own sources (ADR-A009).
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // OTLP only: locally the AppHost points this at the Aspire dashboard; on the VM it is the
        // Collector on localhost:4317, which forwards to Azure Monitor (ADR-007.1). The API itself
        // never talks to Azure Monitor, so there is no Azure Monitor exporter here.
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        // Backs the /health result cache in MapDefaultEndpoints.
        builder.Services.AddOutputCache();

        return builder;
    }

    /// <param name="app">The application to map the endpoints on.</param>
    /// <param name="configureHealth">
    /// Further conventions for <c>/health</c>, such as the host's rate-limiting policy, which this project
    /// does not know about.
    /// </param>
    public static WebApplication MapDefaultEndpoints(
        this WebApplication app,
        Action<IEndpointConventionBuilder>? configureHealth = null)
    {
        // Mapped in every environment, unlike the template default: Caddy exposes /health to the
        // availability tests (ADR-016, container-deployment.md §1.3), and a readiness check that only
        // exists in development cannot report a dependency outage in production. /alive stays inside the
        // pod. The default response writer emits only the aggregate status word — no check names, no
        // exception messages — so exposing /health discloses nothing beyond up or down.

        // Only GET and HEAD: the cache key includes the method, so any other method would bypass the cache.
        HttpMethodMetadata getOrHead = new([HttpMethods.Get, HttpMethods.Head]);

        // Caches only endpoints that ask for it (CacheOutput below); every other endpoint is unaffected.
        app.UseOutputCache();

        // All health checks must pass for app to be considered ready to accept traffic after starting.
        // Public, and every run queries PostgreSQL and Blob Storage, so the result is cached: however often
        // it is called, the checks run at most once per HealthCheckCachePolicy.Duration.
        IEndpointConventionBuilder health = app.MapHealthChecks(HealthEndpointPath)
            .WithMetadata(getOrHead)
            .CacheOutput(new HealthCheckCachePolicy());
        configureHealth?.Invoke(health);

        // Only health checks tagged with the "live" tag must pass for app to be considered alive. A
        // dependency being down does not make this process restartable, so such checks stay out of here.
        // Not cached: it touches no dependency and is reachable only inside the pod.
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        })
            .WithMetadata(getOrHead);

        return app;
    }
}
