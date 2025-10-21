using Microsoft.AspNetCore.Http;

namespace SA_Project_API.Services
{
    public interface IImageUploadService
    {
        Task<string> SaveImageAsync(IFormFile file, string folder = "products");
        Task<List<string>> SaveImagesAsync(List<IFormFile> files, string folder = "products");
        Task<bool> DeleteImageAsync(string imageUrl);
        bool IsValidImage(IFormFile file);
    }

    public class ImageUploadService : IImageUploadService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ImageUploadService> _logger;
        private readonly long _maxFileSize = 5 * 1024 * 1024; // 5MB
        private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        public ImageUploadService(IWebHostEnvironment environment, ILogger<ImageUploadService> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        public bool IsValidImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return false;

            if (file.Length > _maxFileSize)
                return false;

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_allowedExtensions.Contains(extension))
                return false;

            return true;
        }

        public async Task<string> SaveImageAsync(IFormFile file, string folder = "products")
        {
            if (!IsValidImage(file))
                throw new ArgumentException("Invalid image file");

            try
            {
                // Create uploads directory if it doesn't exist
                var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads", folder);
                if (!Directory.Exists(uploadsPath))
                {
                    Directory.CreateDirectory(uploadsPath);
                }

                // Generate unique filename
                var extension = Path.GetExtension(file.FileName);
                var fileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadsPath, fileName);

                // Save file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Return relative URL
                var imageUrl = $"/uploads/{folder}/{fileName}";
                _logger.LogInformation($"Image saved: {imageUrl}");

                return imageUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving image");
                throw;
            }
        }

        public async Task<List<string>> SaveImagesAsync(List<IFormFile> files, string folder = "products")
        {
            var imageUrls = new List<string>();

            foreach (var file in files)
            {
                if (IsValidImage(file))
                {
                    var imageUrl = await SaveImageAsync(file, folder);
                    imageUrls.Add(imageUrl);
                }
            }

            return imageUrls;
        }

        public async Task<bool> DeleteImageAsync(string imageUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(imageUrl))
                    return false;

                // Extract file path from URL
                var relativePath = imageUrl.TrimStart('/');
                var filePath = Path.Combine(_environment.WebRootPath, relativePath);

                if (File.Exists(filePath))
                {
                    await Task.Run(() => File.Delete(filePath));
                    _logger.LogInformation($"Image deleted: {imageUrl}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting image: {imageUrl}");
                return false;
            }
        }
    }
}
