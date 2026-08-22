using Abs.DBCC.Domain.Common;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

public static class DefaultConstraintScriptGenerator
{
    public static string GenerateDrop(ObjectRef table, DefaultConstraintSnapshot constraint) =>
        $"ALTER TABLE {table.Identifier.Quoted} DROP CONSTRAINT {SqlIdentifier.QuotePart(constraint.Name)};";

    /// <summary>sys.default_constraints.definition already comes fully parenthesized, e.g. "((0))".</summary>
    public static string GenerateCreate(ObjectRef table, DefaultConstraintSnapshot constraint) =>
        $"ALTER TABLE {table.Identifier.Quoted} ADD CONSTRAINT {SqlIdentifier.QuotePart(constraint.Name)} " +
        $"DEFAULT {constraint.Definition} FOR {SqlIdentifier.QuotePart(constraint.ColumnName)};";
}
