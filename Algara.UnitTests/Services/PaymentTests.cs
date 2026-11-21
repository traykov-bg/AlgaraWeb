using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Web.Services.Payments;
namespace Algara.UnitTests.Services;
public class PaymentTests
{
 [Theory]
 [InlineData(0,false,false,false)] [InlineData(1,false,false,true)]
 [InlineData(2,false,false,false)] [InlineData(5,false,false,false)]
 [InlineData(0,true,false,true)] [InlineData(0,false,true,true)]
 public void DepositDependsOnTransportAndManufacture(int method,bool individual,bool manufacturing,bool expected)
 {
  var o=new Order{DeliverySnapshot="{\"Method\":"+method+"}",OrderItems=[new OrderItem{IndividualManufacture=individual,RequiresManufacturing=manufacturing}]};
  Assert.Equal(expected,PaymentRules.RequiresDeposit(o));
 }
 [Fact] public void MinimumDepositRoundsUpToCentAndExemptionIsIndependentOfCredit()
 {
  var o=new Order{TotalAmount=100.01m,DepositPercent=50,DepositRequired=true,DeferredPaymentDays=30};
  Assert.Equal(50.01m,PaymentRules.Deposit(o,false));Assert.Equal(0m,PaymentRules.Deposit(o,true));
  o.DepositRequired=false;Assert.Equal(0m,PaymentRules.Deposit(o,false));
 }
 [Theory]
 [InlineData("12,34",true)] [InlineData("12.34",true)] [InlineData("1,000.00",false)] [InlineData("-20",false)] [InlineData("1e3",false)]
 public void AmountDoesNotInterpretThousandsOrExponent(string text,bool expected)=>Assert.Equal(expected,PaymentService.Parse(text,out _));
 [Theory]
 [InlineData(0,false)] [InlineData(-1,false)] [InlineData(1.001,false)] [InlineData(0.01,true)] [InlineData(1000000000,false)]
 public void MoneyHasBoundedPositiveCents(decimal value,bool expected)=>Assert.Equal(expected,PaymentRules.Money(value));
 [Fact] public void OverdueStartsAfterDueDateAndCancelledFundsNeedSettlement()
 {
  var today=new DateOnly(2026,11,12);var o=new Order{TotalAmount=100,PaymentDueOn=today,Status=OrderStatus.Delivered};
  Assert.Equal("Partial",PaymentRules.State(o,50,today));Assert.Equal("Overdue",PaymentRules.State(o,50,today.AddDays(1)));Assert.Equal("Paid",PaymentRules.State(o,100,today.AddDays(1)));
  o.Status=OrderStatus.Cancelled;Assert.Equal("RefundDue",PaymentRules.State(o,50,today));Assert.Equal("Cancelled",PaymentRules.State(o,0,today));
 }
 [Theory]
 [InlineData(StaffRoles.Moderator)] [InlineData(StaffRoles.CatalogEditor)] [InlineData(StaffRoles.OrderManager)]
 public void PaymentsRequireExplicitGrants(string role)
 {
  foreach(var key in new[]{PermissionCatalog.Payments.View,PermissionCatalog.Payments.Record,PermissionCatalog.Payments.Correct,PermissionCatalog.Payments.Override})Assert.DoesNotContain(key,PermissionCatalog.DefaultGrants(role));
 }
}
