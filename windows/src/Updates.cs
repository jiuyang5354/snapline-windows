using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Snapline
{
    [DataContract]
    internal sealed class Release
    {
        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "body")] public string Notes { get; set; }
        [DataMember(Name = "assets")] public ReleaseAsset[] Assets { get; set; }
        internal Version Version;
        internal ReleaseAsset Package;
        internal string Page { get { return Updates.Repository + "/releases/tag/" + Tag; } }
    }

    [DataContract]
    internal sealed class ReleaseAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "state")] public string State { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
        [DataMember(Name = "digest")] public string Digest { get; set; }
        [DataMember(Name = "browser_download_url")] public string Url { get; set; }
    }

    internal sealed class Updates : IDisposable
    {
        internal const string Repository = "https://github.com/jiuyang5354/snapline-windows";
        internal const string Feed = "https://raw.githubusercontent.com/jiuyang5354/snapline-windows/main/update.json";
        private const int MaxPackageBytes = 20 * 1024 * 1024;
        private readonly HttpClient client;
        internal static Version CurrentVersion {
            get { var version = Assembly.GetExecutingAssembly().GetName().Version; return new Version(version.Major, version.Minor, version.Build); }
        }

        internal Updates() : this(new HttpClientHandler()) { }
        internal Updates(HttpMessageHandler handler)
        {
            client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = MaxPackageBytes };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Snapline/" + CurrentVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        internal static bool Due(Settings settings, DateTime now)
        {
            return settings.AutoCheckUpdates && (settings.UpdateCheckedUtcTicks <= 0 ||
                settings.UpdateCheckedUtcTicks > now.Ticks || now.Ticks - settings.UpdateCheckedUtcTicks >= TimeSpan.TicksPerDay);
        }

        internal static Release Parse(byte[] json, Version current)
        {
            Release best = null;
            Release[] releases;
            using (var stream = new MemoryStream(json))
                releases = (Release[])new DataContractJsonSerializer(typeof(Release[])).ReadObject(stream);
            foreach (var release in releases ?? new Release[0])
            {
                Version version;
                if (release == null || release.Draft || release.Tag == null || !release.Tag.StartsWith("v", StringComparison.Ordinal) ||
                    !Version.TryParse(release.Tag.Substring(1), out version) || version.Build < 0 || version.Revision >= 0 ||
                    release.Tag != "v" + version.ToString(3) || version <= current || release.Assets == null) continue;
                string name = "Snapline-Windows-" + release.Tag + ".zip";
                foreach (var asset in release.Assets)
                    if (asset != null && asset.Name == name && asset.State == "uploaded" && asset.Size > 0 && asset.Size <= MaxPackageBytes &&
                        asset.Url == Repository + "/releases/download/" + release.Tag + "/" + name && asset.Digest != null &&
                        Regex.IsMatch(asset.Digest, "\\Asha256:[0-9a-fA-F]{64}\\z")) { release.Package = asset; break; }
                if (release.Package == null || (best != null && version <= best.Version)) continue;
                release.Version = version;
                best = release;
            }
            return best;
        }

        internal async Task<Release> CheckAsync(CancellationToken cancel)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(15000);
                using (var response = await client.GetAsync(Feed, timeout.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        await Transfer(input, output, 1024 * 1024, null, timeout.Token).ConfigureAwait(false);
                        return Parse(output.ToArray(), CurrentVersion);
                    }
                }
            }
        }

        internal async Task DownloadAsync(Release release, string destination, IProgress<int> progress, CancellationToken cancel)
        {
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    timeout.CancelAfter(120000);
                    using (var response = await client.GetAsync(release.Package.Url, timeout.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        if (response.RequestMessage.RequestUri.Scheme != Uri.UriSchemeHttps) throw new IOException("更新下载地址必须使用 HTTPS。");
                        long received;
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                            received = await Transfer(input, output, release.Package.Size, progress, timeout.Token).ConfigureAwait(false);
                        if (received != release.Package.Size) throw new IOException("更新包下载不完整，请重试。");
                    }
                    string digest;
                    using (var input = File.OpenRead(temporary))
                    using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "");
                    if (!string.Equals(digest, release.Package.Digest.Substring(7), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("更新包校验失败，请重新下载。");
                    timeout.Token.ThrowIfCancellationRequested();
                    if (File.Exists(destination)) File.Replace(temporary, destination, null);
                    else File.Move(temporary, destination);
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static async Task<long> Transfer(Stream input, Stream output, long limit, IProgress<int> progress, CancellationToken cancel)
        {
            var buffer = new byte[8192];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) != 0)
            {
                total += read;
                if (total > limit) throw new IOException("更新响应超出预期大小。");
                await output.WriteAsync(buffer, 0, read, cancel).ConfigureAwait(false);
                if (progress != null) progress.Report((int)(total * 100 / limit));
            }
            return total;
        }

        internal static bool IsError(Exception ex)
        {
            return ex is HttpRequestException || ex is OperationCanceledException || ex is IOException ||
                ex is UnauthorizedAccessException || ex is SerializationException;
        }

        public void Dispose() { client.Dispose(); }
    }
}
