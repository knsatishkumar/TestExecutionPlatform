using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TestExecutionPlatform.Core.Services.Monitoring;

namespace TestExecutionPlatform.Functions
{
    public class MonitoringFunctions
    {
        private readonly MonitoringService _monitoringService;
        private readonly AlertService _alertService;
        private readonly ILogger<MonitoringFunctions> _logger;

        public MonitoringFunctions(
            MonitoringService monitoringService,
            AlertService alertService,
            ILogger<MonitoringFunctions> logger)
        {
            _monitoringService = monitoringService;
            _alertService = alertService;
            _logger = logger;
        }

        [FunctionName("CollectClusterMetrics")]
        public async Task CollectClusterMetrics(
            [TimerTrigger("0 */5 * * * *")] TimerInfo timer) // Run every 5 minutes
        {
            _logger.LogInformation("Starting collection of cluster metrics");
            
            try
            {
                await _monitoringService.CollectClusterMetricsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting cluster metrics");
            }
        }

        [FunctionName("CleanupCompletedJobs")]
        public async Task CleanupCompletedJobs(
            [TimerTrigger("0 */4 * * *")] TimerInfo timer) // Run every 4 hours
        {
            _logger.LogInformation("Starting cleanup of completed Kubernetes jobs");
            
            try
            {
                await _monitoringService.CleanupCompletedJobsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up completed jobs");
            }
        }

        [FunctionName("CleanupOldTestResults")]
        public async Task CleanupOldTestResults(
            [TimerTrigger("0 0 0 * * *")] TimerInfo timer) // Run daily at midnight
        {
            _logger.LogInformation("Starting cleanup of old test results based on retention policy");
            
            try
            {
                await _monitoringService.CleanupOldTestResultsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up old test results");
            }
        }

        [FunctionName("SendTestNotification")]
        public async Task SendTestNotification(
            [TimerTrigger("0 0 8 * * *")] TimerInfo timer) // Run daily at 8 AM
        {
            _logger.LogInformation("Sending test notification to verify alerting system");
            
            try
            {
                await _alertService.SendTestNotificationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending test notification");
            }
        }

        [FunctionName("SystemHealth")]
        public async Task<IActionResult> SystemHealth(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
        {
            _logger.LogInformation("System health check requested");
            
            try
            {
                var healthStatus = await _monitoringService.GetSystemHealthAsync();
                
                var response = new {
                    status = healthStatus.IsHealthy ? "Healthy" : "Unhealthy",
                    timestamp = DateTime.UtcNow,
                    components = healthStatus.ComponentStatus
                };
                
                return new OkObjectResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system health");
                
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}