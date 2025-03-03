using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TestExecutionPlatform.Core.Services;
using System.Security.Claims;
using System.IO;
using Newtonsoft.Json;
using TestExecutionPlatform.Core.Services.Monitoring;
using TestExecutionPlatform.Core.Configuration;

namespace TestExecutionPlatform.Functions
{
    public class AdminFunctions
    {
        private readonly ReportingService _reportingService;
        private readonly AlertService _alertService;
        private readonly ILogger<AdminFunctions> _logger;

        public AdminFunctions(
            ReportingService reportingService,
            AlertService alertService,
            ILogger<AdminFunctions> logger)
        {
            _reportingService = reportingService;
            _alertService = alertService;
            _logger = logger;
        }

        [FunctionName("GetJobs")]
        public async Task<IActionResult> GetJobs(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/jobs")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation("Getting jobs list.");

            // Check for admin role
            if (!claimsPrincipal.IsInRole("TestExecutionAdmin"))
            {
                return new UnauthorizedResult();
            }

            // Parse query parameters
            string lobId = req.Query["lobId"];
            string teamId = req.Query["teamId"];
            string jobId = req.Query["jobId"];
            string status = req.Query["status"];

            DateTime? startDate = null;
            if (DateTime.TryParse(req.Query["startDate"], out var parsedStartDate))
            {
                startDate = parsedStartDate;
            }

            DateTime? endDate = null;
            if (DateTime.TryParse(req.Query["endDate"], out var parsedEndDate))
            {
                endDate = parsedEndDate;
            }

            int pageSize = 50;
            if (int.TryParse(req.Query["pageSize"], out var parsedPageSize) && parsedPageSize > 0)
            {
                pageSize = parsedPageSize;
            }

            int pageNumber = 1;
            if (int.TryParse(req.Query["page"], out var parsedPageNumber) && parsedPageNumber > 0)
            {
                pageNumber = parsedPageNumber;
            }

            // Get jobs
            var jobs = await _reportingService.GetJobsAsync(
                lobId, teamId, jobId, startDate, endDate, status, pageSize, pageNumber);

            // Get total count for pagination
            var totalCount = await _reportingService.GetJobsCountAsync(
                lobId, teamId, jobId, startDate, endDate, status);

            // Return paginated result
            return new OkObjectResult(new
            {
                items = jobs,
                totalCount = totalCount,
                pageSize = pageSize,
                pageNumber = pageNumber,
                totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            });
        }

        [FunctionName("GetJobsSummary")]
        public async Task<IActionResult> GetJobsSummary(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/jobs/summary")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation("Getting jobs summary.");

            // Check for admin role
            if (!claimsPrincipal.IsInRole("TestExecutionAdmin"))
            {
                return new UnauthorizedResult();
            }

            // Parse query parameters
            string lobId = req.Query["lobId"];

            DateTime? startDate = null;
            if (DateTime.TryParse(req.Query["startDate"], out var parsedStartDate))
            {
                startDate = parsedStartDate;
            }

            DateTime? endDate = null;
            if (DateTime.TryParse(req.Query["endDate"], out var parsedEndDate))
            {
                endDate = parsedEndDate;
            }

            // Get summary
            var summary = await _reportingService.GetExecutionSummaryAsync(lobId, startDate, endDate);

            return new OkObjectResult(summary);
        }

        [FunctionName("GetLobsSummary")]
        public async Task<IActionResult> GetLobsSummary(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/lobs/summary")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation("Getting LOBs summary.");

            // Check for admin role
            if (!claimsPrincipal.IsInRole("TestExecutionAdmin"))
            {
                return new UnauthorizedResult();
            }

            // Parse query parameters
            DateTime? startDate = null;
            if (DateTime.TryParse(req.Query["startDate"], out var parsedStartDate))
            {
                startDate = parsedStartDate;
            }

            DateTime? endDate = null;
            if (DateTime.TryParse(req.Query["endDate"], out var parsedEndDate))
            {
                endDate = parsedEndDate;
            }

            // Get LOB summary
            var summary = await _reportingService.GetLobExecutionSummaryAsync(startDate, endDate);

            return new OkObjectResult(summary);
        }

        [FunctionName("GetTopFailingTests")]
        public async Task<IActionResult> GetTopFailingTests(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/tests/failing")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation("Getting top failing tests.");

            // Check for admin role
            if (!claimsPrincipal.IsInRole("TestExecutionAdmin"))
            {
                return new UnauthorizedResult();
            }

            // Parse query parameters
            string lobId = req.Query["lobId"];
            string teamId = req.Query["teamId"];

            DateTime? startDate = null;
            if (DateTime.TryParse(req.Query["startDate"], out var parsedStartDate))
            {
                startDate = parsedStartDate;
            }

            DateTime? endDate = null;
            if (DateTime.TryParse(req.Query["endDate"], out var parsedEndDate))
            {
                endDate = parsedEndDate;
            }

            int limit = 10;
            if (int.TryParse(req.Query["limit"], out var parsedLimit) && parsedLimit > 0)
            {
                limit = parsedLimit;
            }

            // Get failing tests
            var failingTests = await _reportingService.GetTopFailingTestsAsync(
                lobId, teamId, startDate, endDate, limit);

            return new OkObjectResult(failingTests);
        }

        [FunctionName("TriggerAlert")]
        public async Task<IActionResult> TriggerAlert(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/alerts/test")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation("Manually triggering test alert");

            // Check for admin role
            if (!claimsPrincipal.IsInRole("TestExecutionAdmin"))
            {
                return new UnauthorizedResult();
            }

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);

                string title = data?.title ?? "Manual Test Alert";
                string message = data?.message ?? "This is a test alert triggered manually";
                string severityString = data?.severity ?? "Information";

                AlertSeverity severity;
                if (!Enum.TryParse(severityString, true, out severity))
                {
                    severity = AlertSeverity.Information;
                }

                await _alertService.SendNotificationAsync(title, message, severity, null);

                return new OkObjectResult(new { message = "Test alert sent successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering test alert");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("AdminPage")]
        public static IActionResult AdminPage(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal,
            ILogger log)
        {
            log.LogInformation("Serving admin page.");

            // Check for admin role
            if (!claimsPrincipal.IsInRole("TestExecutionAdmin"))
            {
                return new UnauthorizedResult();
            }

            string htmlContent = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "www", "admin.html"));
            return new ContentResult
            {
                Content = htmlContent,
                ContentType = "text/html",
                StatusCode = 200
            };
        }
    }
}