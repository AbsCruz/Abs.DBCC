using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Inspection;
using FluentValidation;
using MediatR;

namespace Abs.DBCC.Application.Collations;

public sealed record GetDatabaseCollationReportQuery(ConnectionProfile Profile) : IRequest<DatabaseCollationReport>;

public sealed class GetDatabaseCollationReportQueryValidator : AbstractValidator<GetDatabaseCollationReportQuery>
{
    public GetDatabaseCollationReportQueryValidator()
    {
        RuleFor(q => q.Profile).SetValidator(new ConnectionProfileValidator());
    }
}

public sealed class GetDatabaseCollationReportQueryHandler(IDatabaseInspectionService inspectionService)
    : IRequestHandler<GetDatabaseCollationReportQuery, DatabaseCollationReport>
{
    public Task<DatabaseCollationReport> Handle(GetDatabaseCollationReportQuery request, CancellationToken cancellationToken) =>
        inspectionService.BuildCollationReportAsync(request.Profile, cancellationToken);
}
