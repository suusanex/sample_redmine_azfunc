using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace redmine_rss_func
{
    public class Redmine
    {
        public Redmine(ILogger log)
        {
            m_Log = log;
        }

        private string m_RedmineRootUrl = "http://redmine-test1-server.japaneast.cloudapp.azure.com/";
        private string m_RedmineProjectRootUrl = "http://redmine-test1-server.japaneast.cloudapp.azure.com/projects/testproject1/";
        private readonly ILogger m_Log;


        public async Task<(bool isChanged, IEnumerable<XElement> updateEntry)> RSSCheck()
        {
            var rssUrl = $"{m_RedmineProjectRootUrl}activity.atom";

            var userName = "testuser1";

            var password = "urG8p7dq";

            var atomKey = "f6012197692df72c5bb59698430e8143aeff13bb";

            using var client = new HttpClient();
            {
                var res = await client.GetAsync($"{rssUrl}?key={atomKey}");
                if (!res.IsSuccessStatusCode)
                {
                    throw new Exception($"RSS Get Fail, {nameof(res.StatusCode)}={res.StatusCode}, {await res.Content.ReadAsStringAsync()} {rssUrl}");
                }

                var rssStr = await res.Content.ReadAsStringAsync();

                var xdoc = XDocument.Parse(rssStr);
                var xns = xdoc.Root.Name.Namespace;
                var entries = xdoc.Descendants(xns + "entry");

                //TODO:とりあえず、最新のエントリを常に「更新あり」とする
                var updateEntry = entries.Take(1);

                return (true, updateEntry);

            }


        }

        public async Task<(bool isIncludeAttachments, IEnumerable<string> attachmentsUrls)> GetAttachmentsInfo(
            int issueId, int? journalId)
        {
            var apiKey = "6f81e2188cb2183f3244791d85c5cccee1818221";

            if (journalId == null)
            {
                //issue自体の添付ファイルを取る
                using var client = new HttpClient();
                {
                    var attachmentIds = new List<int>();

                    {
                        var reqUrl = $"{m_RedmineRootUrl}issues/{issueId}.xml?include=journals";
                        var req = new HttpRequestMessage(HttpMethod.Get, reqUrl);
                        req.Headers.Add("X-Redmine-API-Key", apiKey);

                        var res = await client.SendAsync(req);
                        if (!res.IsSuccessStatusCode)
                        {
                            throw new Exception(
                                $"issue ID={issueId} Get Fail, {nameof(res.StatusCode)}={res.StatusCode}, {await res.Content.ReadAsStringAsync()} {reqUrl}");
                        }

                        var resStr = await res.Content.ReadAsStringAsync();
                        m_Log.LogInformation($"get xml {resStr}");
                        var xdoc = XDocument.Parse(resStr);
                        var xns = xdoc.Root.Name.Namespace;

                        var journals = xdoc.Descendants(xns + "journals").FirstOrDefault();
                        if (journals != null)
                        {
                            foreach (var journal in journals.Elements())
                            {
                                var details = journal.Element(xns + "details");
                                if(details?.Element(xns + "property")?.Value != "attachment") continue;

                                if(int.TryParse(details.Element(xns + "name")?.Value, out int id))
                                {
                                    m_Log.LogInformation($"Attachment Found Id={id}");
                                    attachmentIds.Add(id);
                                }
                            }
                        }
                    }

                    var attachmentUrls = new List<string>();
                    foreach (var attachmentId in attachmentIds)
                    {
                        
                        var reqUrl = $"{m_RedmineProjectRootUrl}attachments/{attachmentId}.xml";
                        var res = await client.GetAsync(reqUrl);
                        if (!res.IsSuccessStatusCode)
                        {
                            throw new Exception(
                                $"attachment ID={attachmentId} Get Fail, {nameof(res.StatusCode)}={res.StatusCode}, {await res.Content.ReadAsStringAsync()} {reqUrl}");
                        }

                        var resStr = await res.Content.ReadAsStringAsync();

                        var xdoc = XDocument.Parse(resStr);
                        var xns = xdoc.Root.Name.Namespace;

                        attachmentUrls.Add(xdoc.Descendants(xns + "content_url").First().Value);

                    }

                    return (attachmentUrls.Any(), attachmentUrls);

                }



            }
            else
            {
                //issueの特定journalの添付ファイルを取る

                //var content = new StringContent(reqJson.ToString(), Encoding.UTF8, "application/json");
                //content.Headers.Add("X-Redmine-API-Key", apiKey);
                throw new NotImplementedException();

            }
        }

    }
}
