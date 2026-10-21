using System.ComponentModel.DataAnnotations;
using System.Reflection;
using ApiService.OpenAPI;
using Kernel.CQRS;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Tests.ApiService.OpenAPI;

/// <summary>
/// Tests that <see cref="ProblemResponseTransformer"/> declares the problem responses the host answers by rule, so the
/// document lists every status the API can return (ADR-009).
/// </summary>
public class ProblemResponseTransformerTests
{
    private sealed class SampleCommand : ICommand
    {
        public static string EventName => "sample";

        [Required]
        public string? Name { get; init; }
    }

    /// <summary>Stands in for an endpoint delegate: what the transformer reads the dispatched command from.</summary>
    private static Task Handler(ICommandHandler<SampleCommand> handler) => Task.CompletedTask;

    private static MethodInfo HandlerMethod =>
        typeof(ProblemResponseTransformerTests).GetMethod(nameof(Handler), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static async Task<OpenApiOperation> TransformAsync(
        string method, OpenApiOperation? operation = null, params object[] endpointMetadata)
    {
        operation ??= new OpenApiOperation();

        await new ProblemResponseTransformer().TransformAsync(operation, new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            Description = new ApiDescription
            {
                HttpMethod = method,
                ActionDescriptor = new ActionDescriptor { EndpointMetadata = [.. endpointMetadata] },
            },
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);

        return operation;
    }

    private static string[] Statuses(OpenApiOperation operation) =>
        operation.Responses is null ? [] : [.. operation.Responses.Keys.Order(StringComparer.Ordinal)];

    [Fact]
    public async Task A_read_declares_nothing() =>
        Assert.Empty(Statuses(await TransformAsync("GET")));

    [Fact]
    public async Task A_write_declares_the_concurrency_409() =>
        Assert.Equal(["409"], Statuses(await TransformAsync("POST")));

    [Fact]
    public async Task A_query_exposed_as_post_does_not() =>
        Assert.Empty(Statuses(await TransformAsync("POST", null, ReadOnlyEndpoint.Instance, IdempotencyDisabled.Instance)));

    [Fact]
    public async Task An_authorization_policy_declares_the_403() =>
        Assert.Equal(["403"], Statuses(await TransformAsync("GET", null, new AuthorizeAttribute("Admin"))));

    [Fact]
    public async Task A_dispatched_command_with_rules_declares_the_400() =>
        Assert.Equal(["400"], Statuses(await TransformAsync("GET", null, HandlerMethod)));

    [Fact]
    public async Task An_endpoints_own_response_is_kept()
    {
        OpenApiOperation operation = new()
        {
            Responses = new OpenApiResponses { ["409"] = new OpenApiResponse { Description = "Username taken" } },
        };

        await TransformAsync("POST", operation);

        Assert.Equal("Username taken", operation.Responses["409"].Description);
    }
}
