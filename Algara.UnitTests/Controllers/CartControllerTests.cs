using Algara.Data.Repositories;
using Algara.Web.Controllers;
using Algara.Web.Services.Cart;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;

namespace Algara.UnitTests.Controllers;

public class CartControllerTests
{
    [Theory]
    [InlineData(nameof(OrderController.Add))]
    [InlineData(nameof(OrderController.Update))]
    [InlineData(nameof(OrderController.Remove))]
    [InlineData(nameof(OrderController.Clear))]
    public void EveryCartMutationRequiresPostAndAntiforgery(string action)
    {
        var method = typeof(OrderController).GetMethod(action)!;
        Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), false));
        Assert.Single(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false));
    }

    [Fact]
    public async Task BadModelBindingDoesNotChangeTheCart()
    {
        var store = new Mock<ICartStore>(MockBehavior.Strict);
        var controller = Controller(store.Object);
        controller.ModelState.AddModelError("quantity", "Invalid value");
        Assert.IsType<RedirectToActionResult>(await controller.Add(1, null, 1));
        Assert.Equal("IssueInvalidQuantity", controller.TempData["CartMessage"]);
        Assert.IsType<RedirectToActionResult>(await controller.Update(1, null, 1));
        Assert.IsType<RedirectToActionResult>(controller.Remove(1, null));
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IndexDoesNotWriteCartState()
    {
        var store = new Mock<ICartStore>(MockBehavior.Strict);
        store.Setup(s => s.Read()).Returns(Array.Empty<CartEntry>());
        var result = Assert.IsType<ViewResult>(await Controller(store.Object).Index());
        Assert.Empty(Assert.IsType<CartSnapshot>(result.Model).Lines);
        store.Verify(s => s.Read(), Times.Once);
        store.VerifyNoOtherCalls();
    }

    private static OrderController Controller(ICartStore store)
    {
        var context = new DefaultHttpContext();
        return new OrderController(new CartService(Mock.Of<IProductRepository>(MockBehavior.Strict), store, TimeProvider.System))
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, Mock.Of<ITempDataProvider>())
        };
    }
}
