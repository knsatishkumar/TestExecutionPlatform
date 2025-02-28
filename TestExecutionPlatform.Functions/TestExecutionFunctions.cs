using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;

using System.IO;
using System.Threading.Tasks;
using TestExecutionPlatform.Core.Services;


namespace TestExecutionPlatform.Functions
{
    

    public static class TestExecutionFunctions
    {
        private static readonly IConfiguration _config;
        private static readonly KubernetesService _kubernetesService;
        private static readonly TestExecutionService _testExecutionService;

        static TestExecutionFunctions()
        {
            _config = new ConfigurationBuilder()
                .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            string kubeConfigPath = _config["KubeConfigPath"];
            string containerRegistry = _config["ContainerRegistry"];

            _kubernetesService = new KubernetesService(kubeConfigPath);
            _testExecutionService = new TestExecutionService(_kubernetesService, containerRegistry);
        }

        [FunctionName("CreateTestPod")]
        public static async Task<IActionResult> CreateTestPod(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Creating test pod.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string repoUrl = data?.repoUrl;
            string testImageType = data?.testImageType;

            if (string.IsNullOrEmpty(repoUrl) || string.IsNullOrEmpty(testImageType))
            {
                return new BadRequestObjectResult("Please provide repoUrl and testImageType in the request body");
            }

            string podName = await _testExecutionService.CreateTestPodAsync(repoUrl, testImageType);
            return new OkObjectResult($"Test pod created: {podName}");
        }

        [FunctionName("CloneAndRunTests")]
        public static async Task<IActionResult> CloneAndRunTests(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Cloning repository and running tests.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string podName = data?.podName;
            string repoUrl = data?.repoUrl;

            if (string.IsNullOrEmpty(podName) || string.IsNullOrEmpty(repoUrl))
            {
                return new BadRequestObjectResult("Please provide podName and repoUrl in the request body");
            }

            await _testExecutionService.CloneAndRunTestsAsync(podName, repoUrl);
            return new OkObjectResult("Tests executed successfully");
        }

        [FunctionName("PushTestResults")]
        public static async Task<IActionResult> PushTestResults(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            [Queue("cleanup-queue")] IAsyncCollector<string> cleanupQueue,
            ILogger log)
        {
            log.LogInformation("Pushing test results.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string podName = data?.podName;

            if (string.IsNullOrEmpty(podName))
            {
                return new BadRequestObjectResult("Please provide podName in the request body");
            }

            await _testExecutionService.PushTestResultsAsync(podName);

            // Add the podName to the cleanup queue
            await cleanupQueue.AddAsync(podName);

            return new OkObjectResult("Test results pushed successfully and cleanup scheduled");
        }

        [FunctionName("CleanupTestPod")]
        public static async Task<IActionResult> CleanupTestPod(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Cleaning up test pod.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string podName = data?.podName;

            if (string.IsNullOrEmpty(podName))
            {
                return new BadRequestObjectResult("Please provide podName in the request body");
            }

            await _testExecutionService.CleanupTestPodAsync(podName);
            return new OkObjectResult($"Test pod {podName} cleaned up successfully");
        }

        [FunctionName("ProcessCleanupQueue")]
        public static async Task ProcessCleanupQueue(
            [QueueTrigger("cleanup-queue")] string podName,
            ILogger log)
        {
            log.LogInformation($"Processing cleanup for pod: {podName}");

            await _testExecutionService.CleanupTestPodAsync(podName);

            log.LogInformation($"Cleanup completed for pod: {podName}");
        }
    }
}