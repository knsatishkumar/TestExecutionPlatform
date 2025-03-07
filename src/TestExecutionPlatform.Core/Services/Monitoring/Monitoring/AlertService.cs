using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using TestExecutionPlatform.Core.Configuration;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace TestExecutionPlatform.Core.Services.Monitoring
{
    public class AlertService
    {
        private readonly ConfigurationService _configService;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<AlertService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _sendGridApiKey;
        private readonly string _alertEmailSender;

        // In-memory cache of recently sent alerts to prevent alert storms
        private readonly Dictionary<string, DateTime> _recentAlerts = new Dictionary<string, DateTime>();

        public AlertService(
            ConfigurationService configService,
            TelemetryClient telemetryClient,
            ILogger<AlertService> logger,
            HttpClient httpClient,
            string sendGridApiKey,
            string alertEmailSender)
        {
            _configService = configService;
            _telemetryClient = telemetryClient;
            _logger = logger;
            _httpClient = httpClient;
            _sendGridApiKey = sendGridApiKey;
            _alertEmailSender = alertEmailSender;
        }

        public async Task SendTestNotificationAsync()
        {
            var adminConfig = await _configService.GetAdminConfigurationAsync();

            if (adminConfig.Alerts.Notifications.SendTestNotificationOnStartup)
            {
                await SendNotificationAsync(
                    "Test Notification",
                    "Test Execution Platform started successfully and alert system is operational.",
                    AlertSeverity.Information,
                    null);
            }
        }

        public async Task EvaluateMetricAsync(string metricName, double value, Dictionary<string, string> dimensions = null)
        {
            try
            {
                var adminConfig = await _configService.GetAdminConfigurationAsync();
                var applicableRules = adminConfig.Alerts.Rules
                    .Where(r => r.Enabled && r.Metric == metricName)
                    .ToList();

                foreach (var rule in applicableRules)
                {
                    // Check if rule dimensions match provided dimensions
                    if (rule.Dimensions != null && rule.Dimensions.Count > 0)
                    {
                        if (dimensions == null)
                        {
                            continue; // Skip this rule since dimensions don't match
                        }

                        bool allDimensionsMatch = true;
                        foreach (var dim in rule.Dimensions)
                        {
                            if (!dimensions.TryGetValue(dim.Key, out var dimValue) || dimValue != dim.Value)
                            {
                                allDimensionsMatch = false;
                                break;
                            }
                        }

                        if (!allDimensionsMatch)
                        {
                            continue; // Skip this rule since dimensions don't match
                        }
                    }

                    // Check if threshold is violated
                    bool isViolated = false;

                    switch (rule.Operator)
                    {
                        case "GreaterThan":
                            isViolated = value > rule.Threshold;
                            break;

                        case "LessThan":
                            isViolated = value < rule.Threshold;
                            break;

                        case "Equals":
                            isViolated = Math.Abs(value - rule.Threshold) < 0.0001;
                            break;

                        default:
                            _logger.LogWarning($"Unknown operator '{rule.Operator}' in alert rule '{rule.Name}'");
                            continue;
                    }

                    if (isViolated)
                    {
                        // Check if we've sent this alert recently to avoid alert storms
                        string alertKey = $"{rule.Id}:{string.Join(",", dimensions?.Select(d => $"{d.Key}={d.Value}") ?? Array.Empty<string>())}";

                        if (_recentAlerts.TryGetValue(alertKey, out var lastSentTime))
                        {
                            var cooldownPeriod = TimeSpan.FromMinutes(rule.TimeWindowMinutes / 2.0);
                            if (DateTime.UtcNow - lastSentTime < cooldownPeriod)
                            {
                                // Skip this alert as we've sent it recently
                                continue;
                            }
                        }

                        // Generate alert message
                        string title = $"Alert: {rule.Name}";
                        string message = $"{rule.Description}\n\n" +
                                        $"Metric: {metricName}\n" +
                                        $"Value: {value}\n" +
                                        $"Threshold: {rule.Threshold} ({rule.Operator})\n" +
                                        $"Time: {DateTime.UtcNow}";

                        if (dimensions != null && dimensions.Count > 0)
                        {
                            message += "\n\nDimensions:\n";
                            foreach (var dim in dimensions)
                            {
                                message += $"- {dim.Key}: {dim.Value}\n";
                            }
                        }

                        // Send the alert
                        await SendNotificationAsync(title, message, rule.Severity, dimensions);

                        // Track when we sent this alert
                        _recentAlerts[alertKey] = DateTime.UtcNow;

                        // Clean up old entries in _recentAlerts
                        CleanupRecentAlerts();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error evaluating metric {metricName}");
            }
        }

        public async Task SendNotificationAsync(string title, string message, AlertSeverity severity, Dictionary<string, string> dimensions)
        {
            try
            {
                var adminConfig = await _configService.GetAdminConfigurationAsync();
                var notifications = adminConfig.Alerts.Notifications;

                // Track alert in Application Insights
                SeverityLevel aiSeverity;
                switch (severity)
                {
                    case AlertSeverity.Critical:
                        aiSeverity = SeverityLevel.Critical;
                        break;
                    case AlertSeverity.Warning:
                        aiSeverity = SeverityLevel.Warning;
                        break;
                    default:
                        aiSeverity = SeverityLevel.Information;
                        break;
                }

                var properties = new Dictionary<string, string>
                {
                    { "AlertTitle", title },
                    { "AlertSeverity", severity.ToString() }
                };

                if (dimensions != null)
                {
                    foreach (var dim in dimensions)
                    {
                        properties[dim.Key] = dim.Value;
                    }
                }

                var telemetry = new TraceTelemetry(message, aiSeverity);
                foreach (var prop in properties)
                {
                    telemetry.Properties.Add(prop.Key, prop.Value);
                }

                _telemetryClient.TrackTrace(telemetry);

                // Send email notifications if enabled
                if (notifications.Email.Enabled)
                {
                    bool shouldSendEmail = false;

                    switch (severity)
                    {
                        case AlertSeverity.Critical:
                            shouldSendEmail = notifications.Email.SendCritical;
                            break;
                        case AlertSeverity.Warning:
                            shouldSendEmail = notifications.Email.SendWarning;
                            break;
                        case AlertSeverity.Information:
                            shouldSendEmail = notifications.Email.SendInformation;
                            break;
                    }

                    if (shouldSendEmail && notifications.Email.Recipients.Count > 0)
                    {
                        await SendEmailAlertAsync(
                            notifications.Email.Recipients,
                            title,
                            message,
                            severity);
                    }
                }

                // Send webhook notifications if enabled
                if (notifications.Webhook.Enabled)
                {
                    bool shouldSendWebhook = false;

                    switch (severity)
                    {
                        case AlertSeverity.Critical:
                            shouldSendWebhook = notifications.Webhook.SendCritical;
                            break;
                        case AlertSeverity.Warning:
                            shouldSendWebhook = notifications.Webhook.SendWarning;
                            break;
                        case AlertSeverity.Information:
                            shouldSendWebhook = notifications.Webhook.SendInformation;
                            break;
                    }

                    if (shouldSendWebhook && notifications.Webhook.Urls.Count > 0)
                    {
                        await SendWebhookAlertsAsync(
                            notifications.Webhook.Urls,
                            title,
                            message,
                            severity,
                            properties);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending alert notification");
            }
        }

        private async Task SendEmailAlertAsync(List<string> recipients, string subject, string message, AlertSeverity severity)
        {
            try
            {
                var client = new SendGridClient(_sendGridApiKey);
                var from = new EmailAddress(_alertEmailSender, "Test Execution Platform");
                var tos = recipients.Select(r => new EmailAddress(r)).ToList();

                var plainTextContent = message;
                var htmlContent = $"<strong>{subject}</strong><br><br>{message.Replace("\n", "<br>")}";

                var msg = MailHelper.CreateSingleEmailToMultipleRecipients(
                    from, tos, subject, plainTextContent, htmlContent);

                var response = await client.SendEmailAsync(msg);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Failed to send alert email: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email alert");
            }
        }

        private async Task SendWebhookAlertsAsync(
            List<string> webhookUrls,
            string title,
            string message,
            AlertSeverity severity,
            Dictionary<string, string> properties)
        {
            foreach (var url in webhookUrls)
            {
                try
                {
                    var payload = new
                    {
                        title,
                        message,
                        severity = severity.ToString(),
                        timestamp = DateTime.UtcNow,
                        properties
                    };

                    var content = new StringContent(
                        JsonSerializer.Serialize(payload),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PostAsync(url, content);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning($"Failed to send webhook alert to {url}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error sending webhook alert to {url}");
                }
            }
        }

        private void CleanupRecentAlerts()
        {
            // Remove alerts older than 24 hours
            var cutoffTime = DateTime.UtcNow.AddHours(-24);
            var keysToRemove = _recentAlerts.Where(kvp => kvp.Value < cutoffTime)
                                          .Select(kvp => kvp.Key)
                                          .ToList();

            foreach (var key in keysToRemove)
            {
                _recentAlerts.Remove(key);
            }
        }
    }
}