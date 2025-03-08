using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using TestExecutionPlatform.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TestExecutionPlatform.Core.Services
{
    public class SchedulingService
    {
        private readonly string _connectionString;
        private readonly ILogger<SchedulingService> _logger;
        private readonly IDeserializer _yamlDeserializer;
        private readonly ISerializer _yamlSerializer;

        public SchedulingService(string sqlConnectionString, ILogger<SchedulingService> logger)
        {
            _connectionString = sqlConnectionString;
            _logger = logger;

            _yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            _yamlSerializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
        }

        public async Task<TestJobSchedule> CreateScheduleFromYamlAsync(string yamlContent, string lobId, string teamId)
        {
            TestJobSchedule schedule;
            try
            {
                schedule = _yamlDeserializer.Deserialize<TestJobSchedule>(yamlContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializing schedule YAML");
                throw new ArgumentException($"Invalid YAML schedule: {ex.Message}", ex);
            }

            // Override LOB and team IDs for security
            schedule.LobId = lobId;
            schedule.TeamId = teamId;

            // Validate schedule
            ValidateSchedule(schedule);

            // Save to database
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var sql = @"
                    INSERT INTO TestJobSchedules (
                        Id, Name, LobId, TeamId, RepoUrl, TestImageType, 
                        ScheduleType, IntervalMinutes, DaysOfWeek, DaysOfMonth, 
                        TimeOfDay, ScheduledTime, MaxRuns, RunCount, IsActive, CreatedAt
                    ) VALUES (
                        @Id, @Name, @LobId, @TeamId, @RepoUrl, @TestImageType,
                        @ScheduleType, @IntervalMinutes, @DaysOfWeek, @DaysOfMonth,
                        @TimeOfDay, @ScheduledTime, @MaxRuns, @RunCount, @IsActive, @CreatedAt
                    )";

                await connection.ExecuteAsync(sql, new
                {
                    schedule.Id,
                    schedule.Name,
                    schedule.LobId,
                    schedule.TeamId,
                    schedule.RepoUrl,
                    schedule.TestImageType,
                    ScheduleType = schedule.ScheduleType.ToString(),
                    schedule.IntervalMinutes,
                    DaysOfWeek = schedule.DaysOfWeek != null && schedule.DaysOfWeek.Any()
                        ? string.Join(",", schedule.DaysOfWeek)
                        : null,
                    DaysOfMonth = schedule.DaysOfMonth != null && schedule.DaysOfMonth.Any()
                        ? string.Join(",", schedule.DaysOfMonth)
                        : null,
                    schedule.TimeOfDay,
                    schedule.ScheduledTime,
                    schedule.MaxRuns,
                    schedule.RunCount,
                    schedule.IsActive,
                    schedule.CreatedAt
                });
            }

            _logger.LogInformation($"Created schedule {schedule.Id} for LOB {lobId}, Team {teamId}");

            return schedule;
        }

        public async Task<TestJobSchedule> GetScheduleByIdAsync(string id, string lobId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var sql = "SELECT * FROM TestJobSchedules WHERE Id = @Id AND LobId = @LobId";

                var schedule = await connection.QueryFirstOrDefaultAsync<TestJobSchedule>(sql, new { Id = id, LobId = lobId });

                if (schedule != null)
                {
                    /*TODO:PENDING
                    // Parse string lists back to collections
                    if (!string.IsNullOrEmpty(schedule.DaysOfWeek))
                    {
                        schedule.DaysOfWeek = schedule.DaysOfWeek.Split(',').Select(int.Parse).ToList();
                    }

                    if (!string.IsNullOrEmpty(schedule.DaysOfMonth))
                    {
                        schedule.DaysOfMonth = schedule.DaysOfMonth.Split(',').Select(int.Parse).ToList();
                    }
                    */
                }

                return schedule;
            }
        }

        public async Task<List<TestJobSchedule>> GetSchedulesAsync(string lobId, string teamId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var sql = "SELECT * FROM TestJobSchedules WHERE LobId = @LobId AND TeamId = @TeamId";

                var schedules = await connection.QueryAsync<TestJobSchedule>(sql, new { LobId = lobId, TeamId = teamId });

                var result = new List<TestJobSchedule>();

                foreach (var schedule in schedules)
                {
                    /*TODO:PENDING
                    // Parse string lists back to collections
                    if (!string.IsNullOrEmpty(schedule.DaysOfWeek))
                    {
                        schedule.DaysOfWeek = schedule.DaysOfWeek.Split(',').Select(int.Parse).ToList();
                    }
                    else
                    {
                        schedule.DaysOfWeek = new List<int>();
                    }

                    if (!string.IsNullOrEmpty(schedule.DaysOfMonth))
                    {
                        schedule.DaysOfMonth = schedule.DaysOfMonth.Split(',').Select(int.Parse).ToList();
                    }
                    else
                    {
                        schedule.DaysOfMonth = new List<int>();
                    }

                    result.Add(schedule);
                    */
                }

                return result;
            }
        }

        public async Task<TestJobSchedule> UpdateScheduleAsync(string id, string yamlContent, string lobId, string teamId)
        {
            // Validate the existing schedule exists
            var existingSchedule = await GetScheduleByIdAsync(id, lobId);
            if (existingSchedule == null)
            {
                throw new ArgumentException($"Schedule with ID {id} not found for LOB {lobId}");
            }

            // Validate the existing schedule belongs to the team
            if (existingSchedule.TeamId != teamId)
            {
                throw new ArgumentException($"Schedule with ID {id} does not belong to team {teamId}");
            }

            // Deserialize new YAML
            TestJobSchedule updatedSchedule;
            try
            {
                updatedSchedule = _yamlDeserializer.Deserialize<TestJobSchedule>(yamlContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializing updated schedule YAML");
                throw new ArgumentException($"Invalid YAML schedule: {ex.Message}", ex);
            }

            // Keep original ID and metadata, update the rest
            updatedSchedule.Id = id;
            updatedSchedule.LobId = lobId;
            updatedSchedule.TeamId = teamId;
            updatedSchedule.CreatedAt = existingSchedule.CreatedAt;
            updatedSchedule.RunCount = existingSchedule.RunCount;
            updatedSchedule.LastRunTime = existingSchedule.LastRunTime;

            // Validate schedule
            ValidateSchedule(updatedSchedule);

            // Update in database
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var sql = @"
                    UPDATE TestJobSchedules 
                    SET Name = @Name, 
                        RepoUrl = @RepoUrl,
                        TestImageType = @TestImageType,
                        ScheduleType = @ScheduleType,
                        IntervalMinutes = @IntervalMinutes,
                        DaysOfWeek = @DaysOfWeek,
                        DaysOfMonth = @DaysOfMonth,
                        TimeOfDay = @TimeOfDay,
                        ScheduledTime = @ScheduledTime,
                        MaxRuns = @MaxRuns,
                        IsActive = @IsActive
                    WHERE Id = @Id AND LobId = @LobId AND TeamId = @TeamId";

                await connection.ExecuteAsync(sql, new
                {
                    updatedSchedule.Id,
                    updatedSchedule.Name,
                    updatedSchedule.LobId,
                    updatedSchedule.TeamId,
                    updatedSchedule.RepoUrl,
                    updatedSchedule.TestImageType,
                    ScheduleType = updatedSchedule.ScheduleType.ToString(),
                    updatedSchedule.IntervalMinutes,
                    DaysOfWeek = updatedSchedule.DaysOfWeek != null && updatedSchedule.DaysOfWeek.Any()
                        ? string.Join(",", updatedSchedule.DaysOfWeek)
                        : null,
                    DaysOfMonth = updatedSchedule.DaysOfMonth != null && updatedSchedule.DaysOfMonth.Any()
                        ? string.Join(",", updatedSchedule.DaysOfMonth)
                        : null,
                    updatedSchedule.TimeOfDay,
                    updatedSchedule.ScheduledTime,
                    updatedSchedule.MaxRuns,
                    updatedSchedule.IsActive
                });
            }

            _logger.LogInformation($"Updated schedule {updatedSchedule.Id} for LOB {lobId}, Team {teamId}");

            return updatedSchedule;
        }

        public async Task<bool> DeleteScheduleAsync(string id, string lobId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var sql = "DELETE FROM TestJobSchedules WHERE Id = @Id AND LobId = @LobId";
                int rowsAffected = await connection.ExecuteAsync(sql, new { Id = id, LobId = lobId });

                if (rowsAffected > 0)
                {
                    _logger.LogInformation($"Deleted schedule {id} for LOB {lobId}");
                    return true;
                }

                return false;
            }
        }

        public async Task<List<TestJobSchedule>> GetDueSchedulesAsync()
        {
            var now = DateTime.UtcNow;

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var sql = "SELECT * FROM TestJobSchedules WHERE IsActive = 1";

                var schedules = await connection.QueryAsync<TestJobSchedule>(sql);

                var dueSchedules = new List<TestJobSchedule>();

                foreach (var schedule in schedules)
                {
                    /*TODO:PENDING
                    // Parse string lists back to collections
                    if (!string.IsNullOrEmpty(schedule.DaysOfWeek))
                    {
                        schedule.DaysOfWeek = schedule.DaysOfWeek.Split(',').Select(int.Parse).ToList();
                    }
                    else
                    {
                        schedule.DaysOfWeek = new List<int>();
                    }

                    if (!string.IsNullOrEmpty(schedule.DaysOfMonth))
                    {
                        schedule.DaysOfMonth = schedule.DaysOfMonth.Split(',').Select(int.Parse).ToList();
                    }
                    else
                    {
                        schedule.DaysOfMonth = new List<int>();
                    }

                    if (IsDue(schedule, now))
                    {
                        dueSchedules.Add(schedule);
                    }
                    */
                }

                return dueSchedules;
            }
        }

        public async Task UpdateScheduleLastRunAsync(string id, string lobId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var now = DateTime.UtcNow;

                // First, get the current schedule
                var getScheduleSql = "SELECT * FROM TestJobSchedules WHERE Id = @Id AND LobId = @LobId";
                var schedule = await connection.QueryFirstOrDefaultAsync<TestJobSchedule>(getScheduleSql, new { Id = id, LobId = lobId });

                if (schedule == null)
                {
                    throw new ArgumentException($"Schedule with ID {id} not found for LOB {lobId}");
                }

                int runCount = schedule.RunCount + 1;
                bool isActive = schedule.IsActive;

                // Check if we've reached max runs
                if (schedule.MaxRuns.HasValue && runCount >= schedule.MaxRuns.Value)
                {
                    isActive = false;
                }

                // Update the schedule
                var updateSql = @"
                    UPDATE TestJobSchedules 
                    SET LastRunTime = @LastRunTime, RunCount = @RunCount, IsActive = @IsActive
                    WHERE Id = @Id AND LobId = @LobId";

                await connection.ExecuteAsync(updateSql, new
                {
                    LastRunTime = now,
                    RunCount = runCount,
                    IsActive = isActive,
                    Id = id,
                    LobId = lobId
                });

                _logger.LogInformation($"Updated schedule {id} last run time to {now}, run count to {runCount}, is active to {isActive}");
            }
        }

        private bool IsDue(TestJobSchedule schedule, DateTime now)
        {
            if (!schedule.IsActive)
            {
                return false;
            }

            // If max runs is specified and we've reached it, not due
            if (schedule.MaxRuns.HasValue && schedule.RunCount >= schedule.MaxRuns.Value)
            {
                return false;
            }

            // If it's never run before, check if it's due based on creation time
            if (!schedule.LastRunTime.HasValue)
            {
                switch (schedule.ScheduleType)
                {
                    case ScheduleType.RunOnce:
                        return schedule.ScheduledTime.HasValue && now >= schedule.ScheduledTime.Value;

                    case ScheduleType.Interval:
                        // If interval, check if enough time has passed since creation
                        return schedule.IntervalMinutes.HasValue &&
                               now >= schedule.CreatedAt.AddMinutes(schedule.IntervalMinutes.Value);

                    case ScheduleType.Weekly:
                        // If weekly, check if today is one of the days and time has passed
                        if (schedule.DaysOfWeek.Contains((int)now.DayOfWeek) && schedule.TimeOfDay.HasValue)
                        {
                            var scheduledTimeToday = new DateTime(
                                now.Year, now.Month, now.Day,
                                schedule.TimeOfDay.Value.Hours,
                                schedule.TimeOfDay.Value.Minutes, 0);

                            return now >= scheduledTimeToday && scheduledTimeToday >= schedule.CreatedAt;
                        }
                        return false;

                    case ScheduleType.Monthly:
                        // If monthly, check if today is one of the days and time has passed
                        if (schedule.DaysOfMonth.Contains(now.Day) && schedule.TimeOfDay.HasValue)
                        {
                            var scheduledTimeToday = new DateTime(
                                now.Year, now.Month, now.Day,
                                schedule.TimeOfDay.Value.Hours,
                                schedule.TimeOfDay.Value.Minutes, 0);

                            return now >= scheduledTimeToday && scheduledTimeToday >= schedule.CreatedAt;
                        }
                        return false;

                    default:
                        return false;
                }
            }

            // If it has run before, check if it's due based on last run time
            switch (schedule.ScheduleType)
            {
                case ScheduleType.RunOnce:
                    // One-time schedules only run once
                    return false;

                case ScheduleType.Interval:
                    // If interval, check if enough time has passed since last run
                    return schedule.IntervalMinutes.HasValue &&
                           now >= schedule.LastRunTime.Value.AddMinutes(schedule.IntervalMinutes.Value);

                case ScheduleType.Weekly:
                    // If weekly, check if today is one of the days and it hasn't run today
                    if (schedule.DaysOfWeek.Contains((int)now.DayOfWeek) && schedule.TimeOfDay.HasValue)
                    {
                        var scheduledTimeToday = new DateTime(
                            now.Year, now.Month, now.Day,
                            schedule.TimeOfDay.Value.Hours,
                            schedule.TimeOfDay.Value.Minutes, 0);

                        return now >= scheduledTimeToday &&
                               (schedule.LastRunTime.Value.Date != now.Date ||
                                schedule.LastRunTime.Value.TimeOfDay < schedule.TimeOfDay.Value);
                    }
                    return false;

                case ScheduleType.Monthly:
                    // If monthly, check if today is one of the days and it hasn't run today
                    if (schedule.DaysOfMonth.Contains(now.Day) && schedule.TimeOfDay.HasValue)
                    {
                        var scheduledTimeToday = new DateTime(
                            now.Year, now.Month, now.Day,
                            schedule.TimeOfDay.Value.Hours,
                            schedule.TimeOfDay.Value.Minutes, 0);

                        return now >= scheduledTimeToday &&
                               (schedule.LastRunTime.Value.Date != now.Date ||
                                schedule.LastRunTime.Value.TimeOfDay < schedule.TimeOfDay.Value);
                    }
                    return false;

                default:
                    return false;
            }
        }

        private void ValidateSchedule(TestJobSchedule schedule)
        {
            if (string.IsNullOrEmpty(schedule.Name))
            {
                throw new ArgumentException("Schedule name is required");
            }

            if (string.IsNullOrEmpty(schedule.RepoUrl))
            {
                throw new ArgumentException("Repository URL is required");
            }

            if (string.IsNullOrEmpty(schedule.TestImageType))
            {
                throw new ArgumentException("Test image type is required");
            }

            switch (schedule.ScheduleType)
            {
                case ScheduleType.RunOnce:
                    if (!schedule.ScheduledTime.HasValue)
                    {
                        throw new ArgumentException("Scheduled time is required for one-time schedules");
                    }
                    break;

                case ScheduleType.Interval:
                    if (!schedule.IntervalMinutes.HasValue || schedule.IntervalMinutes.Value <= 0)
                    {
                        throw new ArgumentException("Interval minutes must be greater than 0");
                    }
                    break;

                case ScheduleType.Weekly:
                    if (schedule.DaysOfWeek == null || schedule.DaysOfWeek.Count == 0)
                    {
                        throw new ArgumentException("At least one day of week is required for weekly schedules");
                    }

                    if (!schedule.TimeOfDay.HasValue)
                    {
                        throw new ArgumentException("Time of day is required for weekly schedules");
                    }
                    break;

                case ScheduleType.Monthly:
                    if (schedule.DaysOfMonth == null || schedule.DaysOfMonth.Count == 0)
                    {
                        throw new ArgumentException("At least one day of month is required for monthly schedules");
                    }

                    if (!schedule.TimeOfDay.HasValue)
                    {
                        throw new ArgumentException("Time of day is required for monthly schedules");
                    }
                    break;
            }
        }
    }
}