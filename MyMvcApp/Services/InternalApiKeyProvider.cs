using System.Text;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace MyMvcApp.Services
{
    public class InternalApiKeyProvider
    {
        private readonly IConfiguration _configuration;
        private readonly IAmazonSecretsManager _secretsManager;
        private readonly ILogger<InternalApiKeyProvider> _logger;

        public InternalApiKeyProvider(
            IConfiguration configuration,
            IAmazonSecretsManager secretsManager,
            ILogger<InternalApiKeyProvider> logger)
        {
            _configuration = configuration;
            _secretsManager = secretsManager;
            _logger = logger;
        }

        public async Task<string?> GetInternalApiKeyAsync()
        {
            var configuredKey = _configuration["InternalApi:Key"];
            if (!string.IsNullOrWhiteSpace(configuredKey))
            {
                return configuredKey;
            }

            foreach (var secretId in GetSecretIds())
            {
                try
                {
                    var response = await _secretsManager.GetSecretValueAsync(new GetSecretValueRequest
                    {
                        SecretId = secretId
                    });

                    var secretValue = ExtractSecretValue(response);
                    if (!string.IsNullOrWhiteSpace(secretValue))
                    {
                        return secretValue;
                    }

                    _logger.LogWarning("Secrets Manager secret {SecretId} did not contain an internal API key value.", secretId);
                }
                catch (ResourceNotFoundException)
                {
                    _logger.LogWarning("Secrets Manager secret {SecretId} was not found while loading the internal API key.", secretId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to load internal API key from Secrets Manager secret {SecretId}.", secretId);
                }
            }

            return _configuration["EventBridge:SharedSecret"];
        }

        private static IEnumerable<string> GetSecretIds()
        {
            yield return "InternalApi__Key";
        }

        private static string? ExtractSecretValue(GetSecretValueResponse response)
        {
            var secret = response.SecretString;
            if (string.IsNullOrWhiteSpace(secret) && response.SecretBinary is not null)
            {
                secret = Encoding.UTF8.GetString(response.SecretBinary.ToArray());
            }

            if (string.IsNullOrWhiteSpace(secret))
            {
                return null;
            }

            return TryReadJsonSecretValue(secret) ?? secret;
        }

        private static string? TryReadJsonSecretValue(string secret)
        {
            try
            {
                using var document = JsonDocument.Parse(secret);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                foreach (var key in new[] { "Key", "key", "InternalApiKey", "InternalApi__Key", "InternalApi:Key", "INTERNAL_API_KEY", "value" })
                {
                    if (document.RootElement.TryGetProperty(key, out var property) &&
                        property.ValueKind == JsonValueKind.String)
                    {
                        var value = property.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }

                if (document.RootElement.TryGetProperty("InternalApi", out var internalApi) &&
                    internalApi.ValueKind == JsonValueKind.Object &&
                    internalApi.TryGetProperty("Key", out var nestedKey) &&
                    nestedKey.ValueKind == JsonValueKind.String)
                {
                    var value = nestedKey.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }
    }
}
