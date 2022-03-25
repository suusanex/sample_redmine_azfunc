using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NUnit.Framework;
using redmine_rss_func;

namespace test_redmine_rss_func
{
    [TestFixture]
    public class RSSPollingFuncTest
    {
        [SetUp]
        public void SetUp()
        {
            m_LoggerFactory = LoggerFactory.Create(build =>
            {
                build.AddConsole();
            });
            m_Logger = m_LoggerFactory.CreateLogger<RSSPollingFuncTest>();

        }

        ILoggerFactory m_LoggerFactory;
        private ILogger m_Logger;

        [Test]
        public async Task Test1()
        {
            m_Logger.LogInformation("Start");
            
            var target = new Redmine(m_Logger);

            var ret = await target.RSSCheck(null);

            foreach (var entry in ret.updateEntry)
            {
                m_Logger.LogInformation(entry.ToString());
                
                var issueIdUrl = entry.Id;
                if (issueIdUrl == null)
                {
                    m_Logger.LogWarning($"id Get Fail, {entry}");
                    continue;
                }

                var issueIdStr = new Uri(issueIdUrl).PathAndQuery.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();


                Assert.That(int.TryParse(issueIdStr, out var issueId), Is.True);

                var ret2 = await target.GetAttachmentsInfo(issueId, null);

                Assert.That(ret2.isIncludeAttachments);

                Assert.That(ret2.attachmentsUrls.First(), Is.EqualTo("http://redmine-test1-server.japaneast.cloudapp.azure.com/attachments/download/1/testdata.txt"));

            }

        }

    }
}