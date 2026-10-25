using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using Algara.Identity.Models;

namespace Algara.Web.Services;

public static class CustomerManagementRules
{
    public static bool CanManage(ApplicationUser user) => user.IsActive || user.AccessSuspendedAtUtc.HasValue;

    public static bool ValidReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 2000;

    public static string? Validate(CustomerProfileUpdate request)
    {
        request.FirstName = (request.FirstName ?? string.Empty).Trim();
        request.LastName = (request.LastName ?? string.Empty).Trim();
        request.PhoneNumber = Optional(request.PhoneNumber);
        return ValidName(request.FirstName) && ValidName(request.LastName) && ValidPhone(request.PhoneNumber)
            ? null : "Въведете име и фамилия до 100 знака и валиден телефон до 40 знака.";
    }

    public static string? Validate(CustomerAddressUpdate request)
    {
        request.FirstName = (request.FirstName ?? string.Empty).Trim();
        request.LastName = (request.LastName ?? string.Empty).Trim();
        request.PhoneNumber = Optional(request.PhoneNumber);
        request.Email = (request.Email ?? string.Empty).Trim();
        request.AddressLine1 = (request.AddressLine1 ?? string.Empty).Trim();
        request.AddressLine2 = Optional(request.AddressLine2);
        request.City = (request.City ?? string.Empty).Trim();
        request.PostalCode = Optional(request.PostalCode);
        request.Country = (request.Country ?? string.Empty).Trim();
        if (request.N is <= 0 || !ValidName(request.FirstName) || !ValidName(request.LastName) || !ValidPhone(request.PhoneNumber))
            return "Въведете име и фамилия до 100 знака и валиден телефон до 40 знака.";
        if (!ValidText(request.Email, 256) || !MailAddress.TryCreate(request.Email, out var email) || email.Address != request.Email)
            return "Въведете един валиден имейл адрес до 256 знака.";
        if (!ValidText(request.AddressLine1, 250) || !ValidOptional(request.AddressLine2, 250) ||
            !ValidText(request.City, 100) || !ValidOptional(request.PostalCode, 20) || !ValidText(request.Country, 100))
            return "Попълнете адрес до 250 знака, град и държава до 100 знака и пощенски код до 20 знака.";
        return null;
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool ValidName(string value) => ValidText(value, 100);
    private static bool ValidText(string value, int maximum) => value.Length is > 0 && value.Length <= maximum && !value.Any(char.IsControl);
    private static bool ValidOptional(string? value, int maximum) => value == null || ValidText(value, maximum);
    private static bool ValidPhone(string? value) => value == null ||
        (ValidText(value, 40) && new PhoneAttribute().IsValid(value));
}
