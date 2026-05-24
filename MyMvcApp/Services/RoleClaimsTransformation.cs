using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using MyMvcApp.Data;
using Microsoft.EntityFrameworkCore;

namespace MyMvcApp.Services
{
    public class RoleClaimsTransformation : IClaimsTransformation
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<RoleClaimsTransformation> _logger;

        public RoleClaimsTransformation(AppDbContext dbContext, ILogger<RoleClaimsTransformation> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.HasClaim(c => c.Type == "NeonDbRoleStamped"))
            {
                return principal;
            }

            var clone = principal.Clone();
            var mainIdentity = clone.Identity as ClaimsIdentity;

            if (mainIdentity != null && mainIdentity.IsAuthenticated)
            {
                var email = clone.FindFirst(ClaimTypes.Email)?.Value
                    ?? clone.FindFirst("email")?.Value;
                var normalizedEmail = email?.Trim().ToLowerInvariant();

                _logger.LogDebug("Attempting to authorize user by email {Email}.", email);

                if (!string.IsNullOrEmpty(normalizedEmail))
                {
                    try
                    {
                        var user = await _dbContext.Users
                            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

                        if (user != null)
                        {
                            var cleanRole = user.Role.Trim();
                            _logger.LogDebug("User found in database. Role assigned: {Role}.", cleanRole);

                            var roleIdentity = new ClaimsIdentity("NeonDbAuth", ClaimTypes.Name, ClaimTypes.Role);
                            roleIdentity.AddClaim(new Claim(ClaimTypes.Role, cleanRole));
                            roleIdentity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
                            roleIdentity.AddClaim(new Claim("email", user.Email));
                            roleIdentity.AddClaim(new Claim("NeonDbRoleStamped", "true"));

                            clone.AddIdentity(roleIdentity);
                            _logger.LogDebug("Role stamped onto identity.");
                        }
                        else
                        {
                            _logger.LogWarning("Authenticated user {Email} was not found in the application database.", email);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Could not load role claims for {Email} because the application database is unavailable.", email);
                    }
                }
            }

            return clone;
        }
    }
}
