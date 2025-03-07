using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Dapper;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace TestExecutionPlatform.Core.Services
{
    public class ReportingService
    {
        private readonly string _connectionString;

        public ILogger<ReportingService> Logger { get; }

        public ReportingService(string sqlConnectionString)
        {
            _connectionString = sqlConnectionString;
        }

        public ReportingService(string sqlConnectionString, ILogger<ReportingService> logger) : this(sqlConnectionString)
        {
            Logger = logger;
        }

        public async Task<TestExecutionSummary> GetExecutionSummaryAsync(string lobId = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var whereClause = BuildWhereClause(lobId, null, null, startDate, endDate);

                var sql = $@"
                    SELECT 
                        COUNT(*) AS TotalJobs,
                        SUM(CASE WHEN Status = 'Succeeded' THEN 1 ELSE 0 END) AS SucceededJobs,
                        SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END) AS FailedJobs,
                        SUM(CASE WHEN Status = 'Running' THEN 1 ELSE 0 END) AS RunningJobs,
                        AVG(DATEDIFF(SECOND, StartTime, EndTime)) AS AvgDurationInSeconds
                    FROM TestJobs
                    {whereClause}";

                return await connection.QueryFirstOrDefaultAsync<TestExecutionSummary>(sql);
            }
        }

        public async Task<List<LobExecutionSummary>> GetLobExecutionSummaryAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var whereClause = BuildWhereClause(null, null, null, startDate, endDate);

                var sql = $@"
                    SELECT 
                        LobId,
                        COUNT(*) AS TotalJobs,
                        SUM(CASE WHEN Status = 'Succeeded' THEN 1 ELSE 0 END) AS SucceededJobs,
                        SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END) AS FailedJobs,
                        AVG(DATEDIFF(SECOND, StartTime, EndTime)) AS AvgDurationInSeconds
                    FROM TestJobs
                    {whereClause}
                    GROUP BY LobId
                    ORDER BY TotalJobs DESC";

                return (await connection.QueryAsync<LobExecutionSummary>(sql)).ToList();
            }
        }

        public async Task<List<TestJob>> GetJobsAsync(
            string lobId = null,
            string teamId = null,
            string jobId = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            string status = null,
            int pageSize = 50,
            int pageNumber = 1)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var whereClause = BuildWhereClause(lobId, teamId, jobId, startDate, endDate, status);

                var sql = $@"
                    SELECT *
                    FROM TestJobs
                    {whereClause}
                    ORDER BY StartTime DESC
                    OFFSET {(pageNumber - 1) * pageSize} ROWS
                    FETCH NEXT {pageSize} ROWS ONLY";

                return (await connection.QueryAsync<TestJob>(sql)).ToList();
            }
        }

        public async Task<int> GetJobsCountAsync(
            string lobId = null,
            string teamId = null,
            string jobId = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            string status = null)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var whereClause = BuildWhereClause(lobId, teamId, jobId, startDate, endDate, status);

                var sql = $@"
                    SELECT COUNT(*)
                    FROM TestJobs
                    {whereClause}";

                return await connection.ExecuteScalarAsync<int>(sql);
            }
        }

        public async Task<List<FailureAnalysis>> GetTopFailingTestsAsync(
            string lobId = null,
            string teamId = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int limit = 10)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var whereClause = BuildWhereClause(lobId, teamId, null, startDate, endDate, "Failed");

                var sql = $@"
                    SELECT 
                        TestName,
                        COUNT(*) AS FailureCount,
                        MAX(StartTime) AS MostRecentFailure
                    FROM TestResults
                    INNER JOIN TestJobs ON TestResults.JobId = TestJobs.Id
                    {whereClause}
                    AND TestResults.Status = 'Failed'
                    GROUP BY TestName
                    ORDER BY FailureCount DESC
                    OFFSET 0 ROWS
                    FETCH NEXT {limit} ROWS ONLY";

                return (await connection.QueryAsync<FailureAnalysis>(sql)).ToList();
            }
        }

        private string BuildWhereClause(
            string lobId = null,
            string teamId = null,
            string jobId = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            string status = null)
        {
            var conditions = new List<string>();

            if (!string.IsNullOrEmpty(lobId))
            {
                conditions.Add($"LobId = '{lobId}'");
            }

            if (!string.IsNullOrEmpty(teamId))
            {
                conditions.Add($"TeamId = '{teamId}'");
            }

            if (!string.IsNullOrEmpty(jobId))
            {
                conditions.Add($"Id = '{jobId}'");
            }

            if (startDate.HasValue)
            {
                conditions.Add($"StartTime >= '{startDate.Value:yyyy-MM-dd HH:mm:ss}'");
            }

            if (endDate.HasValue)
            {
                conditions.Add($"StartTime <= '{endDate.Value:yyyy-MM-dd HH:mm:ss}'");
            }

            if (!string.IsNullOrEmpty(status))
            {
                conditions.Add($"Status = '{status}'");
            }

            if (conditions.Count > 0)
            {
                return "WHERE " + string.Join(" AND ", conditions);
            }

            return string.Empty;
        }
    }

    public class TestExecutionSummary
    {
        public int TotalJobs { get; set; }
        public int SucceededJobs { get; set; }
        public int FailedJobs { get; set; }
        public int RunningJobs { get; set; }
        public double AvgDurationInSeconds { get; set; }
    }

    public class LobExecutionSummary
    {
        public string LobId { get; set; }
        public int TotalJobs { get; set; }
        public int SucceededJobs { get; set; }
        public int FailedJobs { get; set; }
        public double AvgDurationInSeconds { get; set; }
    }

    public class TestJob
    {
        public string Id { get; set; }
        public string LobId { get; set; }
        public string TeamId { get; set; }
        public string RepoUrl { get; set; }
        public string TestImageType { get; set; }
        public string Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int TestsPassed { get; set; }
        public int TestsFailed { get; set; }
        public int TestsSkipped { get; set; }
    }

    public class FailureAnalysis
    {
        public string TestName { get; set; }
        public int FailureCount { get; set; }
        public DateTime MostRecentFailure { get; set; }
    }
}