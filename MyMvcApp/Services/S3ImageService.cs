using Amazon.S3;
using Amazon.S3.Model;

namespace MyMvcApp.Services
{
    public interface IS3ImageService
    {
        Task<string> UploadImageAsync(IFormFile file);
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

        public async Task<string> UploadImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty or null.");

            var bucketName = _configuration["AWS:BucketName"];
            var region = _configuration["AWS:Region"];
            
            // Generate a unique filename to prevent overwriting
            var fileExtension = Path.GetExtension(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";

            var putRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = $"community-hub/{uniqueFileName}", // Creates a folder in your bucket called community-hub
                InputStream = file.OpenReadStream(),
                ContentType = file.ContentType
            };

            await _s3Client.PutObjectAsync(putRequest);

            // Construct and return the public URL
            return $"https://{bucketName}.s3.{region}.amazonaws.com/community-hub/{uniqueFileName}";
        }
    }
}