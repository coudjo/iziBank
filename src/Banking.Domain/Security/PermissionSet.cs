namespace Banking.Domain.Security;

public enum PermissionAction
{
    View,
    Add,
    Edit,
    Delete,
    Supervise
}

/// <summary>
/// Effective permissions of one staff member in one branch (addendum 2.2): the union of the rows of every
/// approved, active role group they hold there. A module-level row (no sub-module) covers all its sub-modules.
/// There are no deny rows.
/// </summary>
public sealed class PermissionSet
{
    public static readonly PermissionSet Empty = new([]);

    private readonly PermissionGrant[] _rows;

    public PermissionSet(IEnumerable<PermissionGrant> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows.ToArray();
    }

    public IReadOnlyList<PermissionGrant> Rows => _rows;

    public bool Allows(string moduleCode, string? subModuleCode, PermissionAction action)
    {
        ArgumentNullException.ThrowIfNull(moduleCode);

        return _rows.Any(row =>
            string.Equals(row.ModuleCode, moduleCode, StringComparison.Ordinal)
            && (row.SubModuleCode is null || string.Equals(row.SubModuleCode, subModuleCode, StringComparison.Ordinal))
            && Has(row, action));
    }

    /// <summary>One row per module or sub-module, flags OR-ed across groups. Used by <c>/auth/me</c>.</summary>
    public IReadOnlyList<PermissionGrant> Merged() =>
        _rows
            .GroupBy(static row => (row.ModuleCode, row.SubModuleCode))
            .Select(static group => new PermissionGrant(
                group.Key.ModuleCode,
                group.Key.SubModuleCode,
                group.Any(static row => row.AllowView),
                group.Any(static row => row.AllowAdd),
                group.Any(static row => row.AllowEdit),
                group.Any(static row => row.AllowDelete),
                group.Any(static row => row.AllowSupervise)))
            .OrderBy(static row => row.ModuleCode, StringComparer.Ordinal)
            .ThenBy(static row => row.SubModuleCode ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

    private static bool Has(PermissionGrant row, PermissionAction action) => action switch
    {
        PermissionAction.View => row.AllowView,
        PermissionAction.Add => row.AllowAdd,
        PermissionAction.Edit => row.AllowEdit,
        PermissionAction.Delete => row.AllowDelete,
        PermissionAction.Supervise => row.AllowSupervise,
        _ => false
    };
}
