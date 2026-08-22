using Abs.DBCC.SharedKernel;

namespace Abs.DBCC.Domain.Collation;

public sealed record SqlCollationName
{
    public string Value { get; }

    public SqlCollationName(string value)
    {
        Value = Guard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public override string ToString() => Value;
}
