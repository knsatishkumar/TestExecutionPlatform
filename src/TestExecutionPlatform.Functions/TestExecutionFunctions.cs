using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Security.Claims;
using TestExecutionPlatform.Core.Services;
using TestExecutionPlatform.Core.Models;

namespace TestExecutionPlatform.Functions
{
    public class TestExecutionFunctions
    {
        private readonly TestExecutionService _testExecutionService;
        private readonly JobTrackingService _jobTrackingService;
        private readonly ILogger<TestExecutionFunctions> _logger;

        public TestExecutionFunctions(
            TestExecutionService testExecutionService,
            JobTrackingService jobTrackingService,
            ILogger<TestExecutionFunctions> logger)
        {
            _testExecutionService = testExecutionService;
            _jobTrackingService = jobTrackingService;
            _logger = logger;
        }

        [FunctionName("CreateAndRunTestJob")]
        public async Task<IActionResult> CreateAndRunTestJob(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation("Creating and running test job.");

            // Extract LOB ID and Team ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;
            string teamId = claimsPrincipal.FindFirst("team_id")?.Value;
            string userId = claimsPrincipal.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(lobId) || string.IsNullOrEmpty(teamId))
            {
                return new UnauthorizedResult();
            }

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            TestJobRequest data;

            try
            {
                data = JsonConvert.DeserializeObject<TestJobRequest>(requestBody);

                if (data == null || string.IsNullOrEmpty(data.RepoUrl) || string.IsNullOrEmpty(data.TestImageType))
                {
                    return new BadRequestObjectResult("Please provide repoUrl and testImageType in the request body");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializing request body");
                return new BadRequestObjectResult("Invalid request format");
            }

            try
            {
                // Create job record in database
                string jobId = await _jobTrackingService.CreateJobAsync(lobId, teamId, data.RepoUrl, data.TestImageType, userId);

                // Create the job in Kubernetes
                string k8sJobName = await _testExecutionService.CreateTestJobAsync(data.RepoUrl, data.TestImageType, lobId);

                return new OkObjectResult(new
                {
                    jobId = jobId,
                    message = $"Test job created and running: {k8sJobName}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating test job");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("GetTestJobStatus")]
        public async Task<IActionResult> GetTestJobStatus(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "jobs/{jobId}")] HttpRequest req,
            string jobId,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation($"Getting status for job {jobId}.");

            // Extract LOB ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;

            if (string.IsNullOrEmpty(lobId))
            {
                return new UnauthorizedResult();
            }

            try
            {
                bool isCompleted = await _testExecutionService.IsJobCompletedAsync(jobId, lobId);
                string status = isCompleted ? "Completed" : "Running";

                return new OkObjectResult(new
                {
                    jobId = jobId,
                    status = status
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting job status for {jobId}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("GetTestResults")]
        public async Task<IActionResult> GetTestResults(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "jobs/{jobId}/results")] HttpRequest req,
            string jobId,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation($"Getting test results for job {jobId}.");

            // Extract LOB ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;

            if (string.IsNullOrEmpty(lobId))
            {
                return new UnauthorizedResult();
            }

            try
            {
                bool isCompleted = await _testExecutionService.IsJobCompletedAsync(jobId, lobId);
                if (!isCompleted)
                {
                    return new OkObjectResult(new
                    {
                        jobId = jobId,
                        status = "Running",
                        message = "Test job is still running"
                    });
                }

                string results = await _testExecutionService.GetTestResultsAsync(jobId, lobId);
                return new OkObjectResult(new
                {
                    jobId = jobId,
                    status = "Completed",
                    results = results
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting test results for job {jobId}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("CleanupTestJob")]
        public async Task<IActionResult> CleanupTestJob(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "jobs/{jobId}/cleanup")] HttpRequest req,
            string jobId,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation($"Cleaning up job {jobId}.");

            // Extract LOB ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;

            if (string.IsNullOrEmpty(lobId))
            {
                return new UnauthorizedResult();
            }

            try
            {
                await _testExecutionService.CleanupTestJobAsync(jobId, lobId);
                return new OkObjectResult(new
                {
                    jobId = jobId,
                    message = $"Test job {jobId} cleaned up successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cleaning up job {jobId}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("ProcessCleanupQueue")]
        public async Task ProcessCleanupQueue(
            [QueueTrigger("cleanup-queue")] CleanupQueueItem queueItem,
            ILogger log)
        {
            log.LogInformation($"Processing cleanup for job: {queueItem.JobId} in LOB: {queueItem.LobId}");

            try
            {
                await _testExecutionService.CleanupTestJobAsync(queueItem.JobId, queueItem.LobId);
                log.LogInformation($"Cleanup completed for job: {queueItem.JobId}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error cleaning up job {queueItem.JobId}");
            }
        }

        public class CleanupQueueItem
        {
            public string JobId { get; set; }
            public string LobId { get; set; }
        }
    }
}