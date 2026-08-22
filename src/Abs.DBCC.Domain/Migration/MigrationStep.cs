namespace Abs.DBCC.Domain.Migration;

public sealed record MigrationStep(int Order, MigrationStepKind Kind, string Description, string Sql);
