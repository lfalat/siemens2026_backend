using Kernel.Auth;
using Kernel.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using RecruitmentModule.Domain;
using RecruitmentModule.Features.StaffManagementFeatures.CreateUser;
using RecruitmentModule.Infrastructure.Entra;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.StaffManagementFeatures.CreateUser;

/// <summary>Creating a staff account: a recruiter with a temporary password, and never one left without its role.</summary>
public class CreateUserHandlerTests
{
    private static readonly CreateUserCommand Command = new() { DisplayName = "Jana Nováková", Username = "jana.novakova" };

    private static CreateUserHandler Handler(FakeStaffDirectory directory) =>
        new(directory, NullLogger<CreateUserHandler>.Instance);

    [Fact]
    public async Task Creates_a_recruiter_with_a_temporary_password()
    {
        FakeStaffDirectory directory = new();

        CreateUserResponse response = await Handler(directory).HandleAsync(Command, TestContext.Current.CancellationToken);

        Assert.Equal(directory.NextId, response.Id);
        Assert.Equal("jana.novakova@example.com", response.UserPrincipalName);
        Assert.Equal(StaffRoles.Recruiter, response.Role);
        Assert.Equal([("Jana Nováková", "jana.novakova", response.TemporaryPassword)], directory.Created);
        Assert.Equal([(directory.NextId, StaffRoles.Recruiter)], directory.Assigned);
    }

    [Fact]
    public async Task A_taken_username_is_409_and_creates_nothing()
    {
        FakeStaffDirectory directory = new() { Usernames = { "jana.novakova" } };

        ConflictException e = await Assert.ThrowsAsync<ConflictException>(
            () => Handler(directory).HandleAsync(Command, TestContext.Current.CancellationToken));

        Assert.Equal(RecruitmentErrors.UsernameTaken, e.Code);
        Assert.Empty(directory.Created);
    }

    [Fact]
    public async Task A_failed_role_assignment_deletes_the_account_and_surfaces()
    {
        ExternalServiceException failure = new("Microsoft Graph refused to assign the app role.");
        FakeStaffDirectory directory = new() { AssignFailure = failure };

        Exception e = await Assert.ThrowsAnyAsync<Exception>(
            () => Handler(directory).HandleAsync(Command, TestContext.Current.CancellationToken));

        Assert.Same(failure, e);
        Assert.Equal([directory.NextId], directory.Deleted);
    }

    [Fact]
    public async Task A_failed_delete_does_not_hide_the_assignment_failure()
    {
        ExternalServiceException failure = new("Microsoft Graph refused to assign the app role.");
        FakeStaffDirectory directory = new()
        {
            AssignFailure = failure,
            DeleteFailure = new ServiceUnavailableException("Microsoft Graph could not be reached."),
        };

        Exception e = await Assert.ThrowsAnyAsync<Exception>(
            () => Handler(directory).HandleAsync(Command, TestContext.Current.CancellationToken));

        Assert.Same(failure, e);
    }

    [Theory]
    [InlineData("jana", true)]
    [InlineData("jana.novakova", true)]
    [InlineData("j_novak-2", true)]
    [InlineData("", false)]
    [InlineData(".jana", false)]
    [InlineData("jana.", false)]
    [InlineData("jana novakova", false)]
    [InlineData("jana@example.com", false)]
    [InlineData("jána", false)]
    public void Usernames_are_a_safe_subset_of_what_Entra_accepts(string username, bool valid)
    {
        Assert.Equal(valid, CommandValidation.IsValid(Command with { Username = username }));
    }

    [Fact]
    public void A_username_is_at_most_64_characters()
    {
        Assert.True(CommandValidation.IsValid(Command with { Username = new string('a', 64) }));
        Assert.False(CommandValidation.IsValid(Command with { Username = new string('a', 65) }));
    }

    [Fact]
    public void A_display_name_is_required()
    {
        Assert.False(CommandValidation.IsValid(Command with { DisplayName = " " }));
    }
}
