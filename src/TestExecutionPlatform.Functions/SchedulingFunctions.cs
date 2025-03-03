using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TestExecutionPlatform.Core.Services;
using System.Security.Claims;
using TestExecutionPlatform.Core.Models;

namespace TestExecutionPlatform.Functions
{
    public class SchedulingFunctions
    {
        private readonly SchedulingService _schedulingService;
        private readonly TestExecutionService _testExecutionService;
        private readonly ILogger<SchedulingFunctions> _logger;

        public SchedulingFunctions(
            SchedulingService schedulingService,
            TestExecutionService testExecutionService,
            ILogger<SchedulingFunctions> logger)
        {
            _schedulingService = schedulingService;
            _testExecutionService = testExecutionService;
            _logger = logger;
        }

        [FunctionName("CreateSchedule")]
        public async Task<IActionResult> CreateSchedule(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "schedules")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation("Creating test job schedule.");

            // Extract LOB ID and Team ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;
            string teamId = claimsPrincipal.FindFirst("team_id")?.Value;

            if (string.IsNullOrEmpty(lobId) || string.IsNullOrEmpty(teamId))
            {
                return new UnauthorizedResult();
            }

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                var schedule = await _schedulingService.CreateScheduleFromYamlAsync(requestBody, lobId, teamId);

                return new OkObjectResult(schedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating schedule");
                return new BadRequestObjectResult(ex.Message);
            }
        }

        [FunctionName("GetSchedule")]
        public async Task<IActionResult> GetSchedule(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "schedules/{id}")] HttpRequest req,
            string id,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation($"Getting schedule {id}.");

            // Extract LOB ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;

            if (string.IsNullOrEmpty(lobId))
            {
                return new UnauthorizedResult();
            }

            var schedule = await _schedulingService.GetScheduleByIdAsync(id, lobId);

            if (schedule == null)
            {
                return new NotFoundResult();
            }

            return new OkObjectResult(schedule);
        }

        [FunctionName("ListSchedules")]
        public async Task<IActionResult> ListSchedules(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "schedules")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation("Listing schedules.");

            // Extract LOB ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;
            string teamId = claimsPrincipal.FindFirst("team_id")?.Value;

            if (string.IsNullOrEmpty(lobId) || string.IsNullOrEmpty(teamId))
            {
                return new UnauthorizedResult();
            }

            var schedules = await _schedulingService.GetSchedulesAsync(lobId, teamId);
            return new OkObjectResult(schedules);
        }

        [FunctionName("UpdateSchedule")]
        public async Task<IActionResult> UpdateSchedule(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "schedules/{id}")] HttpRequest req,
            string id,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation($"Updating schedule {id}.");

            // Extract LOB ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;
            string teamId = claimsPrincipal.FindFirst("team_id")?.Value;

            if (string.IsNullOrEmpty(lobId) || string.IsNullOrEmpty(teamId))
            {
                return new UnauthorizedResult();
            }

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                var schedule = await _schedulingService.UpdateScheduleAsync(id, requestBody, lobId, teamId);

                return new OkObjectResult(schedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating schedule {id}");
                return new BadRequestObjectResult(ex.Message);
            }
        }

        [FunctionName("DeleteSchedule")]
        public async Task<IActionResult> DeleteSchedule(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "schedules/{id}")] HttpRequest req,
            string id,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation($"Deleting schedule {id}.");

            // Extract LOB ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;

            if (string.IsNullOrEmpty(lobId))
            {
                return new UnauthorizedResult();
            }

            bool deleted = await _schedulingService.DeleteScheduleAsync(id, lobId);

            if (!deleted)
            {
                return new NotFoundResult();
            }

            return new OkResult();
        }

        [FunctionName("ProcessScheduledJobs")]
        public async Task ProcessScheduledJobs(
            [TimerTrigger("0 */5 * * * *")] TimerInfo timer, // Runs every 5 minutes
            [Queue("test-job-queue")] IAsyncCollector<TestJobRequest> queue)
        {
            _logger.LogInformation("Processing scheduled jobs.");

            try
            {
                var dueSchedules = await _schedulingService.GetDueSchedulesAsync();

                foreach (var schedule in dueSchedules)
                {
                    _logger.LogInformation($"Schedule {schedule.Id} is due. Queueing test job.");

                    // Queue the test job
                    var jobRequest = new TestJobRequest
                    {
                        RepoUrl = schedule.RepoUrl,
                        TestImageType = schedule.TestImageType,
                        LobId = schedule.LobId,
                        TeamId = schedule.TeamId
                    };

                    await queue.AddAsync(jobRequest);

                    // Update last run time
                    await _schedulingService.UpdateScheduleLastRunAsync(schedule.Id, schedule.LobId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scheduled jobs");
            }
        }
    }
}