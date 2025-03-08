using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using Dapper;
using Microsoft.Extensions.Logging;
using TestExecutionPlatform.Core.Models;
using TestExecutionPlatform.Core.Services.Messaging;
using TestExecutionPlatform.Core.Services.Monitoring;
using TestExecutionPlatform.Core.Services.Storage;

namespace TestExecutionPlatform.Core.Services
{
    public class JobTrackingService
    {
        private readonly string _connectionString;
        private readonly IMessagingService _messagingService;
        private readonly TestResultStorageService _storageService;
        private readonly MonitoringService _monitoringService;
        private readonly ILogger<JobTrackingService> _logger;

        public JobTrackingService(
            string sqlConnectionString,
            IMessagingService messagingService,
            TestResultStorageService storageService,
            MonitoringService monitoringService,
            ILogger<JobTrackingService> logger)
        {
            _connectionString = sqlConnectionString;
            _messagingService = messagingService;
            _storageService = storageService;
            _monitoringService = monitoringService;
            _logger = logger;
        }

        public async Task<string> CreateJobAsync(string lobId, string teamId, string repoUrl, string testImageType, string userId, string scheduleId = null)
        {
            string jobId = Guid.NewGuid().ToString();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var sql = @"
                    INSERT INTO TestJobs (
                        Id, LobId, TeamId, RepoUrl, TestImageType, Status, 
                        StartTime, CreatedById, ScheduleId
                    ) VALUES (
                        @Id, @LobId, @TeamId, @RepoUrl, @TestImageType, @Status,
                        @StartTime, @CreatedById, @ScheduleId
                    )";

                await connection.ExecuteAsync(sql, new
                {
                    Id = jobId,
                    LobId = lobId,
                    TeamId = teamId,
                    RepoUrl = repoUrl,
                    TestImageType = testImageType,
                    Status = "Running",
                    StartTime = DateTime.UtcNow,
                    CreatedById = userId,
                    ScheduleId = scheduleId
                });
            }

            _logger.LogInformation($"Created job {jobId} for LOB {lobId}, Team {teamId}, Repo {repoUrl}, Image {testImageType}");

            return jobId;
        }

        public async Task UpdateJobStatusAsync(string jobId, string status)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var sql = @"
                    UPDATE TestJobs
                    SET Status = @Status
                    WHERE Id = @Id";

                await connection.ExecuteAsync(sql, new
                {
                    Id = jobId,
                    Status = status
                });
            }

            _logger.LogInformation($"Updated job {jobId} status to {status}");
        }

        public async Task CompleteJobAsync(string jobId, string status, string testResultsXml, Stream testResultsFile = null)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Parse test results
                        var (testsPassed, testsFailed, testsSkipped, testResults) = ParseTestResults(testResultsXml, jobId);

                        // Get job details
                        var jobSql = "SELECT * FROM TestJobs WHERE Id = @Id";
                        var job = await connection.QueryFirstOrDefaultAsync<dynamic>(jobSql, new { Id = jobId }, transaction);

                        if (job == null)
                        {
                            throw new ArgumentException($"Job with ID {jobId} not found");
                        }

                        var endTime = DateTime.UtcNow;

                        // Update job
                        var updateJobSql = @"
                            UPDATE TestJobs
                            SET Status = @Status,
                                EndTime = @EndTime,
                                TestsPassed = @TestsPassed,
                                TestsFailed = @TestsFailed,
                                TestsSkipped = @TestsSkipped
                            WHERE Id = @Id";

                        await connection.ExecuteAsync(updateJobSql, new
                        {
                            Id = jobId,
                            Status = status,
                            EndTime = endTime,
                            TestsPassed = testsPassed,
                            TestsFailed = testsFailed,
                            TestsSkipped = testsSkipped
                        }, transaction);

                        // Insert test results
                        if (testResults.Count > 0)
                        {
                            var insertResultsSql = @"
                                INSERT INTO TestResults (
                                    Id, JobId, TestName, Status, Duration, ErrorMessage, StackTrace
                                ) VALUES (
                                    @Id, @JobId, @TestName, @Status, @Duration, @ErrorMessage, @StackTrace
                                )";

                            await connection.ExecuteAsync(insertResultsSql, testResults, transaction);
                        }

                        transaction.Commit();

                        // Store test results file if provided
                        if (testResultsFile != null && testResultsFile.Length > 0)
                        {
                            // Reset stream position
                            testResultsFile.Position = 0;

                            // Store in blob storage
                            await _storageService.StoreTestResultsAsync(
                                job.LobId, job.TeamId, jobId, testResultsFile, "test-results.xml");

                            // Create additional artifact with full log
                            using (var logStream = new MemoryStream())
                            using (var writer = new StreamWriter(logStream))
                            {
                                await writer.WriteLineAsync($"Test Execution Log for Job {jobId}");
                                await writer.WriteLineAsync($"LOB: {job.LobId}");
                                await writer.WriteLineAsync($"Team: {job.TeamId}");
                                await writer.WriteLineAsync($"Repository: {job.RepoUrl}");
                                await writer.WriteLineAsync($"Image Type: {job.TestImageType}");
                                await writer.WriteLineAsync($"Started: {job.StartTime}");
                                await writer.WriteLineAsync($"Completed: {endTime}");
                                await writer.WriteLineAsync($"Duration: {(endTime - job.StartTime).TotalSeconds} seconds");
                                await writer.WriteLineAsync($"Status: {status}");
                                await writer.WriteLineAsync($"Tests Passed: {testsPassed}");
                                await writer.WriteLineAsync($"Tests Failed: {testsFailed}");
                                await writer.WriteLineAsync($"Tests Skipped: {testsSkipped}");
                                await writer.WriteLineAsync("--------------------------------------------------");

                                // Add individual test results
                                foreach (var result in testResults)
                                {
                                    await writer.WriteLineAsync($"Test: {result.TestName}");
                                    await writer.WriteLineAsync($"Status: {result.Status}");
                                    await writer.WriteLineAsync($"Duration: {result.Duration} seconds");

                                    if (result.Status == "Failed" && !string.IsNullOrEmpty(result.ErrorMessage))
                                    {
                                        await writer.WriteLineAsync($"Error: {result.ErrorMessage}");

                                        if (!string.IsNullOrEmpty(result.StackTrace))
                                        {
                                            await writer.WriteLineAsync($"Stack Trace: {result.StackTrace}");
                                        }
                                    }

                                    await writer.WriteLineAsync("--------------------------------------------------");
                                }

                                await writer.FlushAsync();
                                logStream.Position = 0;

                                await _storageService.StoreTestResultsAsync(
                                    job.LobId, job.TeamId, jobId, logStream, "full-log.txt");
                            }
                        }

                        // Track metrics
                        var duration = endTime - job.StartTime;
                        bool success = status == "Succeeded";

                        _monitoringService.TrackTestExecution(
                            jobId,
                            job.LobId,
                            job.TeamId,
                            duration,
                            testsPassed,
                            testsFailed,
                            testsSkipped,
                            success);

                        // Publish test result metadata to Kafka
                        var message = new TestResultMetadataMessage
                        {
                            JobId = jobId,
                            LobId = job.LobId,
                            TeamId = job.TeamId,
                            RepoUrl = job.RepoUrl,
                            TestImageType = job.TestImageType,
                            TotalTests = testsPassed + testsFailed + testsSkipped,
                            PassedTests = testsPassed,
                            FailedTests = testsFailed,
                            SkippedTests = testsSkipped,
                            StartTime = job.StartTime,
                            EndTime = endTime,
                            DurationInSeconds = duration.TotalSeconds
                        };

                        await _messagingService.PublishTestResultMetadataAsync(message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error completing job {jobId}");
                        transaction.Rollback();
                        throw;
                    }
                }
            }

            _logger.LogInformation($"Completed job {jobId} with status {status}");
        }

        private (int TestsPassed, int TestsFailed, int TestsSkipped, List<TestResult> TestResults) ParseTestResults(string testResultsXml, string jobId)
        {
            int testsPassed = 0;
            int testsFailed = 0;
            int testsSkipped = 0;
            var testResults = new List<TestResult>();

            try
            {
                var doc = XDocument.Parse(testResultsXml);
                var testElements = doc.Descendants("test");

                foreach (var test in testElements)
                {
                    string testName = test.Attribute("name")?.Value ?? "Unknown";
                    string result = test.Attribute("result")?.Value ?? "Unknown";
                    string duration = test.Attribute("duration")?.Value ?? "0";
                    string errorMessage = test.Element("failure")?.Element("message")?.Value;
                    string stackTrace = test.Element("failure")?.Element("stack-trace")?.Value;

                    double testDuration;
                    if (!double.TryParse(duration, out testDuration))
                    {
                        testDuration = 0;
                    }

                    string status;
                    switch (result.ToLower())
                    {
                        case "pass":
                        case "passed":
                            status = "Passed";
                            testsPassed++;
                            break;
                        case "fail":
                        case "failed":
                            status = "Failed";
                            testsFailed++;
                            break;
                        case "skip":
                        case "skipped":
                        case "ignored":
                            status = "Skipped";
                            testsSkipped++;
                            break;
                        default:
                            status = "Unknown";
                            break;
                    }

                    testResults.Add(new TestResult
                    {
                        Id = Guid.NewGuid().ToString(),
                        JobId = jobId,
                        TestName = testName,
                        Status = status,
                        Duration = testDuration,
                        ErrorMessage = errorMessage,
                        StackTrace = stackTrace
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error parsing test results XML for job {jobId}");
                // If XML parsing fails, we still want to complete the job
                // Just with no detailed test results
            }

            return (testsPassed, testsFailed, testsSkipped, testResults);
        }
    }
}