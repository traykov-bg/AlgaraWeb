using System.ComponentModel.DataAnnotations;
using Algara.Web.ViewModels;

namespace Algara.UnitTests.Validation;

public class CustomerPasswordChangeValidationTests
{
    [Theory]
    [InlineData("abcdef")]
    [InlineData("123456")]
    public void Password_change_preserves_the_simple_six_character_customer_policy(string newPassword)
    {
        var model = ValidPasswordChange();
        model.NewPassword = newPassword;
        model.ConfirmPassword = newPassword;

        Assert.Empty(ValidationErrors(model));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(101)]
    public void Password_change_rejects_a_new_password_outside_supported_lengths(int length)
    {
        var model = ValidPasswordChange();
        model.NewPassword = new string('a', length);
        model.ConfirmPassword = model.NewPassword;

        var error = Assert.Single(ValidationErrors(model));

        Assert.Contains(nameof(ChangePasswordViewModel.NewPassword), error.MemberNames);
    }

    [Fact]
    public void Password_change_requires_the_current_password()
    {
        var model = ValidPasswordChange();
        model.CurrentPassword = string.Empty;

        var error = Assert.Single(ValidationErrors(model));

        Assert.Contains(nameof(ChangePasswordViewModel.CurrentPassword), error.MemberNames);
    }

    [Fact]
    public void Password_change_rejects_a_different_confirmation()
    {
        var model = ValidPasswordChange();
        model.ConfirmPassword = "different-password";

        Assert.Single(ValidationErrors(model));
    }

    private static ChangePasswordViewModel ValidPasswordChange() => new()
    {
        CurrentPassword = "old-password",
        NewPassword = "abcdef",
        ConfirmPassword = "abcdef"
    };

    private static List<ValidationResult> ValidationErrors(object model)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), errors, validateAllProperties: true);
        return errors;
    }
}
