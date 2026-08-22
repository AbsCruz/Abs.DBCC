namespace Abs.DBCC.Domain.Snapshot;

/// <summary>A view, stored procedure, function or trigger, captured as its complete CREATE ... statement text.</summary>
public sealed record ObjectDefinition(ObjectRef Ref, string DefinitionScript, bool IsSchemaBound);
