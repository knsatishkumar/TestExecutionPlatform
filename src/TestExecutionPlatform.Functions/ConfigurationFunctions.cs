using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.IO;
using TestExecutionPlatform.Core.Services;
using TestExecutionPlatform.Core.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TestExecutionPlatform.Functions
{
    public class ConfigurationFunctions
    {
        private readonly ConfigurationService _configService;
        private readonly ILogger<ConfigurationFunctions> _logger;

        public ConfigurationFunctions(
            ConfigurationService configService,
            ILogger<ConfigurationFunctions> logger)
        {
            _configService = configService;
            _logger = logger;
        }

        [FunctionName("GetAdminConfiguration")]
        public async Task<IActionResult> GetAdminConfiguration(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/configuration")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation("Getting admin configuration.");

            // Check for admin role
            if (!claimsPrincipal.IsInRole("TestExecutionAdmin"))
            {
                return new UnauthorizedResult();
            }

            try
            {
                var adminConfig = await _configService.GetAdminConfigurationAsync();
                return new OkObjectResult(adminConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting admin configuration");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("UpdateAdminConfiguration")]
        public async Task<IActionResult> UpdateAdminConfiguration(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "admin/configuration")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation("Updating admin configuration.");

            // Check for admin role
            if (!claimsPrincipal.IsInRole("TestExecutionAdmin"))
            {
                return new UnauthorizedResult();
            }

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                // Deserialize YAML to AdminConfiguration
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                var adminConfig = deserializer.Deserialize<AdminConfiguration>(requestBody);

                // If Id is not provided, get the current admin config ID
                if (string.IsNullOrEmpty(adminConfig.Id))
                {
                    var currentConfig = await _configService.GetAdminConfigurationAsync();
                    adminConfig.Id = currentConfig.Id;
                }

                // Set update time
                adminConfig.UpdatedAt = DateTime.UtcNow;

                // Save configuration
                await _configService.SaveAdminConfigurationAsync(adminConfig);

                return new OkObjectResult(adminConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating admin configuration");
                return new BadRequestObjectResult(ex.Message);
            }
        }

        [FunctionName("GetUserConfigurations")]
        public async Task<IActionResult> GetUserConfigurations(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "configurations")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation("Getting user configurations.");

            // Extract LOB ID and Team ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;
            string teamId = claimsPrincipal.FindFirst("team_id")?.Value;

            if (string.IsNullOrEmpty(lobId) || string.IsNullOrEmpty(teamId))
            {
                return new UnauthorizedResult();
            }

            try
            {
                var configurations = await _configService.GetUserConfigurationsAsync(lobId, teamId);
                return new OkObjectResult(configurations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user configurations");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("GetUserConfiguration")]
        public async Task<IActionResult> GetUserConfiguration(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "configurations/{id}")] HttpRequest req,
            string id,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation($"Getting user configuration {id}.");

            // Extract LOB ID and Team ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;
            string teamId = claimsPrincipal.FindFirst("team_id")?.Value;

            if (string.IsNullOrEmpty(lobId) || string.IsNullOrEmpty(teamId))
            {
                return new UnauthorizedResult();
            }

            try
            {
                var configuration = await _configService.GetUserConfigurationAsync(id, lobId, teamId);

                if (configuration == null)
                {
                    return new NotFoundResult();
                }

                return new OkObjectResult(configuration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting user configuration {id}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("CreateUserConfiguration")]
        public async Task<IActionResult> CreateUserConfiguration(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "configurations")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation("Creating user configuration.");

            // Extract LOB ID, Team ID and User ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;
            string teamId = claimsPrincipal.FindFirst("team_id")?.Value;
            string userId = claimsPrincipal.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(lobId) || string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(userId))
            {
                return new UnauthorizedResult();
            }

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                var configuration = await _configService.CreateUserConfigurationFromYamlAsync(
                    requestBody, lobId, teamId, userId);

                return new OkObjectResult(configuration);
            }
            catch (ArgumentException ex)
            {
                return new BadRequestObjectResult(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user configuration");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("UpdateUserConfiguration")]
        public async Task<IActionResult> UpdateUserConfiguration(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "configurations/{id}")] HttpRequest req,
            string id,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation($"Updating user configuration {id}.");

            // Extract LOB ID, Team ID and User ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;
            string teamId = claimsPrincipal.FindFirst("team_id")?.Value;
            string userId = claimsPrincipal.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(lobId) || string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(userId))
            {
                return new UnauthorizedResult();
            }

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                var configuration = await _configService.UpdateUserConfigurationAsync(
                    id, requestBody, lobId, teamId, userId);

                return new OkObjectResult(configuration);
            }
            catch (ArgumentException ex)
            {
                return new BadRequestObjectResult(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating user configuration {id}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("DeleteUserConfiguration")]
        public async Task<IActionResult> DeleteUserConfiguration(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "configurations/{id}")] HttpRequest req,
            string id,
            ClaimsPrincipal claimsPrincipal)
        {
            _logger.LogInformation($"Deleting user configuration {id}.");

            // Extract LOB ID and Team ID from claims
            string lobId = claimsPrincipal.FindFirst("lob_id")?.Value;
            string teamId = claimsPrincipal.FindFirst("team_id")?.Value;

            if (string.IsNullOrEmpty(lobId) || string.IsNullOrEmpty(teamId))
            {
                return new UnauthorizedResult();
            }

            try
            {
                await _configService.DeleteUserConfigurationAsync(id, lobId, teamId);
                return new OkResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting user configuration {id}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}