using System.Text.Json;
using Amazon.Lambda.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyMvcApp.Data;
using MyMvcApp.Services;
using Stripe;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace MyMvcApp.Serverless
{
    public class Function
    {
        private readonly IServiceProvider _serviceProvider;

        public Function()
        {
            var configuration = BuildConfiguration();
            StripeConfiguration.ApiKey = configuration["Stripe:SecretKey"];

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging(logging => logging.AddConsole());
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
            services.AddScoped<EmailService>();
            services.AddScoped<StripeEventBridgeProcessingService>();

            _serviceProvider = services.BuildServiceProvider();
        }

        public async Task<StripeEventLambdaResponse> FunctionHandler(JsonElement payload, ILambdaContext context)
        {
            using var scope = _serviceProvider.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<StripeEventBridgeProcessingService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Function>>();
            var summary = StripeEventBridgeProcessingService.ReadEventSummary(payload);

            try
            {
                var result = await processor.ProcessEventBridgeEventAsync(payload);
                var response = StripeEventLambdaResponse.FromResult(result);

                if (result.IsSuccessStatusCode)
                {
                    logger.LogInformation("Processed Stripe EventBridge event {EventType} {EventId}.", summary.EventType, summary.EventId);
                }
                else
                {
                    logger.LogWarning(
                        "Stripe EventBridge event {EventType} {EventId} returned status {StatusCode}: {Message}.",
                        summary.EventType,
                        summary.EventId,
                        result.StatusCode,
                        result.Message);
                }

                return response;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process Stripe EventBridge event {EventType} {EventId}.", summary.EventType, summary.EventId);
                throw;
            }
        }

        private static IConfiguration BuildConfiguration()
        {
            return new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
        }
    }

    public sealed record StripeEventLambdaResponse(
        int StatusCode,
        string? EventId,
        string? EventType,
        string? Message,
        object? Body)
    {
        public static StripeEventLambdaResponse FromResult(StripeEventProcessResult result)
        {
            return new StripeEventLambdaResponse(
                result.StatusCode,
                result.Summary.EventId,
                result.Summary.EventType,
                result.Message,
                result.Body);
        }
    }
}
