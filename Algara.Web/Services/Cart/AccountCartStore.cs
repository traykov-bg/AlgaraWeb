using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;
namespace Algara.Web.Services.Cart;

public sealed class AccountCartStore(IHttpContextAccessor accessor,ProtectedCookieCartStore guest,DbContextOptions<ShopDbContext> options,TimeProvider clock):ICartStore
{
 private string? Owner => accessor.HttpContext?.User.Identity?.IsAuthenticated == true ? accessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
 public IReadOnlyList<CartEntry> Read()
 {
  if(Owner is not {} owner)
  {
   var snapshot=guest.ReadGuest();if(snapshot==null)return [];
   using var claimsDb=new ShopDbContext(options);
   if(claimsDb.Set<GuestCartClaim>().Any(c=>c.Id==snapshot.Id)){guest.DiscardClaimedGuest();return [];}
   return snapshot.Entries;
  }
  using var db=new ShopDbContext(options);
  return Decode(db.Set<CustomerCart>().AsNoTracking().SingleOrDefault(c=>c.UserId==owner)?.Contents);
 }
 public void Write(IReadOnlyList<CartEntry> entries)
 {
  if(Owner is not {} owner){Read();guest.Write(entries);return;}
  if(!ValidIdentities(entries))throw new ArgumentException("Invalid cart contents.",nameof(entries));
  using var db=new ShopDbContext(options);using var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted);
  Lock(db,owner);
  var row=db.Set<CustomerCart>().SingleOrDefault(c=>c.UserId==owner);
  if(row==null){row=new CustomerCart{UserId=owner};db.Add(row);}
  row.Contents=JsonSerializer.Serialize(entries);row.UpdatedAtUtc=clock.GetUtcNow().UtcDateTime;
  db.SaveChanges();tx.Commit();
 }
 internal static async Task ClearAsync(ShopDbContext db,string owner,DateTime updatedAtUtc,CancellationToken ct)
 {
  if(db.Database.CurrentTransaction==null)throw new InvalidOperationException("Cart clearing requires the order transaction.");
  await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={Resource(owner)},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 51004,'Cart busy.',1;",ct);
  var row=await db.Set<CustomerCart>().SingleOrDefaultAsync(c=>c.UserId==owner,ct);
  if(row!=null){row.Contents="[]";row.UpdatedAtUtc=updatedAtUtc;}
 }
 public async Task<bool> ImportGuestAsync(CancellationToken ct)
 {
  if(Owner is not {} owner || guest.ReadGuest() is not {} cart || cart.Entries.Count==0)return false;
  await using var db=new ShopDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
  await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={Resource(owner)},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 51004,'Cart busy.',1;",ct);
  await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={"Algara.GuestCart."+cart.Id.ToString("N")},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 51004,'Cart busy.',1;",ct);
  var claimed=await db.Set<GuestCartClaim>().AnyAsync(c=>c.Id==cart.Id,ct);
  if(!claimed)
  {
   var row=await db.Set<CustomerCart>().SingleOrDefaultAsync(c=>c.UserId==owner,ct);
   if(row==null){row=new CustomerCart{UserId=owner};db.Add(row);}
   row.Contents=JsonSerializer.Serialize(Merge(Decode(row.Contents),cart.Entries));row.UpdatedAtUtc=clock.GetUtcNow().UtcDateTime;
   db.Add(new GuestCartClaim{Id=cart.Id,UserId=owner,ClaimedAtUtc=clock.GetUtcNow().UtcDateTime});await db.SaveChangesAsync(ct);
  }
  await tx.CommitAsync(ct);
  guest.DiscardClaimedGuest();
  return !claimed;
 }
 public static IReadOnlyList<CartEntry> Merge(IReadOnlyList<CartEntry> saved,IReadOnlyList<CartEntry> incoming)
 {
  if(!ValidIdentities(saved)||!ValidIdentities(incoming))throw new ArgumentException("Invalid cart contents.");
  var result=saved.ToList();
  foreach(var entry in incoming){var index=result.FindIndex(e=>e.ProductN==entry.ProductN&&e.VariantN==entry.VariantN);if(index<0)result.Add(entry);else result[index]=entry with {Quantity=checked(result[index].Quantity+entry.Quantity)};}
  return result;
 }
 public static bool ValidIdentities(IReadOnlyList<CartEntry>? entries)=>entries!=null && entries.All(e=>e!=null&&e.ProductN>0&&(e.VariantN==null||e.VariantN>0)&&e.Quantity>0)&&entries.Select(e=>(e.ProductN,e.VariantN)).Distinct().Count()==entries.Count;
 private static IReadOnlyList<CartEntry> Decode(string? contents)
 {
  if(contents==null)return [];
  try{var entries=JsonSerializer.Deserialize<List<CartEntry>>(contents);return ValidIdentities(entries)?entries!:[];}catch(JsonException){return [];}
 }
 private static string Resource(string owner)=>"Algara.Cart."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner)));
 private static void Lock(ShopDbContext db,string owner)=>db.Database.ExecuteSqlInterpolated($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={Resource(owner)},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 51004,'Cart busy.',1;");
}
public sealed class CartOwnershipMiddleware(RequestDelegate next)
{
 public async Task InvokeAsync(HttpContext context,AccountCartStore cart)
 {
  // Legacy v1 was shared across accounts and cannot safely be attributed to an owner.
  if(context.Request.Cookies.ContainsKey("Algara.Cart.v1"))context.Response.Cookies.Delete("Algara.Cart.v1",new CookieOptions{Path=context.Request.PathBase.HasValue?context.Request.PathBase.Value:"/",HttpOnly=true,Secure=context.Request.IsHttps,SameSite=SameSiteMode.Lax});
  await cart.ImportGuestAsync(context.RequestAborted);
  await next(context);
 }
}
