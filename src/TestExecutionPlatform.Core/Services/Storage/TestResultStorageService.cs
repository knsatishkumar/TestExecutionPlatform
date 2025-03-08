using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace TestExecutionPlatform.Core.Services.Storage
{
    public class TestResultStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _containerName;
        private readonly ILogger _logger;
        private readonly ConfigurationService _configService;

        public TestResultStorageService(
            string connectionString,
            string containerName,
            ILogger logger,
            ConfigurationService configService)
        {
            _blobServiceClient = new BlobServiceClient(connectionString);
            _containerName = containerName;
            _logger = logger;
            _configService = configService;

            // Ensure container exists
            EnsureContainerExistsAsync().GetAwaiter().GetResult();
        }

        private async Task EnsureContainerExistsAsync()
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating blob container {_containerName}");
                throw;
            }
        }

        public async Task StoreTestResultsAsync(string lobId, string teamId, string jobId, Stream resultsContent, string fileName)
        {
            try
            {
                // Get admin configuration for max file size
                var adminConfig = await _configService.GetAdminConfigurationAsync();
                int maxFileSizeMB = adminConfig.Retention.MaxTestResultFileSizeMB;
                long maxFileSizeBytes = maxFileSizeMB * 1024 * 1024;

                // Check if the file size exceeds the limit
                if (resultsContent.Length > maxFileSizeBytes)
                {
                    throw new ArgumentException($"Test results file size ({resultsContent.Length / (1024 * 1024)} MB) exceeds the maximum allowed size ({maxFileSizeMB} MB)");
                }

                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

                // Create path with hierarchy: lob/team/jobId/filename
                string blobPath = $"{lobId}/{teamId}/{jobId}/{fileName}";
                var blobClient = containerClient.GetBlobClient(blobPath);

                // Upload the content
                await blobClient.UploadAsync(resultsContent, true);

                _logger.LogInformation($"Stored test results for job {jobId} at {blobPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error storing test results for job {jobId}");
                throw;
            }
        }

        public async Task<Stream> GetTestResultsAsync(string lobId, string teamId, string jobId, string fileName)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

                string blobPath = $"{lobId}/{teamId}/{jobId}/{fileName}";
                var blobClient = containerClient.GetBlobClient(blobPath);

                var response = await blobClient.DownloadAsync();

                _logger.LogInformation($"Retrieved test results for job {jobId} from {blobPath}");

                return response.Value.Content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving test results for job {jobId}");
                throw;
            }
        }

        public async Task<bool> DeleteTestResultsAsync(string lobId, string teamId, string jobId)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

                string prefix = $"{lobId}/{teamId}/{jobId}/";
                var blobs = containerClient.GetBlobsAsync(prefix: prefix);

                int deletedCount = 0;
                await foreach (var blob in blobs)
                {
                    var blobClient = containerClient.GetBlobClient(blob.Name);
                    await blobClient.DeleteIfExistsAsync();
                    deletedCount++;
                }

                _logger.LogInformation($"Deleted {deletedCount} test result files for job {jobId}");

                return deletedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting test results for job {jobId}");
                throw;
            }
        }

        public async Task<IEnumerable<string>> ListTestResultsAsync(string lobId, string teamId, string jobId)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

                string prefix = $"{lobId}/{teamId}/{jobId}/";
                var blobs = containerClient.GetBlobsAsync(prefix: prefix);

                var fileNames = new List<string>();
                await foreach (var blob in blobs)
                {
                    // Extract just the filename part
                    string fullPath = blob.Name;
                    string fileName = fullPath.Substring(prefix.Length);
                    fileNames.Add(fileName);
                }

                return fileNames;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error listing test results for job {jobId}");
                throw;
            }
        }

        public async Task CleanupOldTestResultsAsync()
        {
            try
            {
                // Get admin configuration for retention settings
                var adminConfig = await _configService.GetAdminConfigurationAsync();
                int retentionDays = adminConfig.Retention.TestResultsRetentionDays;

                // Calculate cutoff date
                var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

                _logger.LogInformation($"Cleaning up test results older than {cutoffDate} (retention: {retentionDays} days)");

                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

                // Get all blobs in the container
                var blobs = containerClient.GetBlobsAsync();
                int deletedCount = 0;

                await foreach (var blob in blobs)
                {
                    // Check if the blob is older than the retention period
                    if (blob.Properties.CreatedOn.HasValue && blob.Properties.CreatedOn.Value.UtcDateTime < cutoffDate)
                    {
                        var blobClient = containerClient.GetBlobClient(blob.Name);
                        await blobClient.DeleteIfExistsAsync();
                        deletedCount++;
                    }
                }

                _logger.LogInformation($"Deleted {deletedCount} test result files based on retention policy");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up old test results");
                throw;
            }
        }
    }
}