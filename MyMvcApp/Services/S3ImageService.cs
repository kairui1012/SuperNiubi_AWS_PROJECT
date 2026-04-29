using Amazon.S3;
using Amazon.S3.Model;

namespace MyMvcApp.Services
{
    public interface IS3ImageService
    {
        Task<string> UploadImageAsync(IFormFile file, string folder = "community-hub");
    }

    public class S3ImageService : IS3ImageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly IConfiguration _configuration;

        public S3ImageService(IAmazonS3 s3Client, IConfiguration configuration)
        {
            _s3Client = s3Client;
            _configuration = configuration;
        }

        public async Task<string> UploadImageAsync(IFormFile file, string folder = "community-hub")
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty or null.");

            var bucketName = _configuration["AWS:BucketName"];
            var region = _configuration["AWS:Region"];
            var safeFolder = string.IsNullOrWhiteSpace(folder)
                ? "community-hub"
                : folder.Trim().Trim('/').Replace("\\", "/");
            
            // Generate a unique filename to prevent overwriting
            var fileExtension = Path.GetExtension(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
            var objectKey = $"{safeFolder}/{uniqueFileName}";

            var putRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = objectKey,
                InputStream = file.OpenReadStream(),
                ContentType = file.ContentType
            };

            await _s3Client.PutObjectAsync(putRequest);

            // Construct and return the public URL
            return $"https://{bucketName}.s3.{region}.amazonaws.com/{objectKey}";
        }
    }
}
