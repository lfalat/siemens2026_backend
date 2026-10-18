namespace RecruitmentModule.Domain;

/// <summary>Names that identify the module.</summary>
public static class ModuleTypes
{
    /// <summary>The module's schema and route-group name.</summary>
    public const string ModuleName = "recruitment";
}

/// <summary>The feature event names the module's commands carry (<c>ICommand.EventName</c>).</summary>
public static class ModuleEvents
{
    /// <summary>An administrator created a staff account in Entra.</summary>
    public const string StaffUserCreated = "staff_user_created";
}
