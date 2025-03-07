using System;

namespace TestExecutionPlatform.Core.Models
{
    public class TestJobRequest
    {
        public string RepoUrl { get; set; }
        public string TestImageType { get; set; }
        public string LobId { get; set; }
        public string TeamId { get; set; }
        public string UserId { get; set; }
        public string ScheduleId { get; set; }

        // Optional configuration overrides
        public int? TimeoutMinutes { get; set; }
        public string Branch { get; set; } = "main";
        public string TestFilter { get; set; }
    }
}