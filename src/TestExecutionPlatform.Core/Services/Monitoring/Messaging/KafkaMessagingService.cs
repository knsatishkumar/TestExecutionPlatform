using System;
using System.Text.Json;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using TestExecutionPlatform.Core.Models;

namespace TestExecutionPlatform.Core.Services.Messaging
{
    public class KafkaMessagingService : IMessagingService
    {
        private readonly string _bootstrapServers;
        private readonly string _testResultsTopic;
        private readonly ILogger _logger;

        public KafkaMessagingService(
            string bootstrapServers,
            string testResultsTopic,
            ILogger logger)
        {
            _bootstrapServers = bootstrapServers;
            _testResultsTopic = testResultsTopic;
            _logger = logger;
        }

        public async Task PublishTestResultMetadataAsync(TestResultMetadataMessage message)
        {
            var config = new ProducerConfig { BootstrapServers = _bootstrapServers };

            try
            {
                using var producer = new ProducerBuilder<string, string>(config).Build();

                string messageJson = JsonSerializer.Serialize(message);

                var kafkaMessage = new Message<string, string>
                {
                    Key = message.JobId,
                    Value = messageJson
                };

                await producer.ProduceAsync(_testResultsTopic, kafkaMessage);

                _logger.LogInformation($"Published test result metadata for job {message.JobId} to Kafka");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error publishing test result metadata for job {message.JobId} to Kafka");
                throw;
            }
        }
    }
}