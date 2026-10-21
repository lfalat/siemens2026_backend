using System.Net.Http.Json;
using System.Text.Json;
using ApiService.Infrastructure.ExceptionHandlers;
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

/// <summary>
/// <c>POST /job-posts</c> for hiring managers (and administrators) only; <c>GET /job-posts</c> and
/// <c>GET /job-posts/{id}</c> for anyone, signed in or not.
/// </summary>
public class JobPostEndpointTests
{
    private static readonly StaffMember Recruiter = new(Guid.NewGuid(), "Jana Nováková", [StaffRoles.Recruiter]);

    private static readonly object CreateBody = new
    {
        title = "Elektrotechnik údržby",
        location = "Bratislava",
        department = "Údržba",
        description = "Údržba a opravy elektrických zariadení vo výrobnej hale.",
        openPositions = 2,
        salary = 9.5,
        paymentType = PaymentTypes.Hourly,
        employmentType = EmploymentTypes.StudentWorkAgreement,
        recruiterId = Recruiter.Id,
    };

    private static readonly object PublishedBody = new
    {
        title = "Elektrotechnik údržby",
        location = "Bratislava",
        department = "Údržba",
        description = "Údržba a opravy elektrických zariadení vo výrobnej hale.",
        isPublished = true,
        openPositions = 2,
        salary = 9.5,
        paymentType = PaymentTypes.Hourly,
        employmentType = EmploymentTypes.StudentWorkAgreement,
        recruiterId = Recruiter.Id,
    };

    private static async Task<WebApplication> StartHostAsync(CancellationToken ct)
    {
        string connectionString = TestDb.NewDatabase();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTestStaffAuthentication();
        builder.Services.AddValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AppExceptionHandler>();
        builder.Services.AddDbContext<RecruitmentDbContext>(o => o.UseNpgsql(connectionString));
        builder.Services.AddCqrsHandlers(typeof(CreateJobPostHandler).Assembly);
        builder.Services.AddSingleton<IStaffDirectory>(new FakeStaffDirectory { Staff = { Recruiter } });

        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("").MapEndpoints(typeof(CreateJobPostHandler).Assembly);

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(ct);
        return app;
    }

    private static HttpClient As(WebApplication app, string role) =>
        StaffTokens.Client(app, StaffTokens.Token(StaffTokens.Claims(role)));

    [Theory]
    [InlineData(StaffRoles.HiringManager)]
    [InlineData(StaffRoles.Admin)]
    public async Task A_hiring_manager_creates_a_draft(string role)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = As(app, role);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/job-posts", CreateBody, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.False(created.GetProperty("isPublished").GetBoolean());
        Assert.Equal(Recruiter.Id, created.GetProperty("recruiterId").GetGuid());
        Assert.Equal(9.5m, created.GetProperty("salary").GetDecimal());
        Assert.Equal(EmploymentTypes.StudentWorkAgreement, created.GetProperty("employmentType").GetString());

        using IServiceScope scope = app.Services.CreateScope();
        RecruitmentDbContext db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.Equal(Guid.Parse(StaffTokens.ObjectId), (await db.JobPosts.SingleAsync(ct)).CreatedBy);
    }

    [Fact]
    public async Task A_recruiter_cannot_create_a_job_post()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = As(app, StaffRoles.Recruiter);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/job-posts", CreateBody, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_is_401()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient client = StaffTokens.Client(app);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/job-posts", CreateBody, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anyone_lists_and_reads_the_published_posts()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using (HttpClient manager = As(app, StaffRoles.HiringManager))
        {
            using HttpResponseMessage created = await manager.PostAsJsonAsync("/job-posts", PublishedBody, ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        using HttpClient anonymous = StaffTokens.Client(app);

        using HttpResponseMessage list = await anonymous.GetAsync("/job-posts?offset=0&limit=10", ct);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        JsonElement page = JsonDocument.Parse(await list.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        string id = page.GetProperty("items")[0].GetProperty("id").GetString()!;

        using HttpResponseMessage detail = await anonymous.GetAsync($"/job-posts/{id}", ct);

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        JsonElement post = JsonDocument.Parse(await detail.Content.ReadAsStringAsync(ct)).RootElement;
        Assert.False(post.TryGetProperty("recruiterId", out _));
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("offset=-1")]
    [InlineData("employmentType=TPP")]
    public async Task Out_of_range_paging_or_an_unknown_type_is_400(string queryString)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient anonymous = StaffTokens.Client(app);

        using HttpResponseMessage response = await anonymous.GetAsync($"/job-posts?{queryString}", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_post_is_404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using WebApplication app = await StartHostAsync(ct);
        using HttpClient anonymous = StaffTokens.Client(app);

        using HttpResponseMessage response = await anonymous.GetAsync($"/job-posts/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
