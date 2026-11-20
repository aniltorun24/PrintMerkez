using System.Security.Cryptography;
using System.Text;

namespace PrintMerkez.Web.Services;

// Temporary shared administrator account until the company's AD integration is ready.
public sealed class LocalAdminCredentials(IConfiguration configuration)
{
    public string Username => configuration["LocalAdmin:Username"] ?? "admin";

    public bool Verify(string username, string password)
    {
        var salt = Convert.FromBase64String(configuration["LocalAdmin:PasswordSalt"]
            ?? throw new InvalidOperationException("Yönetici hesabı yapılandırılmamış."));
        var expected = Convert.FromBase64String(configuration["LocalAdmin:PasswordHash"]
            ?? throw new InvalidOperationException("Yönetici hesabı yapılandırılmamış."));
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 210000,
            HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected)
            & string.Equals(username, Username, StringComparison.Ordinal);
    }
}
