using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using FluentValidation;
using MediatR;

namespace Abs.DBCC.Application.Collations;

public sealed record GetAvailableCollationsQuery(ConnectionProfile Profile) : IRequest<IReadOnlyList<CollationInfo>>;

public sealed class GetAvailableCollationsQueryValidator : AbstractValidator<GetAvailableCollationsQuery>
{
    public GetAvailableCollationsQueryValidator()
    {
        RuleFor(q => q.Profile).SetValidator(new ConnectionProfileValidator());
    }
}

public sealed class GetAvailableCollationsQueryHandler(ICollationCatalogService catalogService)
    : IRequestHandler<GetAvailableCollationsQuery, IReadOnlyList<CollationInfo>>
{
    public Task<IReadOnlyList<CollationInfo>> Handle(GetAvailableCollationsQuery request, CancellationToken cancellationToken) =>
        catalogService.GetAvailableCollationsAsync(request.Profile, cancellationToken);
}
