using Abs.DBCC.Domain.Collation;

namespace Abs.DBCC.Domain.Snapshot;

public sealed record DatabaseSnapshot(
    SqlCollationName DatabaseCollation,
    IReadOnlyList<TableSnapshot> Tables,
    IReadOnlyList<ForeignKeySnapshot> ForeignKeys,
    IReadOnlyList<ObjectDefinition> ProgrammableObjects,
    IReadOnlyList<SchemaBoundDependency> SchemaBoundDependencies,
    IReadOnlyList<SequenceSnapshot> Sequences,
    IReadOnlyList<SynonymSnapshot> Synonyms,
    IReadOnlyList<FullTextCatalogSnapshot> FullTextCatalogs,
    IReadOnlyList<FullTextIndexSnapshot> FullTextIndexes,
    IReadOnlyList<PermissionSnapshot> Permissions,
    IReadOnlyList<ExtendedPropertySnapshot> ExtendedProperties,
    IReadOnlyList<ViewIndexSnapshot> ViewIndexes);
