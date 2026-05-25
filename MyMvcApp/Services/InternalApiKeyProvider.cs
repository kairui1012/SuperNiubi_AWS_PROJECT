using System.Text;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace MyMvcApp.Services
{
    /// <summary>
    /// Resolves the shared internal API key used to authenticate trusted server-to-server callbacks.
    /// Lambda and EventBridge-facing workflows use this key when calling MVC internal endpoints,
    /// such as Stripe payment confirmation and S3 upload confirmation callbacks.
    /// CloudWatch logs key lookup failures, and SNS alarms can notify maintainers if callbacks fail repeatedly.
    /// </summary>
    public class InternalApiKeyProvider
    {
        private readonly IConfiguration _configuration;
        private readonly IAmazonSecretsManager _secretsManager;
        private readonly ILogger<InternalApiKeyProvider> _logger;

        /// <summary>
        /// Describes where the internal API key was loaded from and why lookup succeeded or failed.
        /// </summary>
        public sealed record InternalApiKeyLookupResult(string? Key, string Source, string Message)
        {
            public bool HasKey => !string.IsNullOrWhiteSpace(Key);
        }

        /// <summary>
        /// Creates the provider with configuration fallback, AWS Secrets Manager access, and logging.
        /// </summary>
        public InternalApiKeyProvider(
            IConfiguration configuration,
            IAmazonSecretsManager secretsManager,
            ILogger<InternalApiKeyProvider> logger)
        {
            _configuration = configuration;
            _secretsManager = secretsManager;
            _logger = logger;
        }

        /// <summary>
        /// Returns the internal API key value, or null when no configured source contains one.
        /// </summary>
        public async Task<string?> GetInternalApiKeyAsync()
        {
            return (await GetInternalApiKeyLookupAsync()).Key;
        }

        /// <summary>
        /// Loads the internal API key from app configuration, AWS Secrets Manager, or the legacy EventBridge shared secret.
        /// </summary>
        public async Task<InternalApiKeyLookupResult> GetInternalApiKeyLookupAsync()
        {
            var configuredKey = _configuration["InternalApi:Key"];
            if (!string.IsNullOrWhiteSpace(configuredKey))
            {
                return new InternalApiKeyLookupResult(configuredKey, "configuration:InternalApi:Key", "Loaded internal API key from MVC configuration.");
            }

            var failures = new List<string>();
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
                        return new InternalApiKeyLookupResult(secretValue, $"secrets-manager:{secretId}", "Loaded internal API key from Secrets Manager.");
                    }

                    failures.Add($"{secretId}: empty secret value");
                    _logger.LogWarning("Secrets Manager secret {SecretId} did not contain an internal API key value.", secretId);
                }
                catch (ResourceNotFoundException)
                {
                    failures.Add($"{secretId}: not found");
                    _logger.LogWarning("Secrets Manager secret {SecretId} was not found while loading the internal API key.", secretId);
                }
                catch (AmazonSecretsManagerException ex)
                {
                    failures.Add($"{secretId}: {ex.ErrorCode ?? ex.GetType().Name}");
                    _logger.LogError(ex, "Unable to load internal API key from Secrets Manager secret {SecretId}.", secretId);
                }
                catch (Exception ex)
                {
                    failures.Add($"{secretId}: {ex.GetType().Name}");
                    _logger.LogError(ex, "Unable to load internal API key from Secrets Manager secret {SecretId}.", secretId);
                }
            }

            var legacySecret = _configuration["EventBridge:SharedSecret"];
            if (!string.IsNullOrWhiteSpace(legacySecret))
            {
                return new InternalApiKeyLookupResult(legacySecret, "configuration:EventBridge:SharedSecret", "Loaded internal API key from legacy EventBridge shared secret.");
            }

            var message = failures.Count == 0
                ? "Internal API key is not configured. Checked configuration InternalApi:Key and legacy EventBridge:SharedSecret."
                : $"Internal API key is not configured. Checked configuration InternalApi:Key, Secrets Manager ({string.Join("; ", failures)}), and legacy EventBridge:SharedSecret.";

            return new InternalApiKeyLookupResult(null, "not-configured", message);
        }

        /// <summary>
        /// Lists the accepted Secrets Manager secret ids used during deployment migration.
        /// </summary>
        private static IEnumerable<string> GetSecretIds()
        {
            yield return "prod/mymvcapp/secrets";
            yield return "InternalApi__Key";
            yield return "InternalApi:Key";
        }

        /// <summary>
        /// Extracts a usable key from either a plain string secret or a JSON secret payload.
        /// </summary>
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

        /// <summary>
        /// Reads common JSON property names used to store the internal API key in Secrets Manager.
        /// </summary>
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
