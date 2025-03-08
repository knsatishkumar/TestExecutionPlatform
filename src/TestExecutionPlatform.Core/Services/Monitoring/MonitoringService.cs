using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using TestExecutionPlatform.Core.Services.Containers;

namespace TestExecutionPlatform.Core.Services.Monitoring
{
    public class MonitoringService
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly IKubernetesProvider _kubernetesProvider;
        private readonly ILogger<MonitoringService> _logger;
        private readonly ConfigurationService _configService;
        private readonly AlertService _alertService;

        public MonitoringService(
            TelemetryClient telemetryClient,
            IKubernetesProvider kubernetesProvider,
            ConfigurationService configService,
            AlertService alertService,
            ILogger<MonitoringService> logger)
        {
            _telemetryClient = telemetryClient;
            _kubernetesProvider = kubernetesProvider;
            _configService = configService;
            _alertService = alertService;
            _logger = logger;
        }

        // Track test execution metrics
        public void TrackTestExecution(string jobId, string lobId, string teamId, TimeSpan duration,
                                      int passedTests, int failedTests, int skippedTests, bool success)
        {
            // Track custom event for test execution
            var properties = new Dictionary<string, string>
            {
                { "JobId", jobId },
                { "LobId", lobId },
                { "TeamId", teamId },
                { "Success", success.ToString() }
            };

            var metrics = new Dictionary<string, double>
            {
                { "Duration", duration.TotalMilliseconds },
                { "PassedTests", passedTests },
                { "FailedTests", failedTests },
                { "SkippedTests", skippedTests },
                { "TotalTests", passedTests + failedTests + skippedTests }
            };

            _telemetryClient.TrackEvent("TestExecutionCompleted", properties, metrics);

            // Track individual metrics for dashboard visualization
            _telemetryClient.TrackMetric("TestExecution.Duration", duration.TotalSeconds, properties);
            _telemetryClient.TrackMetric("TestExecution.PassedTests", passedTests, properties);
            _telemetryClient.TrackMetric("TestExecution.FailedTests", failedTests, properties);

            // Track pass rate percentage
            double passRate = 0;
            int totalTests = passedTests + failedTests + skippedTests;
            if (totalTests > 0)
            {
                passRate = (double)passedTests * 100.0 / totalTests;
            }
            _telemetryClient.TrackMetric("TestExecution.PassRate", passRate, properties);

            // Calculate fail rate for alerting
            double failRate = 0;
            if (totalTests > 0)
            {
                failRate = (double)failedTests * 100.0 / totalTests;
            }

            // Evaluate metrics against alert rules
            _alertService.EvaluateMetricAsync("TestExecution.Duration", duration.TotalSeconds, properties);
            _alertService.EvaluateMetricAsync("TestExecution.FailRate", failRate, properties);

            // If a test fails completely, send a specific metric for alerting
            if (!success)
            {
                _alertService.EvaluateMetricAsync("TestExecution.Failed", 1, properties);
            }
        }

        // Collect Kubernetes cluster metrics
        public async Task CollectClusterMetricsAsync()
        {
            try
            {
                var adminConfig = await _configService.GetAdminConfigurationAsync();

                // Get information about all namespaces managed by the platform
                string namespacePrefix = adminConfig.Cluster.LobNamespacePrefix;
                var namespaces = await _kubernetesProvider.ListNamespacesAsync(namespacePrefix);

                int totalPods = 0;
                int runningPods = 0;
                int pendingPods = 0;
                int failedPods = 0;
                int totalJobs = 0;
                int runningJobs = 0;
                int succeededJobs = 0;
                int failedJobs = 0;

                foreach (var ns in namespaces)
                {
                    // Get pod metrics for this namespace
                    var pods = await _kubernetesProvider.ListPodsAsync(ns);
                    totalPods += pods.Count;

                    foreach (var pod in pods)
                    {
                        switch (pod.Status.Phase)
                        {
                            case "Running":
                                runningPods++;
                                break;
                            case "Pending":
                                pendingPods++;
                                break;
                            case "Failed":
                                failedPods++;
                                break;
                        }
                    }

                    // Get job metrics for this namespace
                    var jobs = await _kubernetesProvider.ListJobsAsync(ns);
                    totalJobs += jobs.Count;

                    foreach (var job in jobs)
                    {
                        if (job.Status.Active > 0)
                        {
                            runningJobs++;
                        }
                        else if (job.Status.Succeeded > 0)
                        {
                            succeededJobs++;
                        }
                        else if (job.Status.Failed > 0)
                        {
                            failedJobs++;
                        }
                    }

                    // Track namespace-specific metrics
                    var namespaceProperties = new Dictionary<string, string>
                    {
                        { "Namespace", ns }
                    };

                    _telemetryClient.TrackMetric("Kubernetes.Namespace.Pods", pods.Count, namespaceProperties);
                    _telemetryClient.TrackMetric("Kubernetes.Namespace.Jobs", jobs.Count, namespaceProperties);
                }

                // Track global cluster metrics
                _telemetryClient.TrackMetric("Kubernetes.TotalPods", totalPods);
                _telemetryClient.TrackMetric("Kubernetes.RunningPods", runningPods);
                _telemetryClient.TrackMetric("Kubernetes.PendingPods", pendingPods);
                _telemetryClient.TrackMetric("Kubernetes.FailedPods", failedPods);
                _telemetryClient.TrackMetric("Kubernetes.TotalJobs", totalJobs);
                _telemetryClient.TrackMetric("Kubernetes.RunningJobs", runningJobs);
                _telemetryClient.TrackMetric("Kubernetes.SucceededJobs", succeededJobs);
                _telemetryClient.TrackMetric("Kubernetes.FailedJobs", failedJobs);

                // Track utilization metrics
                var nodes = await _kubernetesProvider.ListNodesAsync();

                int totalNodes = nodes.Count;
                int readyNodes = 0;
                int notReadyNodes = 0;

                foreach (var node in nodes)
                {
                    bool isReady = false;

                    if (node.Status.Conditions != null)
                    {
                        foreach (var condition in node.Status.Conditions)
                        {
                            if (condition.Type == "Ready" && condition.Status == "True")
                            {
                                isReady = true;
                                break;
                            }
                        }
                    }

                    if (isReady)
                    {
                        readyNodes++;
                    }
                    else
                    {
                        notReadyNodes++;
                    }
                }

                _telemetryClient.TrackMetric("Kubernetes.TotalNodes", totalNodes);
                _telemetryClient.TrackMetric("Kubernetes.ReadyNodes", readyNodes);
                _telemetryClient.TrackMetric("Kubernetes.NotReadyNodes", notReadyNodes);

                // Calculate approximate cluster load
                double clusterLoad = (double)runningPods / Math.Max(1, readyNodes * 10);  // Assume avg 10 pods per node
                _telemetryClient.TrackMetric("Kubernetes.ClusterLoad", clusterLoad);

                // Evaluate metrics against alert rules
                await _alertService.EvaluateMetricAsync("Kubernetes.RunningPods", runningPods);
                await _alertService.EvaluateMetricAsync("Kubernetes.PendingPods", pendingPods);
                await _alertService.EvaluateMetricAsync("Kubernetes.FailedPods", failedPods);
                await _alertService.EvaluateMetricAsync("Kubernetes.FailedJobs", failedJobs);
                await _alertService.EvaluateMetricAsync("Kubernetes.ClusterLoad", clusterLoad);
                await _alertService.EvaluateMetricAsync("Kubernetes.NotReadyNodes", notReadyNodes);

                // Add namespace-specific alerts
                foreach (var ns in namespaces)
                {
                    var namespaceDimensions = new Dictionary<string, string>
                    {
                        { "Namespace", ns }
                    };

                    // Get count of pods and jobs in this namespace
                    var pods = await _kubernetesProvider.ListPodsAsync(ns);
                    int namespacePodCount = pods.Count(p => p.Metadata.NamespaceProperty == ns);
                    var jobs = await _kubernetesProvider.ListJobsAsync(ns);
                    int namespaceJobCount = jobs.Count(j => j.Metadata.NamespaceProperty == ns);

                    await _alertService.EvaluateMetricAsync("Kubernetes.Namespace.Pods", namespacePodCount, namespaceDimensions);
                    await _alertService.EvaluateMetricAsync("Kubernetes.Namespace.Jobs", namespaceJobCount, namespaceDimensions);
                }

                _logger.LogInformation("Collected Kubernetes cluster metrics successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting Kubernetes cluster metrics");
            }
        }

        // Track resource usage for a specific job
        public async Task TrackJobResourceUsageAsync(string jobId, string namespaceParam)
        {
            try
            {
                var pods = await _kubernetesProvider.ListPodsAsync(namespaceParam, labelSelector: $"job-name={jobId}");

                foreach (var pod in pods)
                {
                    var podName = pod.Metadata.Name;

                    // In a real implementation, we would query the Kubernetes Metrics API
                    // to get actual resource usage. For this example, we'll use placeholders.
                    double cpuUsage = 0.5;  // Example: 0.5 cores
                    double memoryUsageMB = 256;  // Example: 256MB

                    var properties = new Dictionary<string, string>
                    {
                        { "JobId", jobId },
                        { "Namespace", namespaceParam },
                        { "PodName", podName }
                    };

                    _telemetryClient.TrackMetric("Pod.CpuUsage", cpuUsage, properties);
                    _telemetryClient.TrackMetric("Pod.MemoryUsage", memoryUsageMB, properties);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error tracking resource usage for job {jobId}");
            }
        }

        // Clean up old test results
        public async Task CleanupOldTestResultsAsync()
        {
            try
            {
                // Get the TestResultStorageService from DI in a real implementation
                // For now we'll just log
                _logger.LogInformation("Cleaning up old test results based on retention policy");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up old test results");
            }
        }

        // Clean up completed jobs
        public async Task CleanupCompletedJobsAsync()
        {
            try
            {
                var adminConfig = await _configService.GetAdminConfigurationAsync();
                string lobNamespacePrefix = adminConfig.Cluster.LobNamespacePrefix;

                // Get all namespaces with the prefix
                var namespaces = await _kubernetesProvider.ListNamespacesAsync(lobNamespacePrefix);

                foreach (var ns in namespaces)
                {
                    // For AKS provider, we cast to AksKubernetesProvider to access the method
                    // In a real implementation, we would add this method to the interface
                    if (_kubernetesProvider is AksKubernetesProvider aksProvider)
                    {
                        await aksProvider.CleanupCompletedJobsAsync(ns);
                    }
                    else if (_kubernetesProvider is OpenShiftKubernetesProvider osProvider)
                    {
                        await osProvider.CleanupCompletedJobsAsync(ns);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up completed jobs");
            }
        }

        // Get system health status
        public async Task<SystemHealthStatus> GetSystemHealthAsync()
        {
            var status = new SystemHealthStatus();

            try
            {
                // Check Kubernetes API health
                var nodes = await _kubernetesProvider.ListNodesAsync();

                int totalNodes = nodes.Count;
                int readyNodes = 0;

                foreach (var node in nodes)
                {
                    if (node.Status.Conditions != null)
                    {
                        foreach (var condition in node.Status.Conditions)
                        {
                            if (condition.Type == "Ready" && condition.Status == "True")
                            {
                                readyNodes++;
                                break;
                            }
                        }
                    }
                }

                bool k8sHealthy = readyNodes > 0 && readyNodes == totalNodes;
                status.ComponentStatus.Add("Kubernetes", new ComponentHealth
                {
                    IsHealthy = k8sHealthy,
                    Details = $"{readyNodes}/{totalNodes} nodes ready"
                });

                // Check database connectivity
                bool dbHealthy = true; // In a real implementation, we would check DB connectivity
                status.ComponentStatus.Add("Database", new ComponentHealth
                {
                    IsHealthy = dbHealthy,
                    Details = "Connected"
                });

                // Check blob storage
                bool storageHealthy = true; // In a real implementation, we would check storage connectivity
                status.ComponentStatus.Add("BlobStorage", new ComponentHealth
                {
                    IsHealthy = storageHealthy,
                    Details = "Connected"
                });

                // Check Kafka connectivity if enabled
                var adminConfig = await _configService.GetAdminConfigurationAsync();
                bool messagingHealthy = true; // In a real implementation, we would check messaging connectivity
                status.ComponentStatus.Add("Messaging", new ComponentHealth
                {
                    IsHealthy = messagingHealthy,
                    Details = "Connected"
                });

                // Overall health is the AND of all component health statuses
                status.IsHealthy = k8sHealthy && dbHealthy && storageHealthy && messagingHealthy;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking system health");
                status.IsHealthy = false;
                status.ComponentStatus.Add("Error", new ComponentHealth
                {
                    IsHealthy = false,
                    Details = ex.Message
                });
            }

            return status;
        }
    }

    public class SystemHealthStatus
    {
        public bool IsHealthy { get; set; } = true;
        public Dictionary<string, ComponentHealth> ComponentStatus { get; set; } = new Dictionary<string, ComponentHealth>();
    }

    public class ComponentHealth
    {
        public bool IsHealthy { get; set; }
        public string Details { get; set; }
    }
}