using System.Net.Http.Json;
using System.Text.Json;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecruitmentModule.Domain;
using RecruitmentModule.Features.JobPostFeatures.CreateJobPost;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using Tests.ApiService.Infrastructure.Auth;
using Tests.RecruitmentModule.Infrastructure.Entra;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.JobPostFeatures;

/// <summary><c>POST /job-posts</c> for any staff member, <c>PUT /job-posts/{id}/recruiter</c> for hiring managers.</summary>
public class JobPostEndpointTests
{
    private static readonly object CreateBody = new
    {
        title = "Elektrotechnik údržby",
        location = "Bratislava",
        department = "Údržba",
        openPositions = 2,
        paymentType = PaymentTypes.Monthly,
        employmentType = EmploymentTypes.StudentWorkAgreement,
    };

    private static readonly StaffMember Recruiter = new(Guid.NewGuid(), "Jana Nováková", [StaffRoles.Recruiter]);

    private static async Task<WebApplication> StartHostAsync(CancellationToken ct)
    {
        string connectionString = TestDb.NewDatabase();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();
        builder.Services.AddValidation();
        builder.Services.AddDbContext<RecruitmentDbContext>(o => o.UseNpgsql(connectionString));
        builder.Services.AddCqrsHandlers(typeof(CreateJobPostHandler).Assembly);
        builder.Services.AddSingleton<IStaffDirectory>(new FakeStaffDirectory { Staff = { Recruiter } });

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(CreateJobPostHandler).Assembly);

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
        return app;
    }

    private static HttpClient As(WebApplication app, string role) =>
        StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(role)));

    private static async Task<JsonElement> CreateAsync(WebApplication app, CancellationToken ct)
    {
        using HttpClient client = As(app, StaffRoles.Recruiter);
        using HttpResponseMessage response = await client.PostAsJsonAsync("/job-posts", CreateBody, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
    }

    [Theory]
    [InlineData(StaffRoles.Recruiter)]
    [InlineData(StaffRoles.HiringManager)]
    [InlineData(StaffRoles.Admin)]
    public async Task Any_staff_member_creates_a_draft(string role)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = As(app, role);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/job-posts", CreateBody, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.False(created.GetProperty("isPublished").GetBoolean());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("recruiterId").ValueKind);
        Assert.Equal(EmploymentTypes.StudentWorkAgreement, created.GetProperty("employmentType").GetString());

        using IServiceScope scope = app.Services.CreateScope();
        RecruitmentDbContext db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.Equal(Guid.Parse(StaffTokens.ObjectId), (await db.JobPosts.SingleAsync(ct)).CreatedBy);
    }

    [Fact]
    public async Task Creating_without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/job-posts", CreateBody, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(StaffRoles.HiringManager)]
    [InlineData(StaffRoles.Admin)]
    public async Task A_hiring_manager_assigns_the_recruiter(string role)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        string id = (await CreateAsync(app, ct)).GetProperty("id").GetString()!;
        using HttpClient client = As(app, role);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/job-posts/{id}/recruiter", new { recruiterId = Recruiter.Id, version = 1 }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement assigned = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal(Recruiter.Id, assigned.GetProperty("recruiterId").GetGuid());
        Assert.Equal(2, assigned.GetProperty("version").GetInt16());
    }

    [Fact]
    public async Task A_recruiter_cannot_assign_the_recruiter()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        string id = (await CreateAsync(app, ct)).GetProperty("id").GetString()!;
        using HttpClient client = As(app, StaffRoles.Recruiter);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/job-posts/{id}/recruiter", new { recruiterId = Recruiter.Id, version = 1 }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
