using System.Net.Sockets;
using System.Text;

var builder = DistributedApplication.CreateBuilder(args);

// ── Database (ADR-006, ADR-A005) ─────────────────────────────────────────────
// One database with a schema per module (ADR-A001). Port and credentials are fixed so a local client
// (psql, DBeaver, Rider) connects with the same values on every run; they are development-only values.
// No data volume: every run starts from configs/postgres/init/01-create-db.sql, which the container executes
// once against POSTGRES_DB — the same database AddDatabase names below, so the schema lands where the API connects.
var postgresUser = builder.AddParameter("postgres-username", "healthyhires");
var postgresPassword = builder.AddParameter("postgres-password", "healthyhires-dev", secret: true);

var postgres = builder.AddPostgres("postgres", postgresUser, postgresPassword, port: 5432)
    .WithImageTag("18")
    .WithEnvironment("POSTGRES_DB", "healthyhires")
    .WithInitFiles("../configs/postgres/init");

var database = postgres.AddDatabase("healthyhires");

// ── Blob Storage (ADR-005, ADR-A010) ─────────────────────────────────────────
// Azurite, the Azure Storage emulator, so the API uses the same Azure.Storage.Blobs SDK locally as in
// production. Blob endpoint on the emulator's standard port.
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite => azurite.WithBlobPort(10000));

var blobs = storage.AddBlobs("blobs");

// ── E-mail (ADR-010) ─────────────────────────────────────────────────────────
// Production sends through ACS Email; locally every message is caught by Mailpit and shown in its web
// inbox instead of being delivered.
var mailpit = builder.AddMailPit("mailpit", httpPort: 8025, smtpPort: 1025);

// ── Malware scanning (ADR-005, ADR-A010) ─────────────────────────────────────
// The same clamd the VM runs (container-deployment.md §1.4). The signature database lives in a named
// volume, unlike the other resources' data: a fresh container downloads several hundred MB of signatures,
// and ClamAV's mirror rate-limits clients that download them on every start. Loading them into memory
// still takes up to a minute after each start, which the health check below waits out.
const int clamavPort = 3310;

builder.Services.AddHealthChecks()
    .AddAsyncCheck("clamav-ping", ct => PingClamdAsync(clamavPort, ct));

var clamav = builder.AddContainer("clamav", "clamav/clamav", "1.5.4")
    .WithEndpoint(port: clamavPort, targetPort: 3310, scheme: "tcp", name: "tcp")
    .WithVolume("healthyhires-clamav-db", "/var/lib/clamav")
    .WithHealthCheck("clamav-ping");

// ── API ──────────────────────────────────────────────────────────────────────
var apiService = builder.AddProject<Projects.ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(database)
    .WithReference(blobs)
    .WithReference(mailpit)
    // services__clamav__tcp__0, resolved through service discovery. No WaitFor: as on the VM, the API runs
    // without ClamAV, and uploads stay in the "scanning" state until it is ready.
    .WithReference(clamav.GetEndpoint("tcp"))
    .WaitFor(database)
    .WaitFor(blobs)
    .WaitFor(mailpit);

// Staff sign-in against the company tenant (iam.md §8). The ids are committed in appsettings.json: they identify the
// tenant and the API registration and are not secrets (iam.md §5). A user secret overrides them, e.g. to try another
// tenant:  dotnet user-secrets set Entra:TenantId <tenant-id> --project AppHost
// Without them the API still runs, and every staff endpoint answers 401.
foreach (var key in new[] { "TenantId", "ApiClientId" })
{
    if (builder.Configuration[$"Entra:{key}"] is { Length: > 0 } value)
        apiService.WithEnvironment($"Entra__{key}", value);
}

// HTTPS when the API's launch profile has it, HTTP otherwise. Asking for an endpoint that does not exist throws,
// and the dashboard then drops every URL of the resource, the API's own included.
apiService.WithUrls(context =>
{
    var ep = apiService.Resource.TryGetEndpoints(out var endpoints) && endpoints.Any(e => e.Name == "https")
        ? apiService.GetEndpoint("https")
        : apiService.GetEndpoint("http");
    context.Urls.Add(new() { Url = $"{ep.Url}/scalar", DisplayText = "Scalar" });
});

builder.Build().Run();

/// <summary>
/// Healthy once clamd answers <c>PING</c> with <c>PONG</c>, i.e. its signatures are loaded and it accepts scans.
/// The container's port opens earlier than that, so a plain TCP check would report it ready too soon.
/// </summary>
static async Task<HealthCheckResult> PingClamdAsync(int port, CancellationToken cancellationToken)
{
    try
    {
        using TcpClient client = new();
        await client.ConnectAsync("localhost", port, cancellationToken);
        await using NetworkStream stream = client.GetStream();

        // The z-prefixed form is NUL-terminated, so the reply is a single read without newline parsing.
        await stream.WriteAsync("zPING\0"u8.ToArray(), cancellationToken);

        byte[] buffer = new byte[16];
        int read = await stream.ReadAsync(buffer, cancellationToken);
        string reply = Encoding.ASCII.GetString(buffer, 0, read).TrimEnd('\0');

        return reply == "PONG"
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"clamd replied '{reply}'.");
    }
    catch (Exception ex) when (ex is SocketException or IOException)
    {
        return HealthCheckResult.Unhealthy("clamd is not accepting connections yet.", ex);
    }
}
