using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.FinancialAccess;
using Domain;
using Domain.Entities.AccountingCore;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Application.Service.FinancialAccess;

public class FinancialAccessService(ApplicationDbcontext dbcontext) : IFinancialAccessService
{
    public async Task<Result> EnsurePermissionAsync(string userId, int legalEntityId, FinancialPermission requiredPermission, CancellationToken cancellationToken = default)
    {
        if (await HasFullAccountingRoleAsync(userId, cancellationToken))
            return Result.Success();

        var permissions = await dbcontext.FinancialUserAccesses
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.LegalEntityId == legalEntityId)
            .Select(x => (FinancialPermission?)x.Permissions)
            .SingleOrDefaultAsync(cancellationToken);

        return permissions is not null && (permissions.Value & requiredPermission) == requiredPermission
            ? Result.Success()
            : Result.Failure(LedgerErrors.AccessDenied);
    }

    public async Task<Result<FinancialUserAccessResponse>> GrantAsync(GrantFinancialUserAccessRequest request, string grantedBy, CancellationToken cancellationToken = default)
    {
        if (!await IsMasterAsync(grantedBy, cancellationToken))
            return Result.Failure<FinancialUserAccessResponse>(LedgerErrors.AccessDenied);

        if (!await dbcontext.ApplicationUsers.AnyAsync(x => x.Id == request.UserId, cancellationToken))
            return Result.Failure<FinancialUserAccessResponse>(LedgerErrors.FinancialUserNotFound);

        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == request.LegalEntityId, cancellationToken))
            return Result.Failure<FinancialUserAccessResponse>(LedgerErrors.LegalEntityNotFound);

        var permissions = Normalize(request.Permissions);
        var access = await dbcontext.FinancialUserAccesses.SingleOrDefaultAsync(
            x => x.UserId == request.UserId && x.LegalEntityId == request.LegalEntityId,
            cancellationToken);

        if (access is null)
        {
            access = new FinancialUserAccess
            {
                UserId = request.UserId,
                LegalEntityId = request.LegalEntityId,
                Permissions = permissions,
                GrantedBy = grantedBy
            };
            dbcontext.FinancialUserAccesses.Add(access);
        }
        else
        {
            access.Permissions = permissions;
            access.GrantedBy = grantedBy;
            access.UpdatedAt = DateTime.UtcNow;
        }

        await AuditAsync(request.LegalEntityId, "Security.FinancialAccessGranted", grantedBy, new { request.UserId, Permissions = permissions }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        var userName = await dbcontext.ApplicationUsers
            .Where(x => x.Id == access.UserId)
            .Select(x => x.UserName ?? x.FullName ?? x.Id)
            .SingleAsync(cancellationToken);

        return Result.Success(ToResponse(access, userName));
    }

    public async Task<Result> RevokeAsync(string userId, int legalEntityId, string revokedBy, CancellationToken cancellationToken = default)
    {
        if (!await IsMasterAsync(revokedBy, cancellationToken))
            return Result.Failure(LedgerErrors.AccessDenied);

        var access = await dbcontext.FinancialUserAccesses.SingleOrDefaultAsync(
            x => x.UserId == userId && x.LegalEntityId == legalEntityId,
            cancellationToken);

        if (access is null)
            return Result.Failure(LedgerErrors.FinancialUserNotFound);

        dbcontext.FinancialUserAccesses.Remove(access);
        await AuditAsync(legalEntityId, "Security.FinancialAccessRevoked", revokedBy, new { UserId = userId }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyCollection<FinancialUserAccessResponse>>> GetForLegalEntityAsync(int legalEntityId, string requestedBy, CancellationToken cancellationToken = default)
    {
        if (!await IsMasterAsync(requestedBy, cancellationToken))
            return Result.Failure<IReadOnlyCollection<FinancialUserAccessResponse>>(LedgerErrors.AccessDenied);

        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == legalEntityId, cancellationToken))
            return Result.Failure<IReadOnlyCollection<FinancialUserAccessResponse>>(LedgerErrors.LegalEntityNotFound);

        var accesses = await dbcontext.FinancialUserAccesses
            .AsNoTracking()
            .Where(x => x.LegalEntityId == legalEntityId)
            .OrderBy(x => x.User.UserName)
            .Select(x => new FinancialUserAccessResponse(
                x.Id,
                x.UserId,
                x.User.UserName ?? x.User.FullName ?? x.UserId,
                x.LegalEntityId,
                x.Permissions,
                x.GrantedBy,
                x.GrantedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyCollection<FinancialUserAccessResponse>>(accesses);
    }

    private async Task<bool> IsMasterAsync(string userId, CancellationToken cancellationToken) => await dbcontext.UserRoles
        .Where(x => x.UserId == userId)
        .Join(
            dbcontext.ApplicationRoles,
            userRole => userRole.RoleId,
            role => role.Id,
            (_, role) => role.Name)
        .AnyAsync(name => name == "Master", cancellationToken);

    private async Task<bool> HasFullAccountingRoleAsync(string userId, CancellationToken cancellationToken) => await dbcontext.UserRoles
        .Where(x => x.UserId == userId)
        .Join(
            dbcontext.ApplicationRoles,
            userRole => userRole.RoleId,
            role => role.Id,
            (_, role) => role.Name)
        .AnyAsync(name => name == "Master" || name == "Accountant", cancellationToken);

    private static FinancialPermission Normalize(FinancialPermission permissions) => permissions == FinancialPermission.None
        ? FinancialPermission.None
        : permissions | FinancialPermission.View;

    private async Task AuditAsync(int legalEntityId, string eventType, string actorId, object payload, CancellationToken cancellationToken)
    {
        if (dbcontext.Database.IsSqlServer() && dbcontext.Database.CurrentTransaction is not null)
            await dbcontext.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource={"Accounting:AuditChain:" + legalEntityId}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000", cancellationToken);
        var head = await dbcontext.AccountingAuditChainHeads.SingleOrDefaultAsync(x => x.LegalEntityId == legalEntityId, cancellationToken);
        if (head is null) { head = new AccountingAuditChainHead { LegalEntityId = legalEntityId }; dbcontext.AccountingAuditChainHeads.Add(head); }
        var payloadJson = JsonSerializer.Serialize(payload);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{head.LastHash}|{legalEntityId}||{eventType}|{actorId}|{payloadJson}")));

        dbcontext.AccountingAuditEvents.Add(new AccountingAuditEvent
        {
            LegalEntityId = legalEntityId,
            EventType = eventType,
            ActorId = actorId,
            PayloadJson = payloadJson,
            PreviousHash = head.LastHash,
            Hash = hash
        });
        dbcontext.AccountingOutboxMessages.Add(new AccountingOutboxMessage
        {
            LegalEntityId = legalEntityId,
            Type = eventType,
            PayloadJson = payloadJson,
            CorrelationId = hash[..32]
        });
        head.LastHash = hash;
    }

    private static FinancialUserAccessResponse ToResponse(FinancialUserAccess access, string userName) => new(
        access.Id,
        access.UserId,
        userName,
        access.LegalEntityId,
        access.Permissions,
        access.GrantedBy,
        access.GrantedAt,
        access.UpdatedAt);
}
