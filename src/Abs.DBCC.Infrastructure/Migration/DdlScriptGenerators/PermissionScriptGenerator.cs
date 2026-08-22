using Abs.DBCC.Domain.Common;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

public static class PermissionScriptGenerator
{
    public static string Generate(PermissionSnapshot permission)
    {
        var verb = permission.State switch
        {
            "GRANT" or "GRANT_WITH_GRANT_OPTION" => "GRANT",
            "DENY" => "DENY",
            _ => throw new ArgumentOutOfRangeException(nameof(permission), permission.State, "Unexpected permission state.")
        };

        var target = permission.OnSchema is not null
            ? $" ON SCHEMA::{SqlIdentifier.QuotePart(permission.OnSchema)}"
            : permission.OnObject is null
                ? string.Empty
                : $" ON {permission.OnObject.Identifier.Quoted}" +
                  (permission.OnColumn is null ? string.Empty : $"({SqlIdentifier.QuotePart(permission.OnColumn)})");

        var withGrantOption = permission.State == "GRANT_WITH_GRANT_OPTION" ? " WITH GRANT OPTION" : string.Empty;

        return $"{verb} {permission.PermissionName}{target} TO {SqlIdentifier.QuotePart(permission.GranteePrincipal)}{withGrantOption};";
    }
}
