using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Web.Services.Checkout;
namespace Algara.UnitTests.Services;
public class OrderSplitTests
{
 [Theory]
 [InlineData(5,2,2,1)]
 [InlineData(5,2,2,2)]
 [InlineData(5,2,4,3)]
 [InlineData(5,4,0,3)]
 [InlineData(3,3,3,3)]
 public void TransferConservesQuantityReservationAndDeduction(int quantity,int reserved,int deducted,int move)
 {
  var source=new OrderItem{ProductN=12,VariantN=34,ProductName="Historical name",VariantName="Blue",Sku="OLD",Quantity=quantity,ReservedQuantity=reserved,DeductedQuantity=deducted,UnitPrice=19.75m,RequiresManufacturing=true};
  var child=OrderSplitQuantities.Transfer(source,move);
  Assert.Equal(quantity,source.Quantity+child.Quantity);
  Assert.Equal(reserved,source.ReservedQuantity+child.ReservedQuantity);
  Assert.Equal(deducted,source.DeductedQuantity+child.DeductedQuantity);
  Assert.InRange(source.DeductedQuantity,0,source.Quantity);Assert.InRange(source.ReservedQuantity,0,source.Quantity);
  Assert.InRange(child.DeductedQuantity,0,child.Quantity);Assert.InRange(child.ReservedQuantity,0,child.Quantity);
  Assert.Equal(19.75m,child.UnitPrice);Assert.Equal(34,child.VariantN);Assert.Equal("Historical name",child.ProductName);
  Assert.False(child.RequiresManufacturing);
 }
 [Theory]
 [InlineData(-1)] [InlineData(0)] [InlineData(3)] [InlineData(6)]
 public void CannotTransferUnreadyOrInvalidQuantity(int move)
 {
  var source=new OrderItem{Quantity=5,ReservedQuantity=2,DeductedQuantity=2};
  Assert.Throws<ArgumentOutOfRangeException>(()=>OrderSplitQuantities.Transfer(source,move));
  Assert.Equal(5,source.Quantity);Assert.Equal(2,source.DeductedQuantity);
 }
 [Theory]
 [InlineData(StaffRoles.Moderator)] [InlineData(StaffRoles.OrderManager)] [InlineData(StaffRoles.CatalogEditor)]
 public void SplitRequiresExplicitNonAdminGrant(string role)=>Assert.DoesNotContain(PermissionCatalog.Orders.Split,PermissionCatalog.DefaultGrants(role));
 [Theory]
 [InlineData("Development",true)] [InlineData("development",true)]
 [InlineData("Production",false)] [InlineData("Staging",false)] [InlineData(null,false)]
 public void SandboxSplitOnlyAllowedInDevelopment(string? environment,bool expected)=>Assert.Equal(expected,OrderSplitService.AllowsSandbox(environment));
 [Fact]
 public void SnapshotRetainsSandboxProvenance()
 {
  var json=OrderSplitService.DestinationSnapshot(new(),new Algara.Web.Services.Shipping.DeliveryQuote(20,5,DateTimeOffset.UtcNow,IsTest:true));
  using var doc=System.Text.Json.JsonDocument.Parse(json);Assert.True(doc.RootElement.GetProperty("IsTest").GetBoolean());
 }
 [Fact]
 public void DispatchedOrWaybilledOrdersCannotSplit()
 {
  var order=new Order{SubmissionId=Guid.NewGuid(),Status=OrderStatus.Confirmed};Assert.True(OrderSplitService.CanSplit(order));
  order.WaybillNumber="123";Assert.False(OrderSplitService.CanSplit(order));order.WaybillNumber=null;order.Status=OrderStatus.Shipped;Assert.False(OrderSplitService.CanSplit(order));
 }
}
