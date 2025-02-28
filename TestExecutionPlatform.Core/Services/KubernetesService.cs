using k8s;
using k8s.Models;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;



namespace TestExecutionPlatform.Core.Services
{
    
    public class KubernetesService
    {
        private readonly Kubernetes _client;

        public KubernetesService(string kubeConfigPath)
        {
            var config = KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeConfigPath);
            _client = new Kubernetes(config);
        }

        public async Task<string> CreateTestPodAsync(string imageName, string podName)
        {
            var pod = new V1Pod
            {
                Metadata = new V1ObjectMeta
                {
                    Name = podName
                },
                Spec = new V1PodSpec
                {
                    Containers = new[]
                    {
                    new V1Container
                    {
                        Name = "test-container",
                        Image = imageName,
                        Command = new[] { "/bin/sh", "-c", "while true; do sleep 30; done;" }
                    }
                }
                }
            };

            var createdPod = await _client.CreateNamespacedPodAsync(pod, "default");
            return createdPod.Metadata.Name;
        }

        public async Task ExecuteCommandInPodAsync(string podName, string command)
        {
            string[] commandArray = { "/bin/sh", "-c", command };

            var response = await _client.NamespacedPodExecAsync(
                podName,
                "default",
                container: "test-container",
                command: commandArray,
                tty: false,
                action: null,
                cancellationToken: CancellationToken.None);


            // Handle the response if needed
        }

        public async Task DeletePodAsync(string podName)
        {
            await _client.DeleteNamespacedPodAsync(podName, "default");
        }
    }
}