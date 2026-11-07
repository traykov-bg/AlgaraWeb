using System.Security.Claims;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Identity.Data;
using Algara.Web.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Algara.Web.Services.Checkout;

public sealed class GuestOrderLinkService(ShopDbContext db, TimeProvider clock)
{
    public async Task<bool> LinkAsync(int n, string? token, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (actor.Identity?.IsAuthenticated != true || !AccountEmailRules.TryParse(token, out var submission, out var secret)) return false;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @r int;
            EXEC @r=sys.sp_getapplock @Resource=N'Algara.AccessManagement',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
            IF @r<0 THROW 51004,'Order busy.',1;
            """, ct);
        await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(db.Database.GetDbConnection()).Options);
        await identity.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
        var userId = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await identity.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive && u.EmailConfirmed, ct);
        if (user == null || user.SecurityStamp != actor.FindFirstValue("SecurityStamp") ||
            !await identity.UserSessions.AnyAsync(s => s.UserN == user.N && s.IsActive && s.SessionId == actor.FindFirstValue("SessionId"), ct)) return false;
        var order = await db.Orders.SingleOrDefaultAsync(o => o.N == n && o.SubmissionId == submission, ct);
        if (order?.GuestTokenHash == null || order.UserN != null || !AccountEmailRules.MatchesHash(secret, order.GuestTokenHash) ||
            !string.Equals(order.ContactEmail, user.Email, StringComparison.OrdinalIgnoreCase)) return false;
        order.UserN = user.N;
        db.Set<OrderEvent>().Add(new OrderEvent { OrderN = n, Action = "ClaimedByVerifiedOwner", ActorId = user.Id, Status = order.Status, OccurredAtUtc = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
