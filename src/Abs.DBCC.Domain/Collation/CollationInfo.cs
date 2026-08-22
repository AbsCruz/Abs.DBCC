namespace Abs.DBCC.Domain.Collation;

/// <summary>One row of sys.fn_helpcollations() – a collation installed on the SQL Server instance.</summary>
public sealed record CollationInfo(string Name, string Description);
