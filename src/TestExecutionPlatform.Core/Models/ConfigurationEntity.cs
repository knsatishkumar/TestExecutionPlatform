using System;

namespace TestExecutionPlatform.Core.Models
{
    // Database entity for storing configurations
    public class ConfigurationEntity
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string LobId { get; set; }
        public string TeamId { get; set; }
        public string UserId { get; set; }
        public string ConfigYaml { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}