using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using TestExecutionPlatform.Core.Services.Containers;

namespace TestExecutionPlatform.Core.Services
{
    public class TestExecutionService
    {
        private readonly IKubernetesProvider _kubernetesProvider;
        private readonly NamespaceManager _namespaceManager;
        private readonly string _containerRegistry;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<TestExecutionService> _logger;

        public TestExecutionService(
            IKubernetesProvider kubernetesProvider,
            NamespaceManager namespaceManager,
            string containerRegistry,
            TelemetryClient telemetryClient,
            ILogger<TestExecutionService> logger)
        {
            _kubernetesProvider = kubernetesProvider;
            _namespaceManager = namespaceManager;
            _containerRegistry = containerRegistry;
            _telemetryClient = telemetryClient;
            _logger = logger;
        }

        public async Task<string> CreateTestJobAsync(string repoUrl, string testImageType, string lobId)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string namespaceName = _namespaceManager.GetNamespaceForLob(lobId);

            _logger.LogInformation($"Creating test job in namespace {namespaceName} for repo {repoUrl} with image {testImageType}");

            // Ensure namespace exists
            await _namespaceManager.EnsureNamespaceExistsAsync(lobId);

            string imageName = $"{_containerRegistry}/{testImageType.ToLower()}:latest";
            string jobName = $"test-job-{Guid.NewGuid():N}";

            try
            {
                string createdJobName = await _kubernetesProvider.CreateTestJobAsync(
                    imageName, jobName, repoUrl, namespaceName);

                stopwatch.Stop();

                // Track metrics
                _telemetryClient.TrackMetric("TestJobCreationDuration", stopwatch.ElapsedMilliseconds);
                _telemetryClient.TrackEvent("TestJobCreated",
                    new Dictionary<string, string> {
                        { "Namespace", namespaceName },
                        { "ImageType", testImageType },
                        { "LobId", lobId }
                    });

                _logger.LogInformation($"Created test job {createdJobName} successfully in {stopwatch.ElapsedMilliseconds}ms");

                return createdJobName;
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex,
                    new Dictionary<string, string> {
                        { "Namespace", namespaceName },
                        { "ImageType", testImageType },
                        { "LobId", lobId }
                    });

                _logger.LogError(ex, $"Error creating test job for repo {repoUrl} with image {testImageType}");
                throw;
            }
        }

        public async Task<bool> IsJobCompletedAsync(string jobName, string lobId)
        {
            string namespaceName = _namespaceManager.GetNamespaceForLob(lobId);
            return await _kubernetesProvider.IsJobCompletedAsync(jobName, namespaceName);
        }

        public async Task<string> GetTestResultsAsync(string jobName, string lobId)
        {
            string namespaceName = _namespaceManager.GetNamespaceForLob(lobId);
            return await _kubernetesProvider.GetJobLogsAsync(jobName, namespaceName);
        }

        public async Task CleanupTestJobAsync(string jobName, string lobId)
        {
            string namespaceName = _namespaceManager.GetNamespaceForLob(lobId);
            _logger.LogInformation($"Cleaning up test job {jobName} in namespace {namespaceName}");
            await _kubernetesProvider.DeleteJobAsync(jobName, namespaceName);
        }
    }
}