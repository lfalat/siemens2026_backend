using Kernel.Errors;
using Microsoft.AspNetCore.Http;

namespace RecruitmentModule.Domain;

/// <summary>The Recruitment module's error codes (ADR-A017, <c>docs/api/errors/errors.md</c>).</summary>
public static class RecruitmentErrors
{
    // ── Staff users ─────────────────────────────────────────────────────────

    /// <summary>The tenant already has an account with the requested sign-in name.</summary>
    public static readonly ErrorCode UsernameTaken = new(StatusCodes.Status409Conflict,
        "staff_user.username_taken", "An account with this username already exists in the directory.");
}
