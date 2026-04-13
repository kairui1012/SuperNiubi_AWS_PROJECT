using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using MyMvcApp.Data;

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
            // 1. Prevent infinite loops / multiple DB calls
            if (principal.HasClaim(c => c.Type == "NeonDbRoleStamped"))
            {
                return Task.FromResult(principal);
            }

            var clone = principal.Clone();
            var mainIdentity = clone.Identity as ClaimsIdentity;

            // 2. Ensure the user is actually logged in
            if (mainIdentity != null && mainIdentity.IsAuthenticated)
            {
                // 3. Extract the username safely (AWS sometimes uses custom claim names)
                var username = clone.FindFirst("cognito:username")?.Value 
                            ?? clone.FindFirst(ClaimTypes.Name)?.Value 
                            ?? mainIdentity.Name;

                if (!string.IsNullOrEmpty(username))
                {
                    // 4. Look them up in Neon DB
                    var user = _dbContext.Users.FirstOrDefault(u => u.Nickname == username);
                    if (user != null)
                    {
                        // 5. THE FIX: Inject the role directly into the main AWS identity card.
                        // We stamp it three different ways to guarantee ASP.NET catches it!
                        mainIdentity.AddClaim(new Claim(mainIdentity.RoleClaimType ?? ClaimTypes.Role, user.Role));
                        mainIdentity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
                        mainIdentity.AddClaim(new Claim("cognito:groups", user.Role)); 

                        // Mark as processed
                        mainIdentity.AddClaim(new Claim("NeonDbRoleStamped", "true"));
                    }
                }
            }

            return Task.FromResult(clone);
        }
    }
}