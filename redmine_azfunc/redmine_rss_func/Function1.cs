using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace redmine_rss_func
{
    public static class Function1
    {
        [FunctionName("RSSPollingFuncLoop")]
        public static async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var rssResult =
                await context.CallActivityAsync<(bool isChanged, IEnumerable<UpdateDocumentItem> updateEntry)>("RSSPollingFunc", null);

            if (rssResult.isChanged)
            {
                await context.CallActivityAsync<(bool isChanged, IEnumerable<UpdateDocumentItem> updateEntry)>("OnDetectChanged", rssResult.updateEntry);

            }

            await context.CreateTimer(context.CurrentUtcDateTime.AddMinutes(1), CancellationToken.None);

            context.ContinueAsNew(null);

        }

        [FunctionName("OnDetectChanged")]
        public static async Task OnDetectChanged([ActivityTrigger] IDurableActivityContext context, ILogger log)
        {
            //更新されたIssueのIDを受け取り、そこから添付ファイルがあるかどうかを取得し、有る場合は添付ファイルをダウンロードする

            var updateItems = context.GetInput<IEnumerable<UpdateDocumentItem>>();

            var inst = new Redmine(log);

            foreach (var entry in updateItems)
            {
                log.LogInformation(entry.ToString());

                var issueIdUrl = entry.IssueId;
                if (issueIdUrl == null)
                {
                    log.LogWarning($"id Get Fail, {entry}");
                    continue;
                }

                var issueIdStr = new Uri(issueIdUrl).PathAndQuery.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();


                if (!int.TryParse(issueIdStr, out var issueId))
                {
                    log.LogWarning($"id Parse Fail, {entry}");
                    continue;
                }

                var attInfo = await inst.GetAttachmentsInfo(issueId, null);

                if (!attInfo.isIncludeAttachments)
                {
                    log.LogInformation($"No Attachments, {entry}");
                    continue;
                }
                
                //TODO:添付ファイルのDL

            }



        }

        [FunctionName("RSSPollingFunc")]
        public static async Task<(bool isChanged, IEnumerable<UpdateDocumentItem> updateEntry)> RSSPollingFunc([ActivityTrigger] IDurableActivityContext context, ILogger log,
            [CosmosDB("RssCheckData", "Items",
                ConnectionStringSetting = "DbRssCheckDataConnectString",
                SqlQuery = "select * from UpdateDocumentItems d ORDER BY d.Updated DESC OFFSET 0 LIMIT 1")]
            IEnumerable<UpdateDocumentItem> updateDocumentLatest,
            [CosmosDB("RssCheckData", "Items",
                ConnectionStringSetting = "DbRssCheckDataConnectString")]
            IAsyncCollector<UpdateDocumentItem> updateDocumentOut)
        {
            log.LogInformation($"RSSPollingFunc Start");
            var updateLatest = updateDocumentLatest.FirstOrDefault();
            log.LogInformation($"RSSPollingFunc Start, Latest={updateLatest}");

            try
            {
                var inst = new Redmine(log);
                var checkResult = await inst.RSSCheck(updateLatest);
                if (checkResult.isChanged)
                {
                    foreach (var item in checkResult.updateEntry)
                    {
                        await updateDocumentOut.AddAsync(item);
                    }
                }

                log.LogInformation($"RSSPollingFunc Result, isChanged={checkResult.isChanged}, UpdateItems={string.Join(", ", checkResult.updateEntry)}");

                return checkResult;
            }
            catch (Exception e)
            {
                log.LogWarning($"Exception, {e}");

                return (false, Array.Empty<UpdateDocumentItem>());
            }
        }

        //[FunctionName("Function1_Hello")]
        //public static string SayHello([ActivityTrigger] string name, ILogger log)
        //{
        //    log.LogInformation($"Saying hello to {name}.");
        //    return $"Hello {name}!";
        //}

        //[FunctionName("RSSPollingStart")]
        //public static async Task Run([TimerTrigger("0 * * * * *")] TimerInfo myTimer,
        //    [DurableClient] IDurableOrchestrationClient starter, ILogger log)
        //{
        //    log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

        //    // Function input comes from the request content.
        //    string instanceId = await starter.StartNewAsync("Function1", null);

        //    log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

        //    return starter.CreateCheckStatusResponse(req, instanceId);
        //}


        [FunctionName("RSSPollingFuncLoop_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("RSSPollingFuncLoop", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName("RSSPollingFuncLoop_HttpStop")]
        public static async Task<HttpResponseMessage> HttpStop(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            var queries = HttpUtility.ParseQueryString(req.RequestUri?.Query);
            var instanceId = queries["instanceid"];

            await starter.TerminateAsync(instanceId, "Http Stop Request");

            log.LogInformation($"Terminated orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}