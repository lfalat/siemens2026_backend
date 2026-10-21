using Kernel.Auth;
using RecruitmentModule.Features.StaffManagementFeatures.ListStaffUsers;
using RecruitmentModule.Infrastructure.Entra;
using Tests.RecruitmentModule.Infrastructure.Entra;

namespace Tests.RecruitmentModule.Features.StaffManagementFeatures.ListStaffUsers;

/// <summary>Listing the staff: everyone or one role, by name.</summary>
public class ListStaffUsersHandlerTests
{
    private static readonly StaffMember Zuzana = new(Guid.NewGuid(), "Zuzana Mrázová", [StaffRoles.Recruiter]);
    private static readonly StaffMember Cyril = new(Guid.NewGuid(), "Čestmír Novák", [StaffRoles.HiringManager]);
    private static readonly StaffMember Adam = new(Guid.NewGuid(), "adam Bielik", [StaffRoles.Admin, StaffRoles.Recruiter]);

    private static ListStaffUsersHandler Handler() => new(new FakeStaffDirectory { Staff = { Zuzana, Cyril, Adam } });

    [Fact]
    public async Task Without_a_role_lists_everyone_by_name()
    {
        ListStaffUsersResponse response = await Handler().HandleAsync(new ListStaffUsersQuery(), TestContext.Current.CancellationToken);

        Assert.Equal([Adam.Id, Cyril.Id, Zuzana.Id], response.Users.Select(u => u.Id));
    }

    [Fact]
    public async Task A_role_lists_only_its_holders()
    {
        ListStaffUsersResponse response = await Handler().HandleAsync(
            new ListStaffUsersQuery { Role = StaffRoles.Recruiter }, TestContext.Current.CancellationToken);

        Assert.Equal([Adam.Id, Zuzana.Id], response.Users.Select(u => u.Id));
        Assert.Equal(["Admin", "Recruiter"], response.Users[0].Roles);
    }
}
