using Algara.Data.Models;
using Algara.Web.Authorization;
using Algara.Web.Services.Checkout;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Algara.Web.Controllers;
public partial class AdminController
{
    [HttpGet("inquiries/{n:int}/order"), RequirePermission("Inquiries.View","Inquiries.Manage","Orders.View","Orders.Create","Partners.View")]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> OrderFromInquiry(int n, [FromServices] CheckoutTokens tokens, [FromServices] StaffOrderService service)
    {
        var inquiry=await _shopDb.Set<FurnitureInquiry>().AsNoTracking().Include(i=>i.Offers).SingleOrDefaultAsync(i=>i.N==n);
        if(inquiry==null)return NotFound();
        if(inquiry.OrderN.HasValue)return RedirectToAction(nameof(OrderDetail),new{n=inquiry.OrderN});
        var offer=inquiry.Offers.OrderByDescending(o=>o.N).FirstOrDefault();
        if(inquiry.Status!=InquiryStatus.Accepted || offer?.LinesJson==null) {
            TempData["Error"]="Запишете и потвърдете актуална оферта с артикули.";
            return RedirectToAction("StaffDetails","Inquiries",new{n});
        }
        var form=new StaffOrderForm { InquiryN=n,InquiryOfferN=offer.N,InquiryVersion=Convert.ToBase64String(inquiry.RowVersion),
            SubmissionToken=tokens.Issue(StaffOrderService.Owner(User)),ContactName=inquiry.ContactName,Phone=inquiry.Phone,Email=inquiry.Email,
            Channel=inquiry.Channel=="InStore"?"InStore":"Phone",Reason=$"По приета оферта #{offer.N}, запитване #{n}",
            Comment=$"Запитване #{n}; оферта #{offer.N}. Спецификацията и основанието за цената са по редовете." };
        if(inquiry.CustomerId!=null) {
            var userN=await _identityDb.Users.Where(u=>u.Id==inquiry.CustomerId&&u.IsActive).Select(u=>(int?)u.N).SingleOrDefaultAsync();
            form.CustomerUserN=userN;
            if(userN.HasValue)form.PartnerN=await _shopDb.PartnerAccountLinks.Where(l=>l.UserN==userN).Select(l=>l.PartnerN).SingleOrDefaultAsync()??0;
        }
        form.Cart=await service.ReadCartAsync(form);
        if(offer.DeliveryAmount is >0)form.Destination.Method=Services.Shipping.DeliveryMethod.OwnTransport;
        await LoadStaffOrderAsync(form);
        return View("OrderCreate",form);
    }

    [HttpPost("orders/{n:int}/ready"),ValidateAntiForgeryToken,RequirePermission("Orders.View","Orders.ChangeStatus")]
    public async Task<IActionResult> OrderReady(int n,int itemN,int quantity,string version,string? note,[FromServices]OrderLifecycleService service)
    {
        var ok=await service.ReadyAsync(n,itemN,quantity,version,note,User,HttpContext.RequestAborted);
        TempData[ok?"Success":"Error"]=ok?"Готовото количество е записано.":"Проверете количеството и бележката или презаредете променената поръчка.";
        return RedirectToAction(nameof(OrderDetail),new{n});
    }
}
