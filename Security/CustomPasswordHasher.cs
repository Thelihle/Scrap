using Microsoft.AspNetCore.Identity;
using SCP_Control.Models;
using System.Security.Cryptography;
using System.Text;

public class CustomPasswordHasher : IPasswordHasher<User>
{
    // Configuration for password hashing
    private const int SaltSize = 16; // 128 bits
    private const int KeySize = 32; // 256 bits
    private const int Iterations = 10000; // PBKDF2 iteration count

    public string HashPassword(User user, string password)
    {
        // Generate a cryptographically strong random salt
        byte[] salt = new byte[SaltSize];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        // Derive the key using PBKDF2 with HMAC-SHA256
        byte[] hash = DeriveKey(Encoding.UTF8.GetBytes(password), salt);

        // Combine salt and hash
        byte[] hashBytes = new byte[SaltSize + KeySize];
        Array.Copy(salt, 0, hashBytes, 0, SaltSize);
        Array.Copy(hash, 0, hashBytes, SaltSize, KeySize);

        // Convert to base64 for storage
        return Convert.ToBase64String(hashBytes);
    }

    public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
    {
        // Decode the stored hash
        byte[] hashBytes;
        try
        {
            hashBytes = Convert.FromBase64String(hashedPassword);
        }
        catch
        {
            return PasswordVerificationResult.Failed;
        }

        // Extract salt and hash
        byte[] salt = new byte[SaltSize];
        byte[] storedHash = new byte[KeySize];
        Array.Copy(hashBytes, 0, salt, 0, SaltSize);
        Array.Copy(hashBytes, SaltSize, storedHash, 0, KeySize);

        // Derive key from provided password
        byte[] computedHash = DeriveKey(Encoding.UTF8.GetBytes(providedPassword), salt);

        // Compare computed hash with stored hash
        if (CryptographicOperations.FixedTimeEquals(computedHash, storedHash))
        {
            return PasswordVerificationResult.Success;
        }

        return PasswordVerificationResult.Failed;
    }

    private byte[] DeriveKey(byte[] password, byte[] salt)
    {
        using (var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256))
        {
            return pbkdf2.GetBytes(KeySize);
        }
    }
}