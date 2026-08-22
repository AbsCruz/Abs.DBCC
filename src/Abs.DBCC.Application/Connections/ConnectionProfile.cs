namespace Abs.DBCC.Application.Connections;

public sealed record ConnectionProfile(
    string Server,
    string Database,
    string User,
    string Password,
    bool TrustServerCertificate = false,
    bool Encrypt = true);
