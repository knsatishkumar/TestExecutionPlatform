using System;
using System.Collections.Generic;

namespace TestExecutionPlatform.Core.Configuration
{
    public class AdminConfiguration
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Resource management
        public ResourceManagementConfig ResourceManagement { get; set; } = new ResourceManagementConfig();

        // Data retention
        public RetentionConfig Retention { get; set; } = new RetentionConfig();

        // Cluster settings
        public ClusterConfig Cluster { get; set; } = new ClusterConfig();

        // API rate limits
        public RateLimitConfig RateLimits { get; set; } = new RateLimitConfig();

        // Alert configuration
        public AlertConfig Alerts { get; set; } = new AlertConfig();
    }

    public class ResourceManagementConfig
    {
        // Maximum concurrent jobs per LOB
        public int MaxConcurrentJobsPerLob { get; set; } = 50;

        // Maximum concurrent jobs per team
        public int MaxConcurrentJobsPerTeam { get; set; } = 20;

        // Job timeout in minutes (default 60 minutes)
        public int DefaultJobTimeoutMinutes { get; set; } = 60;

        // Container resource limits
        public ContainerResourceLimits DefaultContainerLimits { get; set; } = new ContainerResourceLimits();

        // Resource cleanup settings
        public bool AutoCleanupJobs { get; set; } = true;
        public int CleanupAfterHours { get; set; } = 24;
    }

    public class ContainerResourceLimits
    {
        public string CpuLimit { get; set; } = "1";
        public string MemoryLimit { get; set; } = "2Gi";
        public string CpuRequest { get; set; } = "0.5";
        public string MemoryRequest { get; set; } = "1Gi";
    }

    public class RetentionConfig
    {
        // Test results retention period in days
        public int TestResultsRetentionDays { get; set; } = 90;

        // Job history retention period in days
        public int JobHistoryRetentionDays { get; set; } = 180;

        // Maximum size of test result files (MB)
        public int MaxTestResultFileSizeMB { get; set; } = 50;
    }

    public class ClusterConfig
    {
        // Default namespace for system components
        public string SystemNamespace { get; set; } = "testexec-system";

        // LOB namespace prefix
        public string LobNamespacePrefix { get; set; } = "testexec-";

        // Enable node auto-scaling
        public bool EnableNodeAutoscaling { get; set; } = true;

        // Node pool configurations
        public List<NodePoolConfig> NodePools { get; set; } = new List<NodePoolConfig>
        {
            new NodePoolConfig { Name = "default", MinNodes = 3, MaxNodes = 10 }
        };
    }

    public class NodePoolConfig
    {
        public string Name { get; set; }
        public int MinNodes { get; set; } = 1;
        public int MaxNodes { get; set; } = 5;
    }

    public class RateLimitConfig
    {
        // Requests per minute per user
        public int RequestsPerMinutePerUser { get; set; } = 60;

        // Requests per minute per team
        public int RequestsPerMinutePerTeam { get; set; } = 300;

        // Requests per minute per LOB
        public int RequestsPerMinutePerLob { get; set; } = 1000;
    }
}