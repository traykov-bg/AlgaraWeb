using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Algara.Web.Services.Shipping;

public sealed class DeliverySecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("Algara.Delivery.Secret.v1");
    public string Protect(string value) => protector.Protect(value);
    public bool TryUnprotect(string? value, out string secret)
    {
        secret = string.Empty;
        if (string.IsNullOrEmpty(value)) return false;
        try
        {
            var decrypted = protector.Unprotect(value);
            if (string.IsNullOrWhiteSpace(decrypted) || decrypted.Any(char.IsControl)) return false;
            secret = decrypted;
            return true;
        }
        catch (CryptographicException) { return false; }
    }
}
