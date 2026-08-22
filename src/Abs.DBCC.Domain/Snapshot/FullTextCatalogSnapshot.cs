namespace Abs.DBCC.Domain.Snapshot;

/// <summary>
/// A full-text catalog (not schema-scoped in SQL Server). Only the index built on it is
/// dropped/recreated around a collation change - the catalog container itself is never touched.
/// </summary>
public sealed record FullTextCatalogSnapshot(string Name, bool IsDefault);
