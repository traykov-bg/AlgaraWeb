using Algara.Web.Services.Cart;
namespace Algara.UnitTests.Services;
public class AccountCartMergeTests
{
 [Fact]
 public void MergeAddsMatchingVariantsAndKeepsDifferentVariantsSeparate()
 {
  var merged=AccountCartStore.Merge([new(1,10,2),new(2,null,1)],[new(1,10,3),new(1,11,4)]);
  Assert.Equal(new[]{new CartEntry(1,10,5),new CartEntry(2,null,1),new CartEntry(1,11,4)},merged);
 }
 [Fact]
 public void MergePreservesExcessQuantityForExplicitCorrection()
 {
  var merged=AccountCartStore.Merge([new(1,null,99)],[new(1,null,99)]);
  Assert.Equal(198,Assert.Single(merged).Quantity);
  Assert.True(AccountCartStore.ValidIdentities(merged));
  Assert.False(ProtectedCookieCartStore.IsValid(merged));
 }
 [Fact]
 public void MergeDoesNotMutateInputOrSilentlyDropExtraLines()
 {
  var saved=Enumerable.Range(1,30).Select(n=>new CartEntry(n,null,1)).ToArray();
  var merged=AccountCartStore.Merge(saved,[new(31,null,1)]);
  Assert.Equal(30,saved.Length);Assert.Equal(31,merged.Count);
  Assert.False(new CartSnapshot{Lines=merged.Select(e=>new CartLine{Entry=e}).ToArray()}.CanQuote);
 }
 [Fact]
 public void MalformedEntriesCannotBecomeProfileCart()=>Assert.Throws<ArgumentException>(()=>AccountCartStore.Merge([],[new(-1,null,1)]));
}
