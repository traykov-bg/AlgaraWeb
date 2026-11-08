using Algara.Data.Models;
using Algara.Web.Authorization;
using Algara.Web.Services.Checkout;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace Algara.Web.Controllers;
public partial class AdminController
{
 [HttpGet("orders/{n}/split"), RequirePermission("Orders.View","Orders.Split")]
 public async Task<IActionResult> OrderSplit(int n)
 {
  var order=await _shopDb.Orders.AsNoTracking().Include(o=>o.OrderItems).SingleOrDefaultAsync(o=>o.N==n);
  if(order==null)return NotFound();
  if(!OrderSplitService.CanSplit(order)){TempData["Error"]="Разделяне е възможно само за потвърдена поръчка без товарителница.";return RedirectToAction(nameof(OrderDetail),new{n});}
  return View(new OrderSplitForm{EnabledCouriers=await _identityDb.CourierConfigurations.AsNoTracking().Where(c=>c.Enabled).Select(c=>c.Id).ToListAsync(),Order=order,Version=Convert.ToBase64String(order.RowVersion),Remaining=OrderSplitService.ReadDestination(order.DeliverySnapshot),Separated=OrderSplitService.ReadDestination(order.DeliverySnapshot)});
 }
 [HttpPost("orders/{n}/split"),ValidateAntiForgeryToken,EnableRateLimiting("DeliveryQuotes"),RequirePermission("Orders.View","Orders.Split")]
 public async Task<IActionResult> OrderSplit(int n,OrderSplitForm form,string? command,[FromServices]OrderSplitService split)
 {
  form.Order=(await _shopDb.Orders.AsNoTracking().Include(o=>o.OrderItems).SingleOrDefaultAsync(o=>o.N==n))!;
  if(form.Order==null)return NotFound();
  if(ModelState.IsValid)
  {
   var child=await split.RunAsync(n,form,User,command=="submit",HttpContext.RequestAborted);
   if(child!=null){TempData["Success"]=$"Поръчката е разделена. Нова поръчка #{child}.";return RedirectToAction(nameof(OrderDetail),new{n=child});}
  }
  else {form.Error="Проверете въведените данни.";form.PreviewToken=null;}
  form.EnabledCouriers=await _identityDb.CourierConfigurations.AsNoTracking().Where(c=>c.Enabled).Select(c=>c.Id).ToListAsync();
  ModelState.Remove(nameof(form.PreviewToken));
  // Agreement applies only to the exact preview; a new preview always requires a fresh check.
  form.CustomerAgreed=false;ModelState.Remove(nameof(form.CustomerAgreed));
  return View(form);
 }
}
