using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using TestExecutionPlatform.Core.Configuration;
using TestExecutionPlatform.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TestExecutionPlatform.Core.Services
{
    public class ConfigurationService
    {
        private readonly string _connectionString;
        private readonly ILogger<ConfigurationService> _logger;
        private readonly IDeserializer _yamlDeserializer;
        private readonly ISerializer _yamlSerializer;

        // Cache for admin configuration (refreshed periodically)
        private AdminConfiguration _cachedAdminConfig;
        private DateTime _adminConfigLastRefreshed = DateTime.MinValue;
        private readonly TimeSpan _adminConfigCacheDuration = TimeSpan.FromMinutes(5);

        public ConfigurationService(string connectionString, ILogger<ConfigurationService> logger)
        {
            _connectionString = connectionString;
            _logger = logger;

            _yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            _yamlSerializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
        }

        // Admin Configuration Methods

        public async Task<AdminConfiguration> GetAdminConfigurationAsync(bool useCache = true)
        {
            // Check if we have a cached config that's still valid
            if (useCache && _cachedAdminConfig != null &&
                (DateTime.UtcNow - _adminConfigLastRefreshed) < _adminConfigCacheDuration)
            {
                return _cachedAdminConfig;
            }

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var sql = "SELECT TOP 1 * FROM AdminConfigurations ORDER BY CreatedAt DESC";
                var configEntity = await connection.QueryFirstOrDefaultAsync<ConfigurationEntity>(sql);

                if (configEntity == null)
                {
                    // Create default admin configuration if none exists
                    var defaultConfig = new AdminConfiguration
                    {
                        Name = "Default Admin Configuration",
                        CreatedAt = DateTime.UtcNow
                    };

                    await SaveAdminConfigurationAsync(defaultConfig);

                    _cachedAdminConfig = defaultConfig;
                    _adminConfigLastRefreshed = DateTime.UtcNow;

                    return defaultConfig;
                }

                // Deserialize YAML content to AdminConfiguration
                var adminConfig = _yamlDeserializer.Deserialize<AdminConfiguration>(configEntity.ConfigYaml);
                adminConfig.Id = configEntity.Id;
                adminConfig.Name = configEntity.Name;
                adminConfig.CreatedAt = configEntity.CreatedAt;
                adminConfig.UpdatedAt = configEntity.UpdatedAt;

                // Update cache
                _cachedAdminConfig = adminConfig;
                _adminConfigLastRefreshed = DateTime.UtcNow;

                return adminConfig;
            }
        }

        public async Task SaveAdminConfigurationAsync(AdminConfiguration config)
        {
            // Check if user has admin role (would be validated in the function)

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Serialize configuration to YAML
                string configYaml = _yamlSerializer.Serialize(config);

                // Check if config already exists
                var existingSql = "SELECT TOP 1 * FROM AdminConfigurations WHERE Id = @Id";
                var existing = await connection.QueryFirstOrDefaultAsync<ConfigurationEntity>(existingSql, new { Id = config.Id });

                if (existing != null)
                {
                    // Update existing config
                    var updateSql = @"
                        UPDATE AdminConfigurations 
                        SET Name = @Name, 
                            ConfigYaml = @ConfigYaml,
                            UpdatedAt = @UpdatedAt
                        WHERE Id = @Id";

                    await connection.ExecuteAsync(updateSql, new
                    {
                        Id = config.Id,
                        Name = config.Name,
                        ConfigYaml = configYaml,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    // Insert new config
                    var insertSql = @"
                        INSERT INTO AdminConfigurations (Id, Name, ConfigYaml, CreatedAt)
                        VALUES (@Id, @Name, @ConfigYaml, @CreatedAt)";

                    await connection.ExecuteAsync(insertSql, new
                    {
                        Id = config.Id,
                        Name = config.Name,
                        ConfigYaml = configYaml,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // Invalidate cache
                _cachedAdminConfig = null;
            }
        }

        // User Configuration Methods

        public async Task<UserConfiguration> GetUserConfigurationAsync(string id, string lobId, string teamId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var sql = "SELECT * FROM UserConfigurations WHERE Id = @Id AND LobId = @LobId AND TeamId = @TeamId";
                var configEntity = await connection.QueryFirstOrDefaultAsync<ConfigurationEntity>(sql,
                    new { Id = id, LobId = lobId, TeamId = teamId });

                if (configEntity == null)
                {
                    return null;
                }

                // Deserialize YAML content to UserConfiguration
                var userConfig = _yamlDeserializer.Deserialize<UserConfiguration>(configEntity.ConfigYaml);
                userConfig.Id = configEntity.Id;
                userConfig.Name = configEntity.Name;
                userConfig.CreatedAt = configEntity.CreatedAt;
                userConfig.UpdatedAt = configEntity.UpdatedAt;
                userConfig.LobId = configEntity.LobId;
                userConfig.TeamId = configEntity.TeamId;
                userConfig.UserId = configEntity.UserId;

                return userConfig;
            }
        }

        public async Task<IEnumerable<UserConfiguration>> GetUserConfigurationsAsync(string lobId, string teamId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var sql = "SELECT * FROM UserConfigurations WHERE LobId = @LobId AND TeamId = @TeamId";
                var configEntities = await connection.QueryAsync<ConfigurationEntity>(sql,
                    new { LobId = lobId, TeamId = teamId });

                var configurations = new List<UserConfiguration>();

                foreach (var entity in configEntities)
                {
                    try
                    {
                        var userConfig = _yamlDeserializer.Deserialize<UserConfiguration>(entity.ConfigYaml);
                        userConfig.Id = entity.Id;
                        userConfig.Name = entity.Name;
                        userConfig.CreatedAt = entity.CreatedAt;
                        userConfig.UpdatedAt = entity.UpdatedAt;
                        userConfig.LobId = entity.LobId;
                        userConfig.TeamId = entity.TeamId;
                        userConfig.UserId = entity.UserId;

                        configurations.Add(userConfig);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error deserializing user configuration {entity.Id}");
                    }
                }

                return configurations;
            }
        }

        public async Task<UserConfiguration> CreateUserConfigurationFromYamlAsync(string yamlContent, string lobId, string teamId, string userId)
        {
            // Validate YAML and deserialize to UserConfiguration
            UserConfiguration userConfig;
            try
            {
                userConfig = _yamlDeserializer.Deserialize<UserConfiguration>(yamlContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializing user configuration YAML");
                throw new ArgumentException($"Invalid YAML configuration: {ex.Message}", ex);
            }

            // Override sensitive values for security
            userConfig.Id = Guid.NewGuid().ToString();
            userConfig.LobId = lobId;
            userConfig.TeamId = teamId;
            userConfig.UserId = userId;
            userConfig.CreatedAt = DateTime.UtcNow;

            // Validate against admin configuration
            var adminConfig = await GetAdminConfigurationAsync();
            ValidateUserConfiguration(userConfig, adminConfig);

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Serialize to YAML (to ensure consistent formatting)
                string configYaml = _yamlSerializer.Serialize(userConfig);

                // Insert new config
                var insertSql = @"
                    INSERT INTO UserConfigurations (Id, Name, LobId, TeamId, UserId, ConfigYaml, CreatedAt)
                    VALUES (@Id, @Name, @LobId, @TeamId, @UserId, @ConfigYaml, @CreatedAt)";

                await connection.ExecuteAsync(insertSql, new
                {
                    userConfig.Id,
                    userConfig.Name,
                    userConfig.LobId,
                    userConfig.TeamId,
                    userConfig.UserId,
                    ConfigYaml = configYaml,
                    userConfig.CreatedAt
                });

                return userConfig;
            }
        }

        public async Task<UserConfiguration> UpdateUserConfigurationAsync(string id, string yamlContent, string lobId, string teamId, string userId)
        {
            // Validate the existing config exists and belongs to the user
            var existingConfig = await GetUserConfigurationAsync(id, lobId, teamId);
            if (existingConfig == null)
            {
                throw new ArgumentException($"Configuration with ID {id} not found for LOB {lobId} and team {teamId}");
            }

            // Deserialize new YAML
            UserConfiguration updatedConfig;
            try
            {
                updatedConfig = _yamlDeserializer.Deserialize<UserConfiguration>(yamlContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializing updated user configuration YAML");
                throw new ArgumentException($"Invalid YAML configuration: {ex.Message}", ex);
            }

            // Keep original ID and metadata, update the rest
            updatedConfig.Id = id;
            updatedConfig.LobId = lobId;
            updatedConfig.TeamId = teamId;
            updatedConfig.UserId = userId;
            updatedConfig.CreatedAt = existingConfig.CreatedAt;
            updatedConfig.UpdatedAt = DateTime.UtcNow;

            // Validate against admin configuration
            var adminConfig = await GetAdminConfigurationAsync();
            ValidateUserConfiguration(updatedConfig, adminConfig);

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Serialize to YAML
                string configYaml = _yamlSerializer.Serialize(updatedConfig);

                // Update existing config
                var updateSql = @"
                    UPDATE UserConfigurations 
                    SET Name = @Name, 
                        ConfigYaml = @ConfigYaml,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @Id AND LobId = @LobId AND TeamId = @TeamId";

                await connection.ExecuteAsync(updateSql, new
                {
                    updatedConfig.Id,
                    updatedConfig.Name,
                    updatedConfig.LobId,
                    updatedConfig.TeamId,
                    ConfigYaml = configYaml,
                    UpdatedAt = DateTime.UtcNow
                });

                return updatedConfig;
            }
        }

        public async Task<bool> DeleteUserConfigurationAsync(string id, string lobId, string teamId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var sql = "DELETE FROM UserConfigurations WHERE Id = @Id AND LobId = @LobId AND TeamId = @TeamId";
                int rowsAffected = await connection.ExecuteAsync(sql, new { Id = id, LobId = lobId, TeamId = teamId });

                return rowsAffected > 0;
            }
        }

        // Helper methods

        private void ValidateUserConfiguration(UserConfiguration userConfig, AdminConfiguration adminConfig)
        {
            // Validate resource limits if specified
            if (userConfig.JobConfig?.ResourceLimits != null)
            {
                var defaultLimits = adminConfig.ResourceManagement.DefaultContainerLimits;
                var userLimits = userConfig.JobConfig.ResourceLimits;

                // Parse CPU values to ensure they don't exceed admin limits
                if (!string.IsNullOrEmpty(userLimits.CpuLimit))
                {
                    double adminCpuLimit = ParseCpuValue(defaultLimits.CpuLimit);
                    double userCpuLimit = ParseCpuValue(userLimits.CpuLimit);

                    if (userCpuLimit > adminCpuLimit)
                    {
                        throw new ArgumentException($"CPU limit ({userLimits.CpuLimit}) exceeds maximum allowed ({defaultLimits.CpuLimit})");
                    }
                }

                // Parse memory values to ensure they don't exceed admin limits
                if (!string.IsNullOrEmpty(userLimits.MemoryLimit))
                {
                    long adminMemoryLimit = ParseMemoryValue(defaultLimits.MemoryLimit);
                    long userMemoryLimit = ParseMemoryValue(userLimits.MemoryLimit);

                    if (userMemoryLimit > adminMemoryLimit)
                    {
                        throw new ArgumentException($"Memory limit ({userLimits.MemoryLimit}) exceeds maximum allowed ({defaultLimits.MemoryLimit})");
                    }
                }
            }

            // Add more validations as needed
        }

        private double ParseCpuValue(string cpuValue)
        {
            // CPU can be specified as "1" or "1000m"
            if (cpuValue.EndsWith("m"))
            {
                return double.Parse(cpuValue.Substring(0, cpuValue.Length - 1)) / 1000.0;
            }
            return double.Parse(cpuValue);
        }

        private long ParseMemoryValue(string memoryValue)
        {
            // Memory can be specified as "1Gi", "1000Mi", "1000Ki", etc.
            long multiplier = 1;

            if (memoryValue.EndsWith("Gi"))
            {
                multiplier = 1024 * 1024 * 1024;
                return long.Parse(memoryValue.Substring(0, memoryValue.Length - 2)) * multiplier;
            }
            else if (memoryValue.EndsWith("Mi"))
            {
                multiplier = 1024 * 1024;
                return long.Parse(memoryValue.Substring(0, memoryValue.Length - 2)) * multiplier;
            }
            else if (memoryValue.EndsWith("Ki"))
            {
                multiplier = 1024;
                return long.Parse(memoryValue.Substring(0, memoryValue.Length - 2)) * multiplier;
            }

            return long.Parse(memoryValue);
        }
    }
}