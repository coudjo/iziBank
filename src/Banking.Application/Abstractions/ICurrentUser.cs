namespace Banking.Application.Abstractions;

/// <summary>
/// The staff member behind the current unit of work. All members are null for system work
/// (Migrator, background jobs), which leaves audit "By" columns NULL.
/// </summary>
public interface ICurrentUser
{
    long? StaffUserId { get; }

    int? BranchId { get; }

    long? SessionId { get; }
}

/// <summary>No signed-in user: Migrator, seeders and background jobs.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public static readonly SystemCurrentUser Instance = new();

    private SystemCurrentUser()
    {
    }

    public long? StaffUserId => null;

    public int? BranchId => null;

    public long? SessionId => null;
}
