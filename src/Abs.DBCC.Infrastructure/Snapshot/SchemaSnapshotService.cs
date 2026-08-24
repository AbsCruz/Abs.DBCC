using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Snapshot.Readers;

namespace Abs.DBCC.Infrastructure.Snapshot;

public sealed class SchemaSnapshotService(
    ISqlScriptRunnerFactory runnerFactory,
    ICollationCatalogService catalogService) : ISchemaSnapshotService
{
    private readonly TableColumnReader _columnReader = new();
    private readonly IndexReader _indexReader = new();
    private readonly ConstraintReader _constraintReader = new();
    private readonly ForeignKeyReader _foreignKeyReader = new();
    private readonly ProgrammabilityReader _programmabilityReader = new();
    private readonly SchemaBoundDependencyReader _schemaBoundDependencyReader = new();
    private readonly SchemaBoundObjectReferenceReader _schemaBoundObjectReferenceReader = new();
    private readonly SequenceReader _sequenceReader = new();
    private readonly SynonymReader _synonymReader = new();
    private readonly FullTextReader _fullTextReader = new();
    private readonly PermissionReader _permissionReader = new();
    private readonly ExtendedPropertyReader _extendedPropertyReader = new();

    public async Task<DatabaseSnapshot> CaptureAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        var databaseCollation = await catalogService.GetDatabaseDefaultCollationAsync(profile, ct);

        await using var runner = await runnerFactory.CreateAsync(profile, ct);

        var columnsByTable = await _columnReader.ReadAsync(runner, ct);
        var (indexesByTable, viewIndexes) = await _indexReader.ReadAsync(runner, ct);
        var (checksByTable, defaultsByTable) = await _constraintReader.ReadAsync(runner, ct);
        var foreignKeys = await _foreignKeyReader.ReadAsync(runner, ct);
        var programmableObjects = await _programmabilityReader.ReadAsync(runner, ct);
        var schemaBoundDependencies = await _schemaBoundDependencyReader.ReadAsync(runner, ct);
        var schemaBoundObjectReferences = await _schemaBoundObjectReferenceReader.ReadAsync(runner, ct);
        var sequences = await _sequenceReader.ReadAsync(runner, ct);
        var synonyms = await _synonymReader.ReadAsync(runner, ct);
        var fullTextCatalogs = await _fullTextReader.ReadCatalogsAsync(runner, ct);
        var fullTextIndexes = await _fullTextReader.ReadIndexesAsync(runner, ct);
        var permissions = await _permissionReader.ReadAsync(runner, ct);
        var extendedProperties = await _extendedPropertyReader.ReadAsync(runner, ct);

        var tables = columnsByTable
            .Select(kv => new TableSnapshot(
                kv.Key,
                kv.Value,
                indexesByTable.GetValueOrDefault(kv.Key, []),
                checksByTable.GetValueOrDefault(kv.Key, []),
                defaultsByTable.GetValueOrDefault(kv.Key, [])))
            .ToList();

        return new DatabaseSnapshot(
            databaseCollation, tables, foreignKeys, programmableObjects, schemaBoundDependencies, schemaBoundObjectReferences,
            sequences, synonyms, fullTextCatalogs, fullTextIndexes, permissions, extendedProperties, viewIndexes);
    }
}
