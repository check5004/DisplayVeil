using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DisplayVeil.Tests
{
    internal static class UpdateTests
    {
        private const string Json = "{\"tag_name\":\"v1.1.0\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://example.invalid/\",\"assets\":[{\"name\":\"DisplayVeil-1.1.0-win.zip\",\"state\":\"uploaded\"}]}";
        private static readonly DateTime Now = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        private static void Assert(bool value) { if (!value) throw new Exception("Update assertion failed"); }
        private static void Check(UpdateMonitor monitor, bool manual) { monitor.CheckAsync(manual, Now).GetAwaiter().GetResult(); }
        private static Task<UpdateRelease> Latest(CancellationToken token) { return Task.FromResult(UpdateRelease.FromTag("v1.1.0")); }
        private sealed class Handler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> Send;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            { token.ThrowIfCancellationRequested(); return Task.FromResult(Send(request)); }
        }
        public static void RunAll(Action<string, Action> run)
        {
            run("Release notes preserve Japanese, line breaks and empty or missing bodies", delegate
            {
                string json = Json.Replace("\"draft\":false", "\"body\":\"## 変更内容\\n- 日本語の修正\\n- **表示**を改善\",\"draft\":false");
                Assert(UpdateClient.Parse(json).Notes == "## 変更内容\n- 日本語の修正\n- **表示**を改善");
                Assert(UpdateClient.Parse(Json).Notes == "");
                Assert(UpdateClient.Parse(Json.Replace("\"draft\":false", "\"body\":null,\"draft\":false")).Notes == "");
                Assert(UpdateRelease.FromTag("v1.1.0").Notes == null);
            });
            run("Latest release notes remain available for current or newer installed versions", delegate
            {
                var release = UpdateRelease.FromTag("v1.1.0", "修正内容");
                using (var monitor = new UpdateMonitor(new Settings(), new Version(1, 2, 0, 0), delegate { return Task.FromResult(release); }, delegate { }))
                {
                    Check(monitor, true);
                    Assert(monitor.Available == null && monitor.Latest == release && monitor.CurrentVersion == new Version(1, 2, 0, 0));
                }
            });
            run("Update release uses numeric version and a fixed GitHub destination", delegate
            {
                var release = UpdateClient.Parse(Json);
                Assert(release.Tag == "v1.1.0" && release.PageUrl == "https://github.com/check5004/DisplayVeil/releases/tag/v1.1.0");
                Assert(UpdateRelease.FromTag("v1.10.0").Version > UpdateRelease.FromTag("v1.9.0").Version);
                foreach (string tag in new[] { "v01.1.0", "v1.1.0-beta", "1.1.0", "v1.1.0/../../", "v1.1.0\n", "v999999999999.1.0", null })
                    Assert(UpdateRelease.FromTag(tag) == null);
            });
            run("Update checker rejects draft, preview, invalid JSON and missing ZIP", delegate
            {
                foreach (string json in new[] { "{broken", "null", "{}", Json.Replace("\"draft\":false", "\"draft\":true"),
                    Json.Replace("\"prerelease\":false", "\"prerelease\":true"), Json.Replace("win.zip", "source.zip"), Json.Replace("uploaded", "new") })
                {
                    bool rejected = false;
                    try { UpdateClient.Parse(json); }
                    catch (Exception ex) { rejected = ex is InvalidDataException || ex is IOException || ex is System.Runtime.Serialization.SerializationException || ex is System.Xml.XmlException; }
                    Assert(rejected);
                }
            });
            run("Update HTTP request is anonymous and includes required API headers", delegate
            {
                var handler = new Handler { Send = delegate(HttpRequestMessage request)
                {
                    Assert(request.RequestUri.AbsoluteUri == UpdateClient.LatestUrl && request.Method == HttpMethod.Get);
                    Assert(request.Headers.UserAgent.ToString().StartsWith("DisplayVeil/"));
                    Assert(request.Headers.Authorization == null && request.Headers.Contains("X-GitHub-Api-Version"));
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json) };
                } };
                Assert(UpdateClient.FetchAsync(CancellationToken.None, handler).GetAwaiter().GetResult().Tag == "v1.1.0");
            });
            run("Update HTTP 404 means no public release; rate limits and server errors are failures", delegate
            {
                foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, (HttpStatusCode)429, HttpStatusCode.ServiceUnavailable })
                {
                    var handler = new Handler { Send = delegate { return new HttpResponseMessage(status); } };
                    try { Assert(UpdateClient.FetchAsync(CancellationToken.None, handler).GetAwaiter().GetResult() == null && status == HttpStatusCode.NotFound); }
                    catch (HttpRequestException) { Assert(status != HttpStatusCode.NotFound); }
                }
            });
            run("Automatic update checks respect opt-out, 24 hours and clock rollback", delegate
            {
                var settings = new Settings();
                Assert(UpdateMonitor.IsDue(settings, Now));
                settings.LastUpdateCheckUtcTicks = Now.AddHours(-23).Ticks; Assert(!UpdateMonitor.IsDue(settings, Now));
                settings.LastUpdateCheckUtcTicks = Now.AddDays(-1).Ticks; Assert(UpdateMonitor.IsDue(settings, Now));
                settings.LastUpdateCheckUtcTicks = Now.AddDays(1).Ticks; Assert(UpdateMonitor.IsDue(settings, Now));
                settings.AutoCheckUpdates = false; Assert(!UpdateMonitor.IsDue(settings, Now));
            });
            run("Manual update checks bypass auto opt-out and saved attempt time", delegate
            {
                int requests = 0, saves = 0;
                var settings = new Settings { AutoCheckUpdates = false, LastUpdateCheckUtcTicks = Now.Ticks };
                using (var monitor = new UpdateMonitor(settings, new Version(1, 0, 0, 0), delegate(CancellationToken token) { requests++; return Latest(token); }, delegate { saves++; }))
                {
                    Check(monitor, false); Assert(requests == 0);
                    Check(monitor, true);
                    Assert(requests == 1 && saves == 2 && monitor.Available.Tag == "v1.1.0" && settings.LatestReleaseTag == "v1.1.0");
                }
            });
            run("Startup results are cached across restarts without repeated requests", delegate
            {
                var settings = new Settings();
                using (var first = new UpdateMonitor(settings, new Version(1, 0, 0, 0), Latest, delegate { })) { Check(first, false); }
                using (var second = new UpdateMonitor(settings, new Version(1, 0, 0, 0), delegate { throw new Exception("Unexpected request"); }, delegate { }))
                { Check(second, false); Assert(second.Available.Tag == "v1.1.0" && second.Message.Contains("更新できます")); }
            });
            run("Same or older public versions never prompt a downgrade", delegate
            {
                foreach (var version in new[] { new Version(1, 1, 0, 0), new Version(1, 2, 0, 0) })
                using (var monitor = new UpdateMonitor(new Settings { LatestReleaseTag = "v1.1.0" }, version, Latest, delegate { }))
                { Assert(monitor.Available == null); Check(monitor, true); Assert(monitor.Available == null && monitor.Message.Contains("最新")); }
            });
            run("No release clears a stale cached update", delegate
            {
                var settings = new Settings { LatestReleaseTag = "v1.1.0" };
                using (var monitor = new UpdateMonitor(settings, new Version(1, 0, 0, 0), delegate { return Task.FromResult<UpdateRelease>(null); }, delegate { }))
                { Check(monitor, true); Assert(monitor.Available == null && settings.LatestReleaseTag == "" && monitor.Message.Contains("見つかりません")); }
            });
            run("Network, timeout and malformed-response failures keep cached updates and allow retry", delegate
            {
                foreach (var error in new Exception[] { new HttpRequestException(), new TaskCanceledException(), new InvalidDataException(), new System.Runtime.Serialization.SerializationException() })
                {
                    var failed = new TaskCompletionSource<UpdateRelease>(); failed.SetException(error);
                    int requests = 0;
                    using (var monitor = new UpdateMonitor(new Settings { LatestReleaseTag = "v1.1.0" }, new Version(1, 0, 0, 0),
                        delegate(CancellationToken token) { return ++requests == 1 ? failed.Task : Latest(token); }, delegate { }))
                    {
                        Check(monitor, false); Assert(!monitor.Busy && monitor.Available != null && monitor.Message.Contains("確認できません"));
                        Check(monitor, false); Assert(requests == 1);
                        Check(monitor, true); Assert(requests == 2 && monitor.Message.Contains("更新できます"));
                    }
                }
            });
            run("An in-flight update check suppresses duplicates and publishes completion", delegate
            {
                int requests = 0, changes = 0;
                var pending = new TaskCompletionSource<UpdateRelease>();
                using (var monitor = new UpdateMonitor(new Settings(), new Version(1, 0, 0, 0), delegate { requests++; return pending.Task; }, delegate { }))
                {
                    monitor.Changed += delegate { changes++; };
                    var task = monitor.CheckAsync(true, Now); Assert(monitor.Busy);
                    Check(monitor, true); Assert(requests == 1);
                    pending.SetResult(UpdateRelease.FromTag("v1.1.0")); task.GetAwaiter().GetResult();
                    Assert(!monitor.Busy && changes == 2 && monitor.Available != null);
                }
            });
            run("Closing during an update cancels work and suppresses late view changes", delegate
            {
                int changes = 0;
                var pending = new TaskCompletionSource<UpdateRelease>();
                var monitor = new UpdateMonitor(new Settings(), new Version(1, 0, 0, 0), delegate(CancellationToken token)
                { token.Register(delegate { pending.TrySetCanceled(); }); return pending.Task; }, delegate { });
                monitor.Changed += delegate { changes++; };
                var task = monitor.CheckAsync(true, Now); monitor.Dispose(); task.GetAwaiter().GetResult();
                Assert(!monitor.Busy && changes == 1); Check(monitor, true); Assert(changes == 1);
            });
        }
    }
}
