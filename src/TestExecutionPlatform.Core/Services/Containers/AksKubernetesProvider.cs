using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestExecutionPlatform.Core.Services;

namespace TestExecutionPlatform.Core.Services.Containers
{
    public class AksKubernetesProvider : IKubernetesProvider
    {
        private readonly Kubernetes _client;
        private readonly ConfigurationService _configService;
        private readonly ILogger _logger;

        public AksKubernetesProvider(string kubeConfigPath, ConfigurationService configService, ILogger logger = null)
        {
            var config = KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeConfigPath);
            _client = new Kubernetes(config);
            _configService = configService;
            _logger = logger;
        }

        public async Task CreateNamespaceIfNotExistsAsync(string namespaceName)
        {
            try
            {
                await _client.ReadNamespaceAsync(namespaceName);
                // Namespace exists, do nothing
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var namespaceObj = new V1Namespace
                {
                    Metadata = new V1ObjectMeta
                    {
                        Name = namespaceName
                    }
                };

                await _client.CreateNamespaceAsync(namespaceObj);
                _logger?.LogInformation($"Created namespace: {namespaceName}");
            }
        }

        public async Task<string> CreateTestJobAsync(string imageName, string jobName, string repoUrl, string namespaceParam)
        {
            // Get admin configuration for resource limits
            var adminConfig = await _configService.GetAdminConfigurationAsync();
            var resourceLimits = adminConfig.ResourceManagement.DefaultContainerLimits;
            int timeoutMinutes = adminConfig.ResourceManagement.DefaultJobTimeoutMinutes;

            var job = new V1Job
            {
                Metadata = new V1ObjectMeta
                {
                    Name = jobName
                },
                Spec = new V1JobSpec
                {
                    // Set job timeout
                    ActiveDeadlineSeconds = timeoutMinutes * 60,

                    Template = new V1PodTemplateSpec
                    {
                        Spec = new V1PodSpec
                        {
                            RestartPolicy = "Never",
                            Containers = new List<V1Container>
                            {
                                new V1Container
                                {
                                    Name = "test-container",
                                    Image = imageName,
                                    Env = new List<V1EnvVar>
                                    {
                                        new V1EnvVar
                                        {
                                            Name = "REPO_URL",
                                            Value = repoUrl
                                        }
                                    },
                                    Command = new List<string>
                                    {
                                        "/bin/sh",
                                        "-c",
                                        "./run-tests.sh"
                                    },
                                    Resources = new V1ResourceRequirements
                                    {
                                        Limits = new Dictionary<string, ResourceQuantity>
                                        {
                                            { "cpu", new ResourceQuantity(resourceLimits.CpuLimit) },
                                            { "memory", new ResourceQuantity(resourceLimits.MemoryLimit) }
                                        },
                                        Requests = new Dictionary<string, ResourceQuantity>
                                        {
                                            { "cpu", new ResourceQuantity(resourceLimits.CpuRequest) },
                                            { "memory", new ResourceQuantity(resourceLimits.MemoryRequest) }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var createdJob = await _client.CreateNamespacedJobAsync(job, namespaceParam);
            return createdJob.Metadata.Name;
        }

        public async Task DeleteJobAsync(string jobName, string namespaceParam)
        {
            await _client.DeleteNamespacedJobAsync(
                jobName,
                namespaceParam,
                propagationPolicy: "Background"); // Delete pods as well
        }

        public async Task<V1Job> GetJobAsync(string jobName, string namespaceParam)
        {
            return await _client.ReadNamespacedJobAsync(jobName, namespaceParam);
        }

        public async Task<string> GetJobLogsAsync(string jobName, string namespaceParam)
        {
            var pods = await _client.ListNamespacedPodAsync(namespaceParam, labelSelector: $"job-name={jobName}");
            if (pods.Items.Count > 0)
            {
                var podName = pods.Items[0].Metadata.Name;
                using var response = await _client.ReadNamespacedPodLogAsync(podName, namespaceParam);
                using var reader = new StreamReader(response);
                return await reader.ReadToEndAsync();
            }
            return "No logs found";
        }

        public async Task<bool> IsJobCompletedAsync(string jobName, string namespaceParam)
        {
            var job = await _client.ReadNamespacedJobAsync(jobName, namespaceParam);
            return job.Status.Succeeded.GetValueOrDefault() > 0;
        }

        public async Task<List<string>> ListNamespacesAsync(string namespacePrefix = null)
        {
            var namespaces = await _client.ListNamespaceAsync();
            var result = new List<string>();

            foreach (var ns in namespaces.Items)
            {
                string name = ns.Metadata.Name;
                if (string.IsNullOrEmpty(namespacePrefix) || name.StartsWith(namespacePrefix))
                {
                    result.Add(name);
                }
            }

            return result;
        }

        public async Task<List<V1Pod>> ListPodsAsync(string namespaceParam, string labelSelector = null)
        {
            var pods = await _client.ListNamespacedPodAsync(namespaceParam, labelSelector: labelSelector);
            return pods.Items.ToList();
        }

        public async Task<List<V1Job>> ListJobsAsync(string namespaceParam, string labelSelector = null)
        {
            var jobs = await _client.ListNamespacedJobAsync(namespaceParam, labelSelector: labelSelector);
            return jobs.Items.ToList();
        }

        public async Task<List<V1Node>> ListNodesAsync()
        {
            var nodes = await _client.ListNodeAsync();
            return nodes.Items.ToList();
        }

        // Add a method to implement auto-cleanup of completed jobs
        public async Task CleanupCompletedJobsAsync(string namespaceParam)
        {
            // Get admin configuration for auto-cleanup
            var adminConfig = await _configService.GetAdminConfigurationAsync();

            if (!adminConfig.ResourceManagement.AutoCleanupJobs)
            {
                // Auto-cleanup is disabled in admin config
                return;
            }

            int cleanupAfterHours = adminConfig.ResourceManagement.CleanupAfterHours;
            var cutoffTime = DateTime.UtcNow.AddHours(-cleanupAfterHours);

            var jobs = await _client.ListNamespacedJobAsync(namespaceParam);

            foreach (var job in jobs.Items)
            {
                // Check if job is completed and old enough for cleanup
                if ((job.Status.Succeeded > 0 || job.Status.Failed > 0) &&
                    job.Status.CompletionTime.HasValue &&
                    job.Status.CompletionTime.Value < cutoffTime)
                {
                    try
                    {
                        await _client.DeleteNamespacedJobAsync(
                            job.Metadata.Name,
                            namespaceParam,
                            propagationPolicy: "Background"); // Delete pods as well

                        _logger?.LogInformation($"Auto-cleaned up completed job {job.Metadata.Name} in namespace {namespaceParam}");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, $"Error cleaning up job {job.Metadata.Name} in namespace {namespaceParam}");
                    }
                }
            }
        }
    }
}