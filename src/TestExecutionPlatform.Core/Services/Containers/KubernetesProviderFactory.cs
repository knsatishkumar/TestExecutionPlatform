//using AutoMapper.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;

namespace TestExecutionPlatform.Core.Services.Containers
{
    public class KubernetesProviderFactory
    {
        public static IKubernetesProvider CreateProvider(IConfiguration configuration, ConfigurationService configService = null, ILogger logger = null)
        {
            string providerType = configuration["KubernetesConfig:Provider"] ?? "AKS";
            string kubeConfigPath = configuration["KubernetesConfig:KubeConfigPath"];

            if (string.IsNullOrEmpty(kubeConfigPath))
            {
                throw new ArgumentException("KubeConfigPath must be specified in configuration");
            }

            return providerType.ToUpperInvariant() switch
            {
                "AKS" => new AksKubernetesProvider(kubeConfigPath, configService, logger),
                "OPENSHIFT" => new OpenShiftKubernetesProvider(kubeConfigPath, configService, logger),
                _ => throw new ArgumentException($"Unsupported Kubernetes provider: {providerType}")
            };
        }
    }
}