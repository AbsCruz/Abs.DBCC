using Abs.DBCC.SharedKernel;

namespace Abs.DBCC.Domain.Common;

/// <summary>Bracket-quotes a schema-qualified SQL Server identifier, escaping embedded "]" characters.</summary>
public sealed record SqlIdentifier
{
    public string SchemaName { get; }
    public string Name { get; }

    public SqlIdentifier(string schemaName, string name)
    {
        SchemaName = Guard.AgainstNullOrWhiteSpace(schemaName, nameof(schemaName));
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name));
    }

    public string Quoted => $"{QuotePart(SchemaName)}.{QuotePart(Name)}";

    public static string QuotePart(string part) => $"[{part.Replace("]", "]]")}]";

    public override string ToString() => Quoted;
}
