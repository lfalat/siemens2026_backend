using RecruitmentModule.Infrastructure.Entra;

namespace Tests.RecruitmentModule.Infrastructure.Entra;

/// <summary>An in-memory Entra tenant that records what was asked of it, and fails where a test says so.</summary>
internal sealed class FakeStaffDirectory : IStaffDirectory
{
    public const string Domain = "example.com";

    public Guid NextId { get; } = Guid.NewGuid();
    public HashSet<string> Usernames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string DisplayName, string Username, string Password)> Created { get; } = [];
    public List<(Guid UserId, string Role)> Assigned { get; } = [];
    public List<Guid> Deleted { get; } = [];
    public List<StaffMember> Staff { get; } = [];
    public Exception? AssignFailure { get; set; }
    public Exception? DeleteFailure { get; set; }

    public Task<bool> UserExistsAsync(string username, CancellationToken ct) =>
        Task.FromResult(Usernames.Contains(username));

    public Task<StaffAccount> CreateUserAsync(string displayName, string username, string password, CancellationToken ct)
    {
        Created.Add((displayName, username, password));
        Usernames.Add(username);
        return Task.FromResult(new StaffAccount(NextId, $"{username}@{Domain}"));
    }

    public Task AssignAppRoleAsync(Guid userId, string role, CancellationToken ct)
    {
        if (AssignFailure is not null)
            throw AssignFailure;
        Assigned.Add((userId, role));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StaffMember>> ListStaffAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StaffMember>>(Staff);

    public Task DeleteUserAsync(Guid userId, CancellationToken ct)
    {
        if (DeleteFailure is not null)
            throw DeleteFailure;
        Deleted.Add(userId);
        return Task.CompletedTask;
    }
}
