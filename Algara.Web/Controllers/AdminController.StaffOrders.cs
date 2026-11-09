using System.Security.Claims;
using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Identity.Authorization;
using Algara.Web.Authorization;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Checkout;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace Algara.Web.Controllers;
public partial class AdminController
{
    [HttpGet("orders/create"), RequirePermission("Orders.View","Orders.Create","Partners.View")]
    public async Task<IActionResult> OrderCreate([FromServices] CheckoutTokens tokens)
    {
        var form=new StaffOrderForm { SubmissionToken=tokens.Issue(StaffOrderService.Owner(User)) };
        await LoadStaffOrderAsync(form);
        return View(form);
    }
    [HttpPost("orders/create"),ValidateAntiForgeryToken,EnableRateLimiting("DeliveryQuotes"),RequirePermission("Orders.View","Orders.Create","Partners.View")]
    public async Task<IActionResult> OrderCreate(StaffOrderForm form,string? command,[FromServices] StaffOrderService service)
    {
        if(form.Lines.Count>CartService.MaxLines) return BadRequest();
        if(command?.StartsWith("add:",StringComparison.Ordinal)==true)
        {
            var parts=command.Split(':');
            if(parts.Length==3 && int.TryParse(parts[1],out var productN) && int.TryParse(parts[2],out var variantN))
            {
                int? variant=variantN==0?null:variantN;
                var index=form.Lines.FindIndex(l=>l.ProductN==productN&&l.VariantN==variant);
                if(index>=0 && form.Lines[index].Quantity<CartService.MaxQuantity)form.Lines[index]=form.Lines[index] with { Quantity=form.Lines[index].Quantity+1 };
                else if(index<0&&form.Lines.Count<CartService.MaxLines)form.Lines.Add(new(productN,variant,1));
            }
        }
        else if(command?.StartsWith("remove:",StringComparison.Ordinal)==true && int.TryParse(command[7..],out var remove)&&remove>=0&&remove<form.Lines.Count) form.Lines.RemoveAt(remove);
        else if(command?.StartsWith("partner:",StringComparison.Ordinal)==true && int.TryParse(command[8..],out var partner))
        { form.PartnerN=partner;form.CustomerUserN=null; }
        else if(command=="offices")
        {
            form.Cart=await service.ReadCartAsync(form.Lines);
            var delivery=HttpContext.RequestServices.GetRequiredService<Algara.Web.Services.Shipping.DeliveryQuoteService>();
            var result=await delivery.OfficesAsync(form.Cart,form.Destination.Method,form.Destination.City,HttpContext.RequestAborted);
            if(result.Success) { form.Destination.Offices=result.Offices; if(!result.Offices.Any(o=>o.Code==form.Destination.OfficeCode))form.Destination.OfficeCode=null; }
            else form.Error = !Algara.Web.Services.Shipping.DeliveryQuoteService.IsOffice(form.Destination.Method)
                ? "Изберете доставка до офис на Еконт или Спиди, а не до адрес."
                : !form.Cart.CanQuote ? "Проверете количеството и наличността на избраните артикули преди зареждане на офисите."
                : !Algara.Web.Services.Shipping.DeliveryQuoteService.AllowsCourier(form.Cart) ? "В избраните артикули има стока, която не позволява куриер."
                : $"Куриерът не върна офиси за {form.Destination.City}. Причина: {result.ErrorCode}. Проверете настройките за този куриер.";
        }
        else if(command is "preview" or "submit")
        {
            if(ModelState.IsValid)
            {
                var n=await service.RunAsync(form,User,command=="submit",HttpContext.RequestAborted);
                if(n.HasValue) { TempData["Success"]="Служебната поръчка е записана и очаква потвърждение.";return RedirectToAction(form.Storefront?nameof(StaffOrderReceipt):nameof(OrderDetail),new { n }); }
            }
            else { form.Error="Проверете въведените данни.";form.PreviewToken=null; }
        }
        if(command is not ("preview" or "submit"))form.PreviewToken=null;
        form.CustomerAgreed=false;
        ModelState.Clear();
        form.Cart=await service.ReadCartAsync(form.Lines);
        await LoadStaffOrderAsync(form);
        if(command?.StartsWith("partner:",StringComparison.Ordinal)==true && form.Customers.SingleOrDefault(c=>c.N==form.CustomerUserN) is {} selected)
        {
            form.ContactName=selected.ContactName??"";form.Phone=selected.Phone??"";form.Email=selected.Email;
        }
        return View(form);
    }
    [HttpGet("orders/{n:int}/copy"),RequirePermission("Orders.View","Orders.Create","Partners.View")]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> OrderCopy(int n,[FromServices] CheckoutTokens tokens,[FromServices] StaffOrderService service)
    {
        var ct=HttpContext.RequestAborted;
        var order=await _shopDb.Orders.AsNoTracking().Include(o=>o.OrderItems).SingleOrDefaultAsync(o=>o.N==n,ct);
        if(order==null)return NotFound();
        var form=StaffOrderService.CopyForm(order,tokens.Issue(StaffOrderService.Owner(User)));
        if(form.PartnerN==0&&order.UserN.HasValue)
            form.PartnerN=await _shopDb.PartnerAccountLinks.Where(l=>l.UserN==order.UserN).Select(l=>l.PartnerN).SingleOrDefaultAsync(ct)??0;
        await LoadStaffOrderAsync(form);
        if(form.Partner==null)
        {
            form.PartnerN=0;form.CustomerUserN=null;form.ActiveAccountCount=0;form.Customers.Clear();
            form.Error="Изберете активен контрагент за новата поръчка. Данните на получателя са пренесени.";
        }
        else if(form.CustomerUserN.HasValue&&!form.Customers.Any(c=>c.N==form.CustomerUserN))
        {
            form.CustomerUserN=null;
            form.Error="Акаунтът от първоначалната поръчка вече не е достъпен за избор. Проверете клиента за новата поръчка.";
        }
        form.Cart=await service.ReadCartAsync(form.Lines);
        return View("OrderCreate",form);
    }
    [HttpGet("orders/create/search"),RequirePermission("Orders.View","Orders.Create","Partners.View")]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> StaffOrderSearch(string? q,string kind)
    {
        q=q?.Trim();
        q ??= "";
        if(q.Length>100 || (kind=="partners" && q.Length<3))return Json(Array.Empty<object>());
        var ct=HttpContext.RequestAborted;
        if(kind=="partners")
        {
            var matches=await _shopDb.Partners.AsNoTracking().Where(p=>p.IsActive&&(p.Code.Contains(q)||p.SiteName.Contains(q)))
                .OrderBy(p=>p.Code).Take(20).Select(p=>new {p.N,p.Code,p.SiteName}).ToListAsync(ct);
            return Json(matches.Select(p=>new {command="partner:"+p.N,label=p.Code+" — "+p.SiteName}));
        }
        if(kind!="products")return BadRequest();
        var ids=await _shopDb.Products.AsNoTracking().Where(p=>p.IsActive&&(p.Name.Contains(q)||p.Sku.Contains(q)||p.Variants.Any(v=>v.Sku.Contains(q))))
            .OrderBy(p=>p.Name).Select(p=>p.N).Take(20).ToListAsync(ct);
        var choices=new List<object>();var repo=new ProductRepository(_shopDb);
        foreach(var id in ids)
        {
            var p=await repo.GetByNAsync(id);if(p==null)continue;
            if(p.OptionKind==ProductOptionKind.None)choices.Add(new {command=$"add:{p.N}:0",label=p.Name+" — "+p.Sku,stock=p.StockQuantity?.ToString()??"неизвестна"});
            else foreach(var v in p.GetSelectableVariants())choices.Add(new {command=$"add:{p.N}:{v.N}",label=p.Name+" — "+v.Color?.Fabric?.GetName()+" / "+v.Color?.GetName()+" — "+v.Sku,stock=v.StockQuantity?.ToString()??"неизвестна"});
        }
        return Json(choices.Take(20));
    }
    [HttpPost("/Checkout/Staff"),ValidateAntiForgeryToken,RequirePermission("Orders.View","Orders.Create","Partners.View")]
    public async Task<IActionResult> StaffCheckout([FromServices] CartService cart,[FromServices] CheckoutTokens tokens)
    {
        var snapshot=await cart.GetAsync();
        var form=new StaffOrderForm {Storefront=true,Cart=snapshot,Lines=snapshot.Lines.Select(l=>l.Entry).ToList(),SubmissionToken=tokens.Issue(StaffOrderService.Owner(User))};
        await LoadStaffOrderAsync(form);
        return View("OrderCreate",form);
    }
    [HttpGet("/Checkout/StaffOrder/{n:int}"),RequirePermission("Orders.View","Orders.Create","Partners.View")]
    public async Task<IActionResult> StaffOrderReceipt(int n)
    {
        var actor=User.FindFirstValue(ClaimTypes.NameIdentifier);
        var order=await _shopDb.Orders.AsNoTracking().Include(o=>o.OrderItems).SingleOrDefaultAsync(o=>o.N==n&&o.CreatedByStaffId==actor);
        return order==null?NotFound():View(order);
    }
    private async Task LoadStaffOrderAsync(StaffOrderForm form)
    {
        var ct=HttpContext.RequestAborted;
        form.EnabledCouriers=await _identityDb.CourierConfigurations.AsNoTracking().Where(c=>c.Enabled).Select(c=>c.Id).ToListAsync(ct);
        form.Partner=await _shopDb.Partners.AsNoTracking().SingleOrDefaultAsync(p=>p.N==form.PartnerN&&p.IsActive,ct);
        var linkedIds=await _shopDb.PartnerAccountLinks.Where(l=>l.PartnerN==form.PartnerN).Select(l=>l.UserN).ToListAsync(ct);
        form.ActiveAccountCount=await _identityDb.Users.CountAsync(u=>linkedIds.Contains(u.N)&&u.IsActive,ct);
        var access=new StaffAccessService(_identityDb);
        if(form.Partner is { AllowAccountLinks:true } && form.Partner.Kind!=PartnerKind.GeneralCustomer &&
            await access.HasPermissionAsync(User,PermissionCatalog.Users.View,ct)&&await access.HasPermissionAsync(User,PermissionCatalog.Users.ViewDossier,ct))
        {
            var ids=await _shopDb.PartnerAccountLinks.Where(l=>l.PartnerN==form.PartnerN).Select(l=>l.UserN).ToListAsync(ct);
            var actorId=User.FindFirstValue(ClaimTypes.NameIdentifier);
            var actorRoles=await _identityDb.UserRoles.Where(r=>r.User.Id==actorId).Select(r=>r.Role.Name).ToListAsync(ct);
            foreach(var user in await _identityDb.Users.AsNoTracking().Where(u=>ids.Contains(u.N)&&u.IsActive).OrderBy(u=>u.FirstName).ToListAsync(ct))
            {
                var roles=await _identityDb.UserRoles.Where(r=>r.UserN==user.N).Select(r=>r.Role.Name).ToListAsync(ct);
                if(StaffAccountManagementRules.CanManage(actorRoles,roles)) form.Customers.Add(new(user.N,(user.FirstName+" "+user.LastName).Trim()+" — "+user.Email,(user.FirstName+" "+user.LastName).Trim(),user.PhoneNumber,user.Email));
            }
        }
        if(form.ActiveAccountCount==1 && form.Customers.Count==1) form.CustomerUserN=form.Customers[0].N;
    }
}
