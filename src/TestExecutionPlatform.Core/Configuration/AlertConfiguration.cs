using System;
using System.Collections.Generic;

namespace TestExecutionPlatform.Core.Configuration
{
    public class AlertConfig
    {
        public List<AlertRule> Rules { get; set; } = new List<AlertRule>
        {
            // Default rules
            new AlertRule
            {
                Name = "HighFailureRate",
                Description = "Alert when test failure rate exceeds threshold",
                Metric = "TestExecution.FailRate",
                Threshold = 20.0,
                Operator = "GreaterThan",
                TimeWindowMinutes = 60,
                Severity = AlertSeverity.Warning
            },
            new AlertRule
            {
                Name = "ClusterLoadHigh",
                Description = "Alert when cluster load is high",
                Metric = "Kubernetes.ClusterLoad",
                Threshold = 0.8,
                Operator = "GreaterThan",
                TimeWindowMinutes = 15,
                Severity = AlertSeverity.Warning
            },
            new AlertRule
            {
                Name = "NodeNotReady",
                Description = "Alert when nodes are not ready",
                Metric = "Kubernetes.NotReadyNodes",
                Threshold = 0,
                Operator = "GreaterThan",
                TimeWindowMinutes = 10,
                Severity = AlertSeverity.Critical
            }
        };

        public AlertNotificationConfig Notifications { get; set; } = new AlertNotificationConfig();
    }

    public class AlertRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string Description { get; set; }
        public string Metric { get; set; }
        public double Threshold { get; set; }
        public string Operator { get; set; } // "GreaterThan", "LessThan", "Equals"
        public int TimeWindowMinutes { get; set; } = 60;
        public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
        public bool Enabled { get; set; } = true;

        // Optional filters to scope the alert
        public Dictionary<string, string> Dimensions { get; set; } = new Dictionary<string, string>();
    }

    public class AlertNotificationConfig
    {
        public EmailNotificationSettings Email { get; set; } = new EmailNotificationSettings();
        public WebhookNotificationSettings Webhook { get; set; } = new WebhookNotificationSettings();
        public bool SendTestNotificationOnStartup { get; set; } = true;
    }

    public class EmailNotificationSettings
    {
        public bool Enabled { get; set; } = true;
        public List<string> Recipients { get; set; } = new List<string>();

        // Notification level filtering
        public bool SendCritical { get; set; } = true;
        public bool SendWarning { get; set; } = true;
        public bool SendInformation { get; set; } = false;
    }

    public class WebhookNotificationSettings
    {
        public bool Enabled { get; set; } = false;
        public List<string> Urls { get; set; } = new List<string>();

        // Notification level filtering
        public bool SendCritical { get; set; } = true;
        public bool SendWarning { get; set; } = true;
        public bool SendInformation { get; set; } = false;
    }

    public enum AlertSeverity
    {
        Information,
        Warning,
        Critical
    }
}