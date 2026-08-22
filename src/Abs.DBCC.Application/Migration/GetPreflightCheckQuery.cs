using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;
using MediatR;

namespace Abs.DBCC.Application.Migration;

public sealed record GetPreflightCheckQuery(ConnectionProfile Profile, MigrationPlan Plan) : IRequest<PreflightCheckResult>;

public sealed class GetPreflightCheckQueryHandler(IPreflightCheckService preflightCheckService)
    : IRequestHandler<GetPreflightCheckQuery, PreflightCheckResult>
{
    public Task<PreflightCheckResult> Handle(GetPreflightCheckQuery request, CancellationToken cancellationToken) =>
        preflightCheckService.CheckAsync(request.Profile, request.Plan, cancellationToken);
}
