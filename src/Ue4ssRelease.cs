using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public sealed class Ue4ssRelease
    {
        public string Provider, Version, Source, Asset, Published, Download, AssetId, Updated;
        public static Ue4ssRelease Bundled
        {
            get
            {
                using (var stream = typeof(Ue4ssRelease).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.runtime.zip"))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                using (var reader = new StreamReader(zip.GetEntry("release.json").Open()))
                    return new JavaScriptSerializer().Deserialize<Ue4ssRelease>(reader.ReadToEnd());
            }
        }
        public static string CacheDirectory
        { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBZKeyForge", "cache", "ue4ss"); } }

        sealed class Sources { public string[] Api { get; set; } public string[] DownloadPrefixes { get; set; } }
        static Sources ReadSources()
        {
            using (var reader = new StreamReader(typeof(Ue4ssRelease).Assembly.GetManifestResourceStream("PBZKeyForge.UE4SS.sources.json")))
                return new JavaScriptSerializer().Deserialize<Sources>(reader.ReadToEnd());
        }

        public static Ue4ssRelease ParseLatest(string text)
        {
            var releases = new JavaScriptSerializer().DeserializeObject(text) as object[];
            if (releases == null) throw new InvalidDataException(I18n.Str("ue4ss.invalid"));
            foreach (var release in releases.Cast<Dictionary<string, object>>().Where(item => !(bool)item["draft"] && !(bool)item["prerelease"])
                .OrderByDescending(item => Convert.ToString(item["published_at"]), StringComparer.Ordinal))
                foreach (var asset in ((object[])release["assets"]).Cast<Dictionary<string, object>>())
                {
                    string name = Convert.ToString(asset["name"]);
                    if (!name.StartsWith("phantombladezero_ue4ss_", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    string url = Convert.ToString(asset["browser_download_url"]);
                    if (!url.StartsWith("https://github.com/KintaroModding/ue4ss/releases/download/", StringComparison.Ordinal)) continue;
                    return new Ue4ssRelease { Provider = "KintaroModding", Version = Convert.ToString(release["tag_name"]),
                        Source = Convert.ToString(release["html_url"]), Published = Convert.ToString(release["published_at"]),
                        Asset = name, AssetId = Convert.ToString(asset["id"]), Updated = Convert.ToString(asset["updated_at"]), Download = url };
                }
            throw new InvalidDataException(I18n.Str("ue4ss.invalid"));
        }

        static HttpWebRequest Request(string url, int timeout)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "PBZKeyForge"; request.Timeout = timeout; request.ReadWriteTimeout = timeout;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return request;
        }

        public static Ue4ssRelease CheckLatest()
        {
            foreach (string url in ReadSources().Api)
                try
                {
                    using (var response = Request(url, 7000).GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream())) return ParseLatest(reader.ReadToEnd());
                }
                catch (WebException) { }
                catch (ArgumentException) { }
                catch (InvalidDataException) { }
            throw new IOException(I18n.Str("ue4ss.checkfailed"));
        }

        public bool IsNewerThan(Ue4ssRelease current)
        {
            if (current == null) return true;
            if (Version == current.Version) return !string.IsNullOrEmpty(Updated) && !string.IsNullOrEmpty(current.Updated)
                && string.CompareOrdinal(Updated, current.Updated) > 0;
            return string.CompareOrdinal(Published, current.Published) > 0;
        }

        public static Dictionary<string, byte[]> ReadPackage(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var libraries = zip.Entries.Where(entry => entry.FullName.Replace('\\', '/').EndsWith("ue4ss/UE4SS.dll", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (libraries.Length != 1) throw new InvalidDataException(I18n.Str("ue4ss.invalid"));
                string full = libraries[0].FullName.Replace('\\', '/'), prefix = full.Substring(0, full.Length - "ue4ss/UE4SS.dll".Length);
                var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || name.EndsWith("/")) continue;
                    name = name.Substring(prefix.Length);
                    if (name.StartsWith("ue4ss/Mods/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Path.IsPathRooted(name) || name.Split('/').Any(part => part == ".." || part == "")) throw new InvalidDataException(I18n.Str("ue4ss.invalid"));
                    using (var input = entry.Open()) using (var output = new MemoryStream())
                    { input.CopyTo(output); result.Add(name, output.ToArray()); }
                }
                byte[] library, proxy;
                if (!result.TryGetValue("ue4ss/UE4SS.dll", out library) || !result.TryGetValue("dwmapi.dll", out proxy)
                    || !IsX64Library(library) || !IsX64Library(proxy)) throw new InvalidDataException(I18n.Str("ue4ss.invalid"));
                return result;
            }
        }

        static bool IsX64Library(byte[] bytes)
        {
            if (bytes.Length < 64 || BitConverter.ToUInt16(bytes, 0) != 0x5a4d) return false;
            int offset = BitConverter.ToInt32(bytes, 60);
            return offset >= 64 && offset <= bytes.Length - 24 && BitConverter.ToUInt32(bytes, offset) == 0x4550
                && BitConverter.ToUInt16(bytes, offset + 4) == 0x8664 && (BitConverter.ToUInt16(bytes, offset + 22) & 0x2000) != 0;
        }

        string CachePath
        {
            get
            {
                string key = AssetId + "-" + Updated;
                foreach (char value in Path.GetInvalidFileNameChars()) key = key.Replace(value, '_');
                return Path.Combine(CacheDirectory, key + ".zip");
            }
        }

        public string DownloadPackage(Action<string> progress)
        {
            Directory.CreateDirectory(CacheDirectory);
            string cached = CachePath;
            if (File.Exists(cached))
                try { ReadPackage(cached); return cached; }
                catch (InvalidDataException) { File.Delete(cached); }
            foreach (string prefix in ReadSources().DownloadPrefixes)
            {
                string temporary = cached + "." + Guid.NewGuid().ToString("N") + ".download";
                try
                {
                    string url = prefix + Download;
                    if (progress != null) progress(new Uri(url).Host);
                    using (var response = Request(url, 20000).GetResponse())
                    using (var input = response.GetResponseStream())
                    using (var output = File.Create(temporary)) input.CopyTo(output);
                    ReadPackage(temporary);
                    if (File.Exists(cached)) File.Delete(cached);
                    File.Move(temporary, cached);
                    return cached;
                }
                catch (WebException) { }
                catch (InvalidDataException) { }
                catch (IOException) { }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            throw new IOException(I18n.Str("ue4ss.downloadfailed"));
        }
    }
}
