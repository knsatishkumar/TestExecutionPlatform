using System;
using System.Collections.Generic;

namespace TestExecutionPlatform.Core.Models
{
    public enum ScheduleType
    {
        RunOnce,
        Interval,
        Weekly,
        Monthly
    }

    public class TestJobSchedule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string LobId { get; set; }
        public string TeamId { get; set; }
        public string RepoUrl { get; set; }
        public string TestImageType { get; set; }
        public ScheduleType ScheduleType { get; set; }

        // For interval-based scheduling (in minutes)
        public int? IntervalMinutes { get; set; }

        // For weekly scheduling (0 = Sunday, 6 = Saturday)
        public List<int> DaysOfWeek { get; set; } = new List<int>();

        // For monthly scheduling
        public List<int> DaysOfMonth { get; set; } = new List<int>();

        // Time of day to run (in 24-hour format)
        public TimeSpan? TimeOfDay { get; set; }

        // For one-time scheduling
        public DateTime? ScheduledTime { get; set; }

        // Max number of times to run (null for unlimited)
        public int? MaxRuns { get; set; }

        // Number of times this job has run
        public int RunCount { get; set; }

        // Whether the schedule is active
        public bool IsActive { get; set; } = true;

        // When this schedule was created
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Last time this schedule was run
        public DateTime? LastRunTime { get; set; }
    }
}