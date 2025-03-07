using k8s.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestExecutionPlatform.Core.Services.Containers
{
    public interface IKubernetesProvider
    {
        Task<string> CreateTestJobAsync(string imageName, string jobName, string repoUrl, string nameSpace);
        Task<string> GetJobLogsAsync(string jobName, string nameSpace);
        Task DeleteJobAsync(string jobName, string nameSpace);
          Task<V1Job> GetJobAsync(string jobName, string nameSpace);
        Task<bool> IsJobCompletedAsync(string jobName, string nameSpace);
        Task CreateNamespaceIfNotExistsAsync(string namespaceName);
        Task<List<string>> ListNamespacesAsync(string namespacePrefix = null);
        Task<List<V1Pod>> ListPodsAsync(string nameSpace, string labelSelector = null);
        Task<List<V1Job>> ListJobsAsync(string nameSpace, string labelSelector = null);
        Task<List<V1Node>> ListNodesAsync();
    }
 }