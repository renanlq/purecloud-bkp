using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PureCloud.Utils.Domain.Attribute;
using PureCloud.Utils.Infra.Service.Client;
using PureCloud.Utils.Infra.Service.Storage;
using PureCloudPlatform.Client.V2.Model;

namespace PureCloud.Utils.Function.TimeTrigger
{
    public static class RecordingDownload
    {
        [FunctionName("RecordingDownload")]
        [ExceptionFilter(Name = "ConversationRecovery")]
        public async static Task Run(
            [TimerTrigger("*/1 * * * * *", RunOnStartup = false)]TimerInfo myTimer,
            ILogger log)
        {
            // TODO 5. get not processed "table.conversations"
            Domain.Models.Conversation conversation = await TableStorageService.GetNotProcessedConversationAsync();

            if (conversation != null)
            {
                // TODO 6.  /api/v2/conversations/{conversationId}/recordings
                PureCloudClient purecloudClient = new PureCloudClient();
                await purecloudClient.GetAccessToken();

                // Post batch download conversations
                BatchDownloadJobSubmissionResult job = await purecloudClient.BatchRecordingDownloadByConversation(conversation.ConversationId);

                if (!string.IsNullOrEmpty(job.Id))
                {
                    string jobId = job.Id;

                    // Get url for download by JobId
                    BatchDownloadJobStatusResult batch = await purecloudClient.GetJobRecordingDownloadResultByConversation(jobId);

                    log.LogInformation($"Bach with Job id: {jobId}, for callrecordings in conversation: {conversation.ConversationId}");
                    await BlobStorageService.AddCallRecordingAsync(
                        JsonConvert.SerializeObject(batch), "batchs", $"{conversation.ConversationId}.json");

                    if (!batch.Results.Count.Equals(0))
                    {
                        if (batch.ErrorCount.Equals(0))
                        {
                            log.LogInformation($"Total callrecordings to download: {batch.Results.Count}, " +
                                $"for conversation: {conversation.ConversationId}, in JobId: {jobId}");

                            foreach (var item in batch.Results)
                            {
                                await BlobStorageService.AddCallRecordingAsync(
                                    JsonConvert.SerializeObject(item), item.ConversationId, $"{item.RecordingId}.json");

                                // TODO 7. download file and upload to "blob.callrecordings"
                                if (!string.IsNullOrEmpty(item.ResultUrl))
                                {
                                    await BlobStorageService.AddCallRecordingAudioFromUrlAsync(
                                        item.ResultUrl, item.ConversationId, $"{item.RecordingId}.ogg");
                                }
                            }
                        }
                        else if (batch.Results.Count.Equals(1))
                        {
                            log.LogInformation($"No callrecordings for conversation: {conversation.ConversationId}, in JobId: {jobId}");
                            await BlobStorageService.AddErrorFromTextAsync(
                                JsonConvert.SerializeObject(batch), $"{conversation.ConversationId}.json");
                        }
                    }
                    else
                    {
                        log.LogInformation($"No results for conversation: {conversation.ConversationId}, in JobId: {jobId}");
                        await BlobStorageService.AddErrorFromTextAsync(
                            JsonConvert.SerializeObject(batch), $"{conversation.ConversationId}.json");
                    }

                    // TODO 8. update "table.conversations" with uridownload
                    conversation.Processed = true;
                    await TableStorageService.UpdateConversationTableAsync(conversation);
                }
            }
            else
            {
                log.LogInformation($"No conversation to process");
            }
        }
    }
}
