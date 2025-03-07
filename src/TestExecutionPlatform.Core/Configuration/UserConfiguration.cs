using System;
using System.Collections.Generic;

namespace TestExecutionPlatform.Core.Configuration
{
    public class UserConfiguration
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // User/Team metadata
        public string LobId { get; set; }
        public string TeamId { get; set; }
        public string UserId { get; set; }

        // Job configuration
        public UserJobConfig JobConfig { get; set; } = new UserJobConfig();
    }

    public class UserJobConfig
    {
        // Repository URL
        public string RepoUrl { get; set; }

        // Test image type
        public string TestImageType { get; set; }

        // Custom environment variables
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();

        // Custom container resources (if allowed by admin config)
        public ContainerResourceLimits ResourceLimits { get; set; }

        // Notification settings
        public NotificationConfig Notifications { get; set; } = new NotificationConfig();

        // Schedule settings (similar to what we already implemented)
        public ScheduleConfig Schedule { get; set; }
    }

    public class NotificationConfig
    {
        public bool NotifyOnSuccess { get; set; } = false;
        public bool NotifyOnFailure { get; set; } = true;
        public List<string> EmailRecipients { get; set; } = new List<string>();
        public List<string> WebhookUrls { get; set; } = new List<string>();
    }

    public class ScheduleConfig
    {
        public string ScheduleType { get; set; } = "RunOnce"; // RunOnce, Interval, Weekly, Monthly
        public int? IntervalMinutes { get; set; }
        public List<int> DaysOfWeek { get; set; } = new List<int>();
        public List<int> DaysOfMonth { get; set; } = new List<int>();
        public string TimeOfDay { get; set; }
        public DateTime? ScheduledTime { get; set; }
        public int? MaxRuns { get; set; }
    }
}