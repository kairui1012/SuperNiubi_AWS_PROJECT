using Microsoft.AspNetCore.Authentication.Google;

namespace MyMvcApp.Extensions;

public static class GoogleAuthenticationExtensions
{
    public static IServiceCollection AddGoogleLogin(this IServiceCollection services, IConfiguration configuration)
    {
        var googleClientId = configuration["Authentication:Google:ClientId"];
        var googleClientSecret = configuration["Authentication:Google:ClientSecret"];

        if (string.IsNullOrWhiteSpace(googleClientId) || string.IsNullOrWhiteSpace(googleClientSecret))
        {
            return services;
        }

        services.AddAuthentication()
            .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
            {
                options.ClientId = googleClientId;
                options.ClientSecret = googleClientSecret;
                options.SaveTokens = true;
            });

        return services;
    }
}
