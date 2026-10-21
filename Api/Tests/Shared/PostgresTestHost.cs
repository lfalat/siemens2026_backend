using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests.Shared;

/// <summary>
/// One Postgres container for the whole test run, on the same major version as the Aspire stack (ADR-006), holding
/// an <c>hh_template</c> database built from the real schema script (<c>configs/postgres/init/01-create-db.sql</c>).
/// <see cref="CreateDatabase"/> clones a fresh database from that template, so every test that needs the schema gets
/// full relational enforcement and its own isolated data. The container is removed by the Testcontainers reaper when
/// the run ends.
/// </summary>
/// <remarks>
/// Started on first use, so a run that touches no database starts no container. Needs Docker or Podman, like the
/// Aspire stack does.
/// </remarks>
internal sealed class PostgresTestHost
{
    /// <summary>Postgres 18, the image the AppHost runs — and the version the schema needs (<c>old</c> in <c>RETURNING</c>).</summary>
    public const string Image = "postgres:18";

    private const string TemplateDb = "hh_template";

    /// <summary>The alias Postgres answers to inside the container network, used as Toxiproxy's upstream.</summary>
    private const string NetworkAlias = "postgres";

    /// <summary>
    /// The schema script, copied next to the test binaries by Tests.csproj. Absent when Api/ is built on its own:
    /// <c>configs/postgres/init/</c> lives in the repository root, so there is nothing to copy.
    /// </summary>
    private static readonly string ScriptPath =
        Path.Combine(AppContext.BaseDirectory, "pg-init", "01-create-db.sql");

    private static readonly Lazy<PostgresTestHost> Instance =
        new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly PostgreSqlContainer _container;

    // CREATE DATABASE ... TEMPLATE requires the template to be connection-free and Postgres serializes template
    // access anyway, so run the clones one at a time.
    private readonly Lock _createLock = new();
    private int _dbCounter;

    // Only fault-injection tests need it, and it costs a container start, so it stays unbuilt otherwise.
    private readonly Lazy<ToxiproxyTestHost> _toxiproxy;

    private PostgresTestHost(PostgreSqlContainer container, INetwork network)
    {
        _container = container;
        _toxiproxy = new Lazy<ToxiproxyTestHost>(
            () => ToxiproxyTestHost.Start(network, NetworkAlias, PostgreSqlBuilder.PostgreSqlPort),
            LazyThreadSafetyMode.ExecutionAndPublication);

        // On the stock 'postgres' database, never the template: a connection held open on the template would make
        // every CreateDatabase fail.
        DataSource = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = "postgres",
            // Tests want the real constraint/column names in failure output.
            IncludeErrorDetail = true,
        }.ConnectionString);
    }

    /// <summary>The shared host, started on first use.</summary>
    public static PostgresTestHost Current => Instance.Value;

    /// <summary>
    /// A data source for tests that need a real PostgreSQL but none of the schema — binding a parameter, say. Use
    /// <see cref="CreateDatabase"/> for anything that reads or writes tables.
    /// </summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>
    /// Skips the calling test when the schema script was not copied to the output. Every schema-backed test clones
    /// the template built from that script, so without it there is nothing to run against. Call this before touching
    /// <see cref="Current"/>.
    /// </summary>
    public static void SkipIfSchemaUnavailable() =>
        Assert.SkipUnless(
            File.Exists(ScriptPath),
            "configs/postgres/init/01-create-db.sql was not copied to the test output. It lives in the repository "
            + "root; build Api/ from inside the repository to run the Postgres-backed tests.");

    private static PostgresTestHost Start()
    {
        // initdb runs everything in /docker-entrypoint-initdb.d against the configured database, exactly as the
        // AppHost's init files do.
        //
        // A network so a Toxiproxy container can reach this one by alias. The host port mapping is unaffected, so
        // ordinary direct connections still work — and stay fast.
        INetwork network = new NetworkBuilder().Build();
        network.CreateAsync().GetAwaiter().GetResult();

        PostgreSqlBuilder builder = new PostgreSqlBuilder(Image)
            .WithDatabase(TemplateDb)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithNetwork(network)
            .WithNetworkAliases(NetworkAlias);

        // Without the script the container still starts, for the tests that only use DataSource.
        if (File.Exists(ScriptPath))
            builder = builder.WithResourceMapping(new FileInfo(ScriptPath), "/docker-entrypoint-initdb.d/");

        PostgreSqlContainer container = builder.Build();
        container.StartAsync().GetAwaiter().GetResult();
        return new PostgresTestHost(container, network);
    }

    /// <summary>
    /// Creates a fresh, data-free database cloned from the schema template and returns its connection string. Each
    /// call is an isolated store.
    /// </summary>
    public string CreateDatabase()
    {
        string name = $"hh_t_{Interlocked.Increment(ref _dbCounter)}";

        lock (_createLock)
        {
            using NpgsqlConnection admin = new(AdminConnectionString());
            admin.Open();
            using NpgsqlCommand create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateDb}\"";
            create.ExecuteNonQuery();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,
            // Many throwaway databases per run — pooling would strand idle connections on each and exhaust
            // max_connections long before the run ends.
            Pooling = false,
            IncludeErrorDetail = true,
        }.ConnectionString;
    }

    /// <summary>
    /// Creates a fresh database like <see cref="CreateDatabase"/>, but routes the connection through a Toxiproxy of
    /// its own so a test can cut or slow the link mid-operation. Dispose the link when done.
    /// </summary>
    /// <remarks>
    /// A proxy per caller, not a shared one: a toxic applies to every connection through its proxy, so sharing would
    /// leak faults between tests the moment they stop running one at a time.
    /// </remarks>
    public (string ConnectionString, ToxiproxyLink Link) CreateDatabaseThroughProxy()
    {
        string direct = CreateDatabase();
        ToxiproxyLink link = _toxiproxy.Value.CreateLink().GetAwaiter().GetResult();

        return (new NpgsqlConnectionStringBuilder(direct) { Host = link.Host, Port = link.Port }.ConnectionString, link);
    }

    private string AdminConnectionString() =>
        // Admin commands connect to the stock 'postgres' database so the template stays connection-free.
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "postgres",
            Pooling = false,
        }.ConnectionString;
}
