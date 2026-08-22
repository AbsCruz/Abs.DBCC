namespace Abs.DBCC.Domain.Migration;

public enum MigrationStepKind
{
    DropIndex,
    DropForeignKey,
    DropCheckConstraint,
    DropDefaultConstraint,
    DropComputedColumn,
    AlterColumnCollation,
    AddComputedColumn,
    AddDefaultConstraint,
    AddCheckConstraint,
    CreateIndex,
    AddForeignKey,
    DropSchemaBoundObject,
    AddSchemaBoundObject,
    DropFullTextIndex,
    AddFullTextIndex,
    GrantPermission,
    AddExtendedProperty,
    AlterDatabaseCollation
}
