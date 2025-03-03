using System;
using System.Threading.Tasks;
using k8s.Models;
using System;
using System.Threading.Tasks;


namespace TestExecutionPlatform.Core.Services
{
    
    public class TestExecutionService
    {
        private readonly KubernetesService _kubernetesService;
        private readonly string _containerRegistry;

        public TestExecutionService(KubernetesService kubernetesService, string containerRegistry)
        {
            _kubernetesService = kubernetesService;
            _containerRegistry = containerRegistry;
        }

        public async Task<string> CreateTestPodAsync(string repoUrl, string testImageType)
        {
            string imageName = $"{_containerRegistry}/{testImageType.ToLower()}:latest";
            string podName = $"test-pod-{Guid.NewGuid():N}";

            return await _kubernetesService.CreateTestPodAsync(imageName, podName);
        }

        public async Task CloneAndRunTestsAsync(string podName, string repoUrl)
        {
            string cloneAndTestCommand = $"git clone {repoUrl} /app && cd /app && run-tests.sh";
            await _kubernetesService.ExecuteCommandInPodAsync(podName, cloneAndTestCommand);
        }

        public async Task PushTestResultsAsync(string podName)
        {
            // Implement the logic to push test results to a storage location
            // For MVP, you can use Azure Blob Storage or simply log the results
            string copyCommand = "cat /app/TestResults.xml";
            await _kubernetesService.ExecuteCommandInPodAsync(podName, copyCommand);
            // Add logic to store or send the test results
        }

        public async Task CleanupTestPodAsync(string podName)
        {
            await _kubernetesService.DeletePodAsync(podName);
        }
    }
}