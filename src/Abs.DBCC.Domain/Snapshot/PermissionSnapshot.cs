namespace Abs.DBCC.Domain.Snapshot;

/// <summary>
/// A GRANT/DENY on the database itself (class 0, everything null), on a schema (class 3, only
/// <see cref="OnSchema"/> set), or on an object/column (class 1, <see cref="OnObject"/> set, optionally
/// <see cref="OnColumn"/>). State is sys.database_permissions.state_desc: "GRANT", "DENY" or
/// "GRANT_WITH_GRANT_OPTION".
/// </summary>
public sealed record PermissionSnapshot(
    string GranteePrincipal,
    string PermissionName,
    string State,
    ObjectRef? OnObject,
    string? OnColumn,
    string? OnSchema = null);
