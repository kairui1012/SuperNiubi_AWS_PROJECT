using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using MyMvcApp.Data;
using System; // Required for Console

namespace MyMvcApp.Services
{
    public class RoleClaimsTransformation : IClaimsTransformation
    {
        private readonly AppDbContext _dbContext;

        public RoleClaimsTransformation(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.HasClaim(c => c.Type == "NeonDbRoleStamped"))
            {
                return Task.FromResult(principal);
            }

            var clone = principal.Clone();
            var mainIdentity = clone.Identity as ClaimsIdentity;

            if (mainIdentity != null && mainIdentity.IsAuthenticated)
            {
                var email = clone.FindFirst(ClaimTypes.Email)?.Value;

                Console.WriteLine($"\n[DEBUG] --> Attempting to authorize user by Email: '{email}'");

                if (!string.IsNullOrEmpty(email))
                {
                    var user = _dbContext.Users.FirstOrDefault(u => u.Email == email);
                    
                    if (user != null)
                    {
                        var cleanRole = user.Role.Trim(); // Removes accidental spaces from DB
                        Console.WriteLine($"[DEBUG] --> User found in DB! Role assigned: '{cleanRole}'");

                        // 2. THE FIX: Create a dedicated, explicitly authenticated Identity card 
                        // Passing a string ("NeonDbAuth") forces IsAuthenticated = true!
                        var roleIdentity = new ClaimsIdentity("NeonDbAuth", ClaimTypes.Name, ClaimTypes.Role);
                        roleIdentity.AddClaim(new Claim(ClaimTypes.Role, cleanRole));
                        roleIdentity.AddClaim(new Claim("NeonDbRoleStamped", "true"));

                        clone.AddIdentity(roleIdentity);
                        Console.WriteLine($"[DEBUG] --> Success: Role stamped onto Identity.\n");
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] --> FAILED: User '{email}' was NOT found in the Neon Database!\n");
                    }
                }
            }

            return Task.FromResult(clone);
        }
    }
}