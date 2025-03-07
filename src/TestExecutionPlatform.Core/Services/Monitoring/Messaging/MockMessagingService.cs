using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestExecutionPlatform.Core.Models;

namespace TestExecutionPlatform.Core.Services.Messaging
{
    public class MockMessagingService : IMessagingService
    {
        private readonly ILogger _logger;

        public MockMessagingService(ILogger logger)
        {
            _logger = logger;
        }

        public Task PublishTestResultMetadataAsync(TestResultMetadataMessage message)
        {
            _logger.LogInformation($"[MOCK] Published test result metadata for job {message.JobId}");
            return Task.CompletedTask;
        }
    }
}