using Algara.Data.Models;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Checkout;
using Algara.Web.Services.Shipping;
namespace Algara.UnitTests.Services;
public sealed class AgreedOfferTests
{
    [Theory] [InlineData("12.50",true)] [InlineData("12,50",true)] [InlineData("1,000.50",false)] [InlineData("1e3",false)]
    public void ServiceAllocationAcceptsEitherDecimalSeparatorWithoutGrouping(string value,bool valid)
    {
        Assert.Equal(valid,Algara.Web.Services.Inquiries.ServiceAmountsBinder.TryAmount(value,out var amount));
        if(valid)Assert.Equal(12.50m,amount);
    }
    [Fact] public void SplitPreservesSpecificationNegotiatedPriceAndReadiness()
    {
        var line=new OrderItem { Quantity=5, ReadyQuantity=3, IndividualManufacture=true, UnitPrice=123, CatalogUnitPrice=100, ExecutionComment="Wide build" };
        var moved=OrderSplitQuantities.Transfer(line,2);
        Assert.Equal(3,line.Quantity);Assert.Equal(1,line.ReadyQuantity);
        Assert.Equal(2,moved.ReadyQuantity);Assert.True(moved.IndividualManufacture);
        Assert.Equal("Wide build",moved.ExecutionComment);Assert.Equal(100,moved.CatalogUnitPrice);Assert.Equal(123,moved.UnitPrice);
        Assert.Equal(0,moved.DeductedQuantity);Assert.Equal(0,moved.ReservedQuantity);
        Assert.Throws<ArgumentOutOfRangeException>(()=>OrderSplitQuantities.Transfer(line,2));
    }
    [Fact] public void ServicesAreConservedAcrossRepeatedSplits()
    {
        var source=new List<AgreedService>{new("Assembly",150),new("Stairs",40)};
        var split=AgreedOffer.SplitServices(source,new Dictionary<int,decimal>{{0,50},{1,40}})!.Value;
        Assert.Equal(100,split.Remaining.Sum(s=>s.Amount));Assert.Equal(90,split.Separated.Sum(s=>s.Amount));
        var second=AgreedOffer.SplitServices(split.Remaining,new Dictionary<int,decimal>{{0,30}})!.Value;
        Assert.Equal(190,second.Remaining.Sum(s=>s.Amount)+second.Separated.Sum(s=>s.Amount)+split.Separated.Sum(s=>s.Amount));
        Assert.Equal("Assembly",second.Separated.Single().Name);
    }
    [Theory] [InlineData(-1)] [InlineData(101)] [InlineData(1.001)]
    public void InvalidServiceAllocationRejected(decimal value) => Assert.Null(AgreedOffer.SplitServices([new("Assembly",100)],new Dictionary<int,decimal>{{0,value}}));
    [Fact] public void UnknownServiceRejected()=>Assert.Null(AgreedOffer.SplitServices([new("Assembly",100)],new Dictionary<int,decimal>{{1,0}}));
    [Fact] public void IndividualBuildCannotUseStandardCourierParcels()
    {
        var cart=new CartSnapshot { Lines=[new() { Entry=new(1,null,1),Product=new() { AllowsCourier=true },IndividualManufacture=true,UnitPrice=10 }] };
        Assert.False(DeliveryQuoteService.AllowsCourier(cart));Assert.True(DeliveryQuoteService.AllowsOwnTransport(cart));
    }
    [Fact] public void StaffOnlyProductsCannotEnterPublicCart()
    {
        var product=new Product { N=1,IsActive=true,IsStaffOnly=true,StockQuantity=99,Price=10 };
        Assert.Equal(CartLineIssue.InactiveProduct,CartService.Evaluate(new(1,null,1),product,DateTime.UtcNow).Issue);
    }
}
