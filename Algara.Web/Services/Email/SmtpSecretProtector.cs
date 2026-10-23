using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Algara.Web.Services.Email;

public sealed class SmtpSecretProtector(IDataProtectionProvider provider) : ISmtpSecretProtector
{
    // Reuse the existing persisted key ring without changing authentication-cookie protection.
    private readonly IDataProtector protector = provider.CreateProtector("Algara.Smtp.Password.v1");

    public string Protect(string password) => protector.Protect(password);

    public bool TryUnprotect(string protectedPassword, out string password)
    {
        try
        {
            password = protector.Unprotect(protectedPassword);
            return true;
        }
        catch (CryptographicException)
        {
            password = string.Empty;
            return false;
        }
    }
}
