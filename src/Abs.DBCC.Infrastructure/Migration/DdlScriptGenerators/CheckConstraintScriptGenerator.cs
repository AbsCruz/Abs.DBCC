using Abs.DBCC.Domain.Common;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

public static class CheckConstraintScriptGenerator
{
    public static string GenerateDrop(ObjectRef table, CheckConstraintSnapshot constraint) =>
        $"ALTER TABLE {table.Identifier.Quoted} DROP CONSTRAINT {SqlIdentifier.QuotePart(constraint.Name)};";

    /// <summary>sys.check_constraints.definition already comes fully parenthesized, e.g. "([Age]&gt;(0))".</summary>
    public static string GenerateCreate(ObjectRef table, CheckConstraintSnapshot constraint) =>
        $"ALTER TABLE {table.Identifier.Quoted} ADD CONSTRAINT {SqlIdentifier.QuotePart(constraint.Name)} CHECK {constraint.Definition};";
}
