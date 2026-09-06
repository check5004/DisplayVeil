using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DisplayVeil
{
    internal sealed class UpdateRelease
    {
        public const string ReleasesUrl = "https://github.com/check5004/DisplayVeil/releases";
        public string Tag { get; private set; }
        public Version Version { get; private set; }
        public string PageUrl { get { return ReleasesUrl + "/tag/" + Tag; } }
        public static UpdateRelease FromTag(string tag)
        {
            Version version;
            if (tag == null || !Regex.IsMatch(tag, @"\Av(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z") ||
                !Version.TryParse(tag.Substring(1) + ".0", out version)) return null;
            return new UpdateRelease { Tag = tag, Version = version };
        }
    }

    internal static class UpdateClient
    {
        public const string LatestUrl = "https://api.github.com/repos/check5004/DisplayVeil/releases/latest";
        public static Version CurrentVersion { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        [DataContract]
        private sealed class ReleaseData
        {
            [DataMember(Name = "tag_name")] public string Tag { get; set; }
            [DataMember(Name = "draft")] public bool Draft { get; set; }
            [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
            [DataMember(Name = "assets")] public AssetData[] Assets { get; set; }
        }
        [DataContract]
        private sealed class AssetData
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "state")] public string State { get; set; }
        }

        public static UpdateRelease Parse(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var data = (ReleaseData)new DataContractJsonSerializer(typeof(ReleaseData)).ReadObject(stream);
                var release = data == null ? null : UpdateRelease.FromTag(data.Tag);
                if (release == null || data.Draft || data.Prerelease || data.Assets == null)
                    throw new InvalidDataException("正式版の情報を取得できませんでした。");
                string expected = "DisplayVeil-" + release.Version.ToString(3) + "-win.zip";
                foreach (var asset in data.Assets)
                    if (asset != null && asset.Name == expected && asset.State == "uploaded") return release;
                throw new InvalidDataException("配布ZIPがまだ公開されていません。");
            }
        }

        public static Task<UpdateRelease> FetchAsync(CancellationToken cancellation)
        {
            return FetchAsync(cancellation, new HttpClientHandler());
        }
        internal static async Task<UpdateRelease> FetchAsync(CancellationToken cancellation, HttpMessageHandler handler)
        {
            using (var client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.MaxResponseContentBufferSize = 256 * 1024;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DisplayVeil/" + CurrentVersion.ToString(3));
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
                using (var response = await client.GetAsync(LatestUrl, cancellation).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound) return null;
                    response.EnsureSuccessStatusCode();
                    return Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
        }
    }

    // The view only reads this state: background results never open or activate a window.
    internal sealed class UpdateMonitor : IDisposable
    {
        private readonly Settings settings;
        private readonly Version current;
        private readonly Func<CancellationToken, Task<UpdateRelease>> fetch;
        private readonly Action save;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool disposed;
        public event Action Changed;
        public bool Busy { get; private set; }
        public string Message { get; private set; }
        public UpdateRelease Available { get; private set; }
        public UpdateMonitor(Settings settings, Version current, Func<CancellationToken, Task<UpdateRelease>> fetch, Action save)
        {
            this.settings = settings; this.current = current; this.fetch = fetch; this.save = save;
            SetRelease(UpdateRelease.FromTag(settings.LatestReleaseTag));
            Message = Available == null ? "現在のバージョン " + current.ToString(3) : Available.Tag + " に更新できます。";
        }
        public static bool IsDue(Settings settings, DateTime utcNow)
        {
            long last = settings.LastUpdateCheckUtcTicks;
            return settings.AutoCheckUpdates && (last <= 0 || last > utcNow.Ticks || utcNow.Ticks - last >= TimeSpan.TicksPerDay);
        }
        private void SetRelease(UpdateRelease release)
        {
            Available = release != null && release.Version > current ? release : null;
        }
        private void Notify() { if (!disposed && Changed != null) Changed(); }
        public async Task CheckAsync(bool manual, DateTime utcNow)
        {
            if (disposed || Busy || (!manual && !IsDue(settings, utcNow))) return;
            Busy = true;
            Message = "更新を確認しています…";
            Notify();
            // Persist attempts as well as successes to avoid repeated requests when offline.
            settings.LastUpdateCheckUtcTicks = utcNow.Ticks;
            try
            {
                save();
                var release = await fetch(cancellation.Token);
                if (disposed) return;
                settings.LatestReleaseTag = release == null ? "" : release.Tag;
                SetRelease(release);
                Message = Available != null ? Available.Tag + " に更新できます。" :
                    release == null ? "公開された正式版が見つかりませんでした。" : "現在の " + current.ToString(3) + " は最新です。";
                save();
            }
            catch (Exception ex)
            {
                if (!(ex is HttpRequestException || ex is OperationCanceledException || ex is IOException || ex is InvalidDataException ||
                    ex is SerializationException || ex is System.Xml.XmlException)) throw;
                if (!disposed) Message = "確認できませんでした。通信環境を確認し、後でもう一度お試しください。";
            }
            finally { Busy = false; Notify(); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; cancellation.Cancel(); cancellation.Dispose();
        }
    }
}
