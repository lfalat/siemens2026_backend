using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Kernel.Errors;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RecruitmentModule.Domain;

namespace RecruitmentModule.Features.StaffManagementFeatures.CreateUser;

/// <summary><c>POST /api/v1/recruitment/staff-users</c>: an administrator creates a recruiter's Entra account.</summary>
internal sealed class CreateUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/staff-users")
            .RequireStaff(StaffPolicies.Admin)
            .MapPost("", static async (
                CreateUserRequest request,
                ICommandHandler<CreateUserCommand, CreateUserResponse> handler,
                CancellationToken ct) =>
            {
                CreateUserCommand command = new() // manual map (ADR-A012)
                {
                    DisplayName = request.DisplayName,
                    Username = request.Username,
                };
                CreateUserResponse result = await handler.HandleAsync(command, ct);

                // No Location: staff accounts are read in Entra, not through this API.
                return Results.Created((string?)null, result);
            })
            .WithName("CreateStaffUser")
            // The response carries the temporary password, which must not be kept for replay in kernel.idempotency.
            // The username makes the call at-most-once anyway: a repeat answers 409.
            .DisableIdempotency()
            .Produces<CreateUserResponse>(StatusCodes.Status201Created)
            .ProducesErrors(RecruitmentErrors.UsernameTaken, CommonErrors.UpstreamFailed, CommonErrors.UpstreamUnavailable);
}
