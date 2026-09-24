using System.ComponentModel.DataAnnotations;
using Algara.Web.ViewModels;

namespace Algara.UnitTests.Validation;

public class CustomerRegistrationValidationTests
{
    [Theory]
    [InlineData("abcdef")]
    [InlineData("123456")]
    public void Registration_accepts_six_character_passwords_without_composition_rules(string password)
    {
        var registration = ValidRegistration();
        registration.Password = password;
        registration.ConfirmPassword = password;

        Assert.Empty(ValidationErrors(registration));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcde")]
    public void Registration_rejects_missing_or_short_passwords(string password)
    {
        var registration = ValidRegistration();
        registration.Password = password;
        registration.ConfirmPassword = password;

        var errors = ValidationErrors(registration);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(RegisterViewModel.Password)));
    }

    [Fact]
    public void Registration_rejects_a_different_password_confirmation()
    {
        var registration = ValidRegistration();
        registration.ConfirmPassword = "different-password";

        Assert.Single(ValidationErrors(registration));
    }

    [Theory]
    [InlineData(false, true, nameof(RegisterViewModel.AgeConfirmed))]
    [InlineData(true, false, nameof(RegisterViewModel.TermsAccepted))]
    public void Registration_requires_each_mandatory_confirmation(
        bool ageConfirmed, bool termsAccepted, string expectedMember)
    {
        var registration = ValidRegistration();
        registration.AgeConfirmed = ageConfirmed;
        registration.TermsAccepted = termsAccepted;

        var error = Assert.Single(ValidationErrors(registration));

        Assert.Contains(expectedMember, error.MemberNames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registration_accepts_either_marketing_preference(bool marketingConsent)
    {
        var registration = ValidRegistration();
        registration.MarketingConsent = marketingConsent;

        Assert.Empty(ValidationErrors(registration));
    }

    [Theory]
    [InlineData("")]
    [InlineData("customer.example.com")]
    public void Registration_requires_a_valid_email_address(string email)
    {
        var registration = ValidRegistration();
        registration.Email = email;

        var error = Assert.Single(ValidationErrors(registration));

        Assert.Contains(nameof(RegisterViewModel.Email), error.MemberNames);
    }

    [Theory]
    [InlineData("И", "Иванова", nameof(RegisterViewModel.FirstName))]
    [InlineData("Ива", "", nameof(RegisterViewModel.LastName))]
    public void Registration_rejects_incomplete_customer_names(
        string firstName, string lastName, string expectedMember)
    {
        var registration = ValidRegistration();
        registration.FirstName = firstName;
        registration.LastName = lastName;

        var error = Assert.Single(ValidationErrors(registration));

        Assert.Contains(expectedMember, error.MemberNames);
    }

    [Fact]
    public void Registration_rejects_a_name_exceeding_the_supported_length()
    {
        var registration = ValidRegistration();
        registration.FirstName = new string('А', 61);

        var error = Assert.Single(ValidationErrors(registration));

        Assert.Contains(nameof(RegisterViewModel.FirstName), error.MemberNames);
    }

    private static RegisterViewModel ValidRegistration() => new()
    {
        FirstName = "Ива",
        LastName = "Иванова",
        Email = "customer@example.com",
        PhoneNumber = null,
        Password = "abcdef",
        ConfirmPassword = "abcdef",
        AgeConfirmed = true,
        TermsAccepted = true,
        MarketingConsent = false
    };

    private static List<ValidationResult> ValidationErrors(object model)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), errors, validateAllProperties: true);
        return errors;
    }
}
