using Azure.Identity;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph;
using RecruitmentModule.Domain;
using RecruitmentModule.Infrastructure.Entra;

namespace RecruitmentModule;

/// <summary>The Recruitment module's composition root: job offers, applications, CVs, the HR workflow and its staff.</summary>
public static class RecruitmentModule
{
    /// <summary>Registers the module's handlers and services. Call before <c>AddCqrsDecorators</c>.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddRecruitmentModule(this IServiceCollection services)
    {
        services.AddCqrsHandlers(typeof(RecruitmentModule).Assembly);

        // Staff accounts live in Entra (iam.md §2.4). Like sign-in, the settings are required outside Development.
        services.AddOptions<StaffDirectoryOptions>()
            .BindConfiguration(StaffDirectoryOptions.SectionName)
            .Validate<IHostEnvironment>(static (options, environment) => environment.IsDevelopment() || options.IsConfigured,
                $"{StaffDirectoryOptions.SectionName}:ApiClientId and {StaffDirectoryOptions.SectionName}:UserDomain must be set outside Development.")
            .ValidateOnStart();

        // The VM's managed identity in production, the developer's Azure CLI sign-in locally (iam.md §4, §8).
        services.AddSingleton(static _ => new GraphServiceClient(new DefaultAzureCredential()));
        services.AddScoped<IStaffDirectory, GraphStaffDirectory>();

        return services;
    }

    /// <summary>Maps the module's endpoints under <c>/recruitment</c> on the versioned API group.</summary>
    /// <param name="api">The versioned API group.</param>
    /// <returns>The same <paramref name="api"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapRecruitmentModule(this IEndpointRouteBuilder api)
    {
        api.MapGroup($"/{ModuleTypes.ModuleName}")
            .WithTags(ModuleTypes.ModuleName)
            .MapEndpoints(typeof(RecruitmentModule).Assembly);
        return api;
    }
}
