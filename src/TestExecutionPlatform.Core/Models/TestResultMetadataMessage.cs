using System;

namespace TestExecutionPlatform.Core.Models
{
    // Base message interface
    public interface ITestResultMessage
    {
        string MessageId { get; }
        DateTime Timestamp { get; }
    }

    // Test result metadata message
    public class TestResultMetadataMessage : ITestResultMessage
    {
        public string MessageId { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Job information
        public string JobId { get; set; }
        public string LobId { get; set; }
        public string TeamId { get; set; }
        public string RepoUrl { get; set; }
        public string TestImageType { get; set; }

        // Test results summary
        public int TotalTests { get; set; }
        public int PassedTests { get; set; }
        public int FailedTests { get; set; }
        public int SkippedTests { get; set; }

        // Execution information
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double DurationInSeconds { get; set; }
    }
}