using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace core.Application.Identity.TenancyOrg.Platforms.Commands.CreatePlatform;

public sealed record CreatePlatformCommand(string Code, string Name, string? LegalName, string? Domain, Guid? ActorId) : ICommand<PlatformDto>;

public class CreatePlatformCommandHandler : ICommandHandler<CreatePlatformCommand, PlatformDto>
{
    private readonly ICoreDbContext _db;

    public CreatePlatformCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<PlatformDto> HandleAsync(CreatePlatformCommand command, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var p = new Platform
        {
            Id = Guid.NewGuid(), Code = command.Code, Name = command.Name, LegalName = command.LegalName,
            Domain = command.Domain, Config = "{}", Status = "active",
            CreatedAt = now, UpdatedAt = now, Version = 1, CreatedBy = command.ActorId
        };
        _db.Platforms.Add(p);
        await _db.SaveChangesAsync(cancellationToken);
        return new PlatformDto(p.Id, p.Code, p.Name, p.LegalName, p.Status, p.CreatedAt);
    }
}
