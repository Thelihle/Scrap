using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SCP_Control.Models;
using System.Threading.Tasks;

namespace SCP_Control.Extensions
{
    public static class UserManagerExtensions
    {
        /// <summary>
        /// Find a user by their clock number
        /// </summary>
        public static async Task<User> FindByClockNumberAsync(this UserManager<User> userManager, string clockNumber)
        {
            return await userManager.Users.FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);
        }

        /// <summary>
        /// Check if a user exists with the given clock number
        /// </summary>
        public static async Task<bool> ClockNumberExistsAsync(this UserManager<User> userManager, string clockNumber)
        {
            return await userManager.Users.AnyAsync(u => u.ClockNumber == clockNumber);
        }

        /// <summary>
        /// Custom password hasher that uses BCrypt instead of Identity's default hasher
        /// </summary>
        public class CustomPasswordHasher : IPasswordHasher<User>
        {
            public string HashPassword(User user, string password)
            {
                return BCrypt.Net.BCrypt.HashPassword(password);
            }

            public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
            {
                if (BCrypt.Net.BCrypt.Verify(providedPassword, hashedPassword))
                {
                    return PasswordVerificationResult.Success;
                }
                return PasswordVerificationResult.Failed;
            }
        }
    }
}