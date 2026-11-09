using System.Security.Claims;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Checkout;
using Algara.Web.Services.Shipping;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.DataProtection;
namespace Algara.UnitTests.Services;
public sealed class StaffOrderTests
{
    private static StaffOrderForm Form() => new() { PartnerN=1,ContactName="Test Customer",Phone="0888000000",Reason="Phone agreement",Lines=[new(1,null,1)] };
    [Fact]
    public void CopyUsesOrderContactsExactVariantsAndDestinationButRequiresFreshAgreement()
    {
        var order=new Order {N=12,PartnerN=3,UserN=4,ContactName="Recipient",ContactPhone="0888000000",ContactEmail="buyer@example.test",
            CreationChannel="InStore",Comment="Call first",InvoiceDetails="Invoice data",Status=OrderStatus.Shipped,WaybillNumber="OLD",
            TotalAmount=999,DeliveryAmount=50,SubmissionId=Guid.NewGuid(),CreatedByStaffId="old-staff",TermsAcceptedAtUtc=DateTime.UtcNow,
            DeliverySnapshot="""{"Method":2,"City":"Plovdiv","PostalCode":"4000","Address":null,"AddressDetails":"Ask for recipient","Office":{"Code":"123","Name":"Old office","Address":"Old address"},"IsTest":true}""",
            OrderItems=[new(){N=1,ProductN=10,VariantN=20,Quantity=2,UnitPrice=100,ReservedQuantity=2,DeductedQuantity=2}]
        };
        var form=StaffOrderService.CopyForm(order,"fresh-token");
        Assert.Equal(12,form.CopiedFromOrderN);Assert.Equal(3,form.PartnerN);Assert.Equal(4,form.CustomerUserN);
        Assert.Equal("Recipient",form.ContactName);Assert.Equal(order.ContactPhone,form.Phone);Assert.Equal(order.ContactEmail,form.Email);
        Assert.Equal("InStore",form.Channel);Assert.Equal(order.Comment,form.Comment);Assert.Equal(order.InvoiceDetails,form.InvoiceDetails);
        Assert.Equal(new CartEntry(10,20,2),Assert.Single(form.Lines));
        Assert.Equal(DeliveryMethod.EcontOffice,form.Destination.Method);Assert.Equal("Plovdiv",form.Destination.City);Assert.Equal("123",form.Destination.OfficeCode);
        Assert.Equal("Ask for recipient",form.Destination.AddressDetails);Assert.Empty(form.Destination.Offices);
        Assert.Equal("fresh-token",form.SubmissionToken);Assert.Null(form.PreviewToken);Assert.Null(form.Quote);Assert.False(form.CustomerAgreed);Assert.False(form.Storefront);
        Assert.Equal(OrderStatus.Shipped,order.Status);Assert.Equal("OLD",order.WaybillNumber);Assert.Equal(2,Assert.Single(order.OrderItems).DeductedQuantity);
    }
    [Fact]
    public void CopyPreservesAddressAndMergesRepeatedExactVariants()
    {
        var order=new Order {N=3,DeliverySnapshot="""{"Method":3,"City":"Plovdiv","Address":"Street 1","PostalCode":"4000"}""",
            OrderItems=[new(){ProductN=1,VariantN=2,Quantity=1},new(){ProductN=1,VariantN=2,Quantity=2},new(){ProductN=1,VariantN=3,Quantity=1}]};
        var form=StaffOrderService.CopyForm(order,"new");
        Assert.Equal(0,form.PartnerN);Assert.Null(form.CustomerUserN);Assert.Equal("Phone",form.Channel);
        Assert.Equal("Street 1",form.Destination.Address);Assert.Equal("4000",form.Destination.PostalCode);
        Assert.Equal(2,form.Lines.Count);Assert.Equal(new CartEntry(1,2,3),form.Lines[0]);Assert.Equal(new CartEntry(1,3,1),form.Lines[1]);
    }
    [Theory]
    [InlineData(null,true)] [InlineData("",true)] [InlineData("buyer@example.test",true)] [InlineData("bad",false)]
    public void EmailIsOptionalButMustBeValidWhenProvided(string? email,bool valid)
    { var form=Form();form.Email=email;Assert.Equal(valid,StaffOrderService.Valid(form)); }
    [Theory]
    [InlineData("Phone",true)] [InlineData("InStore",true)] [InlineData("Web",false)] [InlineData("",false)]
    public void ChannelMustRepresentActualStaffSale(string channel,bool valid)
    { var form=Form();form.Channel=channel;Assert.Equal(valid,StaffOrderService.Valid(form)); }
    [Fact]
    public void DuplicateLinesAndLimitsCannotBypassInventoryRules()
    {
        var form=Form();form.Lines.Add(new(1,null,1));Assert.False(StaffOrderService.Valid(form));
        form.Lines=[new(1,null,100)];Assert.False(StaffOrderService.Valid(form));
        form.Lines=[new(1,null,0)];Assert.False(StaffOrderService.Valid(form));
        form.Lines=Enumerable.Range(1,31).Select(n=>new CartEntry(n,null,1)).ToList();Assert.False(StaffOrderService.Valid(form));
    }
    [Fact]
    public void RequiredCustomerAndAgreementDetailsCannotBeWhitespace()
    {
        var form=Form();form.ContactName=" ";Assert.False(StaffOrderService.Valid(form));
        form=Form();form.Reason=" ";Assert.False(StaffOrderService.Valid(form));
        form=Form();form.Phone=" ";Assert.False(StaffOrderService.Valid(form));
        form=Form();form.PartnerN=0;Assert.False(StaffOrderService.Valid(form));
        form=Form();form.Destination.Method=(DeliveryMethod)99;Assert.False(StaffOrderService.Valid(form));
    }
    [Theory]
    [InlineData(StaffRoles.Moderator)] [InlineData(StaffRoles.OrderManager)] [InlineData(StaffRoles.CatalogEditor)]
    public void CreateRequiresExplicitGrant(string role) => Assert.DoesNotContain(PermissionCatalog.Orders.Create,PermissionCatalog.DefaultGrants(role));
    [Fact]
    public void SubmissionIsSeparatedFromCheckoutAndOtherSessions()
    {
        var actor=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"staff"),new Claim("SessionId","one"),new Claim("SecurityStamp","stamp")],"test"));
        var tokens=new CheckoutTokens(new EphemeralDataProtectionProvider(),TimeProvider.System);
        var token=tokens.Issue(StaffOrderService.Owner(actor));
        Assert.NotNull(tokens.Read(token,StaffOrderService.Owner(actor)));
        Assert.Null(tokens.Read(token,CheckoutService.Owner(actor)));
        ((ClaimsIdentity)actor.Identity!).RemoveClaim(actor.FindFirst("SessionId")!);
        ((ClaimsIdentity)actor.Identity!).AddClaim(new Claim("SessionId","two"));
        Assert.Null(tokens.Read(token,StaffOrderService.Owner(actor)));
    }
    [Fact]
    public void PreviewBindsCustomerContactAgreementLocationAndPrice()
    {
        var form=Form();
        var cart=new CartSnapshot { Lines=[new CartLine { Entry=new(1,null,1),Product=new Product { N=1,Name="Chair",Price=10 },UnitPrice=10 }] };
        var quote=new DeliveryQuote(10,0,DateTimeOffset.UtcNow);
        string Fingerprint()=>StaffOrderService.Fingerprint(form,cart,quote,"partner-v1","link-v1");
        var original=Fingerprint();
        form.Storefront=true;Assert.NotEqual(original,Fingerprint());form.Storefront=false;
        form.CopiedFromOrderN=12;Assert.NotEqual(original,Fingerprint());form.CopiedFromOrderN=null;
        form.CustomerUserN=3;Assert.NotEqual(original,Fingerprint());form.CustomerUserN=null;
        form.Email="new@example.test";Assert.NotEqual(original,Fingerprint());form.Email=null;
        form.Reason="Different agreement";Assert.NotEqual(original,Fingerprint());form.Reason="Phone agreement";
        form.Destination.AddressDetails="Second floor";Assert.NotEqual(original,Fingerprint());form.Destination.AddressDetails=null;
        Assert.NotEqual(original,StaffOrderService.Fingerprint(form,cart,quote,"partner-v2","link-v1"));
        Assert.NotEqual(original,StaffOrderService.Fingerprint(form,cart,quote,"partner-v1","link-v2"));
        Assert.NotEqual(original,StaffOrderService.Fingerprint(form,cart,quote with { DeliveryAmount=5 },"partner-v1","link-v1"));
        Assert.Equal(original,Fingerprint());
    }
}
