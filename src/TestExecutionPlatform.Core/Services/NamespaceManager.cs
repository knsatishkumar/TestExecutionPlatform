using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestExecutionPlatform.Core.Services.Containers;

namespace TestExecutionPlatform.Core.Services
{
    public class NamespaceManager
    {
        private readonly IKubernetesProvider _kubernetesProvider;
        private readonly ConfigurationService _configService;
        private readonly ILogger<NamespaceManager> _logger;

        public NamespaceManager(
            IKubernetesProvider kubernetesProvider,
            ConfigurationService configService,
            ILogger<NamespaceManager> logger)
        {
            _kubernetesProvider = kubernetesProvider;
            _configService = configService;
            _logger = logger;
        }

        public async Task<string> GetNamespaceForLobAsync(string lobId)
        {
            var adminConfig = await _configService.GetAdminConfigurationAsync();
            string prefix = adminConfig.Cluster.LobNamespacePrefix;

            return $"{prefix}{lobId.ToLower()}";
        }

        public string GetNamespaceForLob(string lobId)
        {
            // Synchronous version that doesn't require async/await
            // This is a simplification - in production, we'd want to cache the prefix
            string prefix = "testexec-"; // Default value
            try
            {
                var adminConfig = _configService.GetAdminConfigurationAsync().GetAwaiter().GetResult();
                prefix = adminConfig.Cluster.LobNamespacePrefix;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting namespace prefix from admin config, using default");
            }

            return $"{prefix}{lobId.ToLower()}";
        }

        public async Task EnsureNamespaceExistsAsync(string lobId)
        {
            string namespaceName = await GetNamespaceForLobAsync(lobId);
            await _kubernetesProvider.CreateNamespaceIfNotExistsAsync(namespaceName);
        }
    }
}