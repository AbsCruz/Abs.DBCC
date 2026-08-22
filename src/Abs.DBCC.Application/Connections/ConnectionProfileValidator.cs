using FluentValidation;

namespace Abs.DBCC.Application.Connections;

public sealed class ConnectionProfileValidator : AbstractValidator<ConnectionProfile>
{
    public ConnectionProfileValidator()
    {
        RuleFor(p => p.Server).NotEmpty();
        RuleFor(p => p.Database).NotEmpty();
        RuleFor(p => p.User).NotEmpty();
        RuleFor(p => p.Password).NotEmpty();
    }
}
