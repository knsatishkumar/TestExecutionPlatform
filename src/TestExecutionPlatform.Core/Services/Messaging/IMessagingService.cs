using System.Threading.Tasks;
using TestExecutionPlatform.Core.Models;

namespace TestExecutionPlatform.Core.Services.Messaging
{
    public interface IMessagingService
    {
        Task PublishTestResultMetadataAsync(TestResultMetadataMessage message);
    }
}