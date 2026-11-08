using Algara.Data.Models;
namespace Algara.Web.Services.Checkout;
public static class OrderSplitQuantities
{
 public static OrderItem Transfer(OrderItem source,int quantity)
 {
  if(quantity<=0 || quantity>source.Quantity || quantity>OrderSplitService.ReadyQuantity(source)) throw new ArgumentOutOfRangeException(nameof(quantity));
  var deducted=Math.Min(quantity,source.DeductedQuantity);var reserved=Math.Min(quantity,source.ReservedQuantity);
  var child=new OrderItem{ProductN=source.ProductN,VariantN=source.VariantN,ProductName=source.ProductName,VariantName=source.VariantName,Sku=source.Sku,
   Quantity=quantity,UnitPrice=source.UnitPrice,ReservedQuantity=reserved,DeductedQuantity=deducted,RequiresManufacturing=false};
  source.Quantity-=quantity;source.DeductedQuantity-=deducted;source.ReservedQuantity-=reserved;
  return child;
 }
}
