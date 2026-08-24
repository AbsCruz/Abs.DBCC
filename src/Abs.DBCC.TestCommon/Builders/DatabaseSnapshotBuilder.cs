using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.TestCommon.Builders;

public sealed class DatabaseSnapshotBuilder
{
    private readonly List<TableSnapshot> _tables = [];
    private readonly List<ForeignKeySnapshot> _foreignKeys = [];
    private readonly List<ObjectDefinition> _programmableObjects = [];
    private readonly List<SchemaBoundDependency> _schemaBoundDependencies = [];
    private readonly List<SchemaBoundObjectReference> _schemaBoundObjectReferences = [];
    private readonly List<SequenceSnapshot> _sequences = [];
    private readonly List<SynonymSnapshot> _synonyms = [];
    private readonly List<FullTextCatalogSnapshot> _fullTextCatalogs = [];
    private readonly List<FullTextIndexSnapshot> _fullTextIndexes = [];
    private readonly List<PermissionSnapshot> _permissions = [];
    private readonly List<ExtendedPropertySnapshot> _extendedProperties = [];
    private readonly List<ViewIndexSnapshot> _viewIndexes = [];
    private SqlCollationName _databaseCollation = new("SQL_Latin1_General_CP1_CI_AS");

    public DatabaseSnapshotBuilder WithDatabaseCollation(string collation)
    {
        _databaseCollation = new SqlCollationName(collation);
        return this;
    }

    public DatabaseSnapshotBuilder WithTable(TableSnapshot table)
    {
        _tables.Add(table);
        return this;
    }

    public DatabaseSnapshotBuilder WithForeignKey(
        string name, ObjectRef parentTable, ObjectRef referencedTable,
        IReadOnlyList<(string ParentColumn, string ReferencedColumn)> columns,
        string deleteAction = "NO_ACTION", string updateAction = "NO_ACTION")
    {
        _foreignKeys.Add(new ForeignKeySnapshot(
            name, parentTable, referencedTable,
            columns.Select(c => new ForeignKeyColumnSnapshot(c.ParentColumn, c.ReferencedColumn)).ToList(),
            deleteAction, updateAction, false));
        return this;
    }

    public DatabaseSnapshotBuilder WithView(string schema, string name, string definitionScript, bool isSchemaBound = false)
    {
        _programmableObjects.Add(new ObjectDefinition(new ObjectRef(schema, name, DatabaseObjectKind.View), definitionScript, isSchemaBound));
        return this;
    }

    public DatabaseSnapshotBuilder WithFunction(string schema, string name, string definitionScript, bool isSchemaBound = false)
    {
        _programmableObjects.Add(new ObjectDefinition(new ObjectRef(schema, name, DatabaseObjectKind.Function), definitionScript, isSchemaBound));
        return this;
    }

    public DatabaseSnapshotBuilder WithStoredProcedure(string schema, string name, string definitionScript)
    {
        _programmableObjects.Add(new ObjectDefinition(new ObjectRef(schema, name, DatabaseObjectKind.StoredProcedure), definitionScript, false));
        return this;
    }

    public DatabaseSnapshotBuilder WithTrigger(string schema, string name, string definitionScript)
    {
        _programmableObjects.Add(new ObjectDefinition(new ObjectRef(schema, name, DatabaseObjectKind.Trigger), definitionScript, false));
        return this;
    }

    public DatabaseSnapshotBuilder WithSchemaBoundDependency(ObjectRef dependentObject, ObjectRef referencedTable, string referencedColumn)
    {
        _schemaBoundDependencies.Add(new SchemaBoundDependency(dependentObject, referencedTable, referencedColumn));
        return this;
    }

    public DatabaseSnapshotBuilder WithSchemaBoundObjectReference(ObjectRef dependentObject, ObjectRef referencedObject)
    {
        _schemaBoundObjectReferences.Add(new SchemaBoundObjectReference(dependentObject, referencedObject));
        return this;
    }

    public DatabaseSnapshotBuilder WithSequence(
        string schema, string name, string dataType = "bigint", string startValue = "1", string increment = "1")
    {
        _sequences.Add(new SequenceSnapshot(
            new ObjectRef(schema, name, DatabaseObjectKind.Sequence), dataType, startValue, increment, null, null, false, 50));
        return this;
    }

    public DatabaseSnapshotBuilder WithSynonym(string schema, string name, string baseObjectName)
    {
        _synonyms.Add(new SynonymSnapshot(new ObjectRef(schema, name, DatabaseObjectKind.Synonym), baseObjectName));
        return this;
    }

    public DatabaseSnapshotBuilder WithFullTextCatalog(string name, bool isDefault = false)
    {
        _fullTextCatalogs.Add(new FullTextCatalogSnapshot(name, isDefault));
        return this;
    }

    public DatabaseSnapshotBuilder WithFullTextIndex(
        ObjectRef table, string catalogName, string keyIndexName, IReadOnlyList<string> columns, string changeTracking = "AUTO")
    {
        _fullTextIndexes.Add(new FullTextIndexSnapshot(
            table, catalogName, keyIndexName, changeTracking,
            columns.Select(c => new FullTextIndexColumnSnapshot(c, 1033)).ToList()));
        return this;
    }

    public DatabaseSnapshotBuilder WithDatabasePermission(string granteePrincipal, string permissionName, string state = "GRANT")
    {
        _permissions.Add(new PermissionSnapshot(granteePrincipal, permissionName, state, null, null));
        return this;
    }

    public DatabaseSnapshotBuilder WithObjectPermission(
        string granteePrincipal, string permissionName, ObjectRef onObject, string? onColumn = null, string state = "GRANT")
    {
        _permissions.Add(new PermissionSnapshot(granteePrincipal, permissionName, state, onObject, onColumn));
        return this;
    }

    public DatabaseSnapshotBuilder WithSchemaPermission(string granteePrincipal, string permissionName, string schemaName, string state = "GRANT")
    {
        _permissions.Add(new PermissionSnapshot(granteePrincipal, permissionName, state, null, null, schemaName));
        return this;
    }

    public DatabaseSnapshotBuilder WithExtendedProperty(
        ObjectRef obj, string? columnName, string propertyName, string propertyValue, ObjectRef? parentTable = null)
    {
        _extendedProperties.Add(new ExtendedPropertySnapshot(obj, columnName, propertyName, propertyValue, parentTable));
        return this;
    }

    public DatabaseSnapshotBuilder WithViewIndex(ObjectRef view, IndexSnapshot index)
    {
        _viewIndexes.Add(new ViewIndexSnapshot(view, index));
        return this;
    }

    public DatabaseSnapshot Build() => new(
        _databaseCollation, _tables, _foreignKeys, _programmableObjects, _schemaBoundDependencies, _schemaBoundObjectReferences,
        _sequences, _synonyms, _fullTextCatalogs, _fullTextIndexes, _permissions, _extendedProperties, _viewIndexes);
}
