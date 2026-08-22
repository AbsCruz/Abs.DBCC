using Abs.DBCC.Application.Ports;
using Abs.DBCC.SharedKernel;
using FluentValidation;
using MediatR;

namespace Abs.DBCC.Application.Connections;

public sealed record TestConnectionQuery(ConnectionProfile Profile) : IRequest<Result>;

public sealed class TestConnectionQueryValidator : AbstractValidator<TestConnectionQuery>
{
    public TestConnectionQueryValidator()
    {
        RuleFor(q => q.Profile).SetValidator(new ConnectionProfileValidator());
    }
}

public sealed class TestConnectionQueryHandler(ISqlScriptRunnerFactory runnerFactory) : IRequestHandler<TestConnectionQuery, Result>
{
    public async Task<Result> Handle(TestConnectionQuery request, CancellationToken cancellationToken)
    {
        try
        {
            await using var runner = await runnerFactory.CreateAsync(request.Profile, cancellationToken);
            await runner.ExecuteScalarAsync<int>("SELECT 1", ct: cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
