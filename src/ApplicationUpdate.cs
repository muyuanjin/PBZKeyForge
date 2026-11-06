using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public sealed class ApplicationRelease
    {
        public string Version, Page, Download;
        public static ApplicationRelease Parse(string json)
        {
            var data = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            string tag = Convert.ToString(data["tag_name"]); System.Version version;
            if ((bool)data["draft"] || (bool)data["prerelease"] || !System.Version.TryParse(tag.TrimStart('v', 'V'), out version))
                throw new InvalidDataException(I18n.Str("app.invalidupdate"));
            foreach (var asset in ((object[])data["assets"]).Cast<Dictionary<string, object>>())
            {
                string name = Convert.ToString(asset["name"]), url = Convert.ToString(asset["browser_download_url"]);
                if (name == "PBZKeyForge-" + tag + ".zip" && url.StartsWith(ProjectLinks.Project + "/releases/download/", StringComparison.Ordinal))
                    return new ApplicationRelease { Version = version.ToString(), Page = Convert.ToString(data["html_url"]), Download = url };
            }
            throw new InvalidDataException(I18n.Str("app.invalidupdate"));
        }
        internal static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "PBZKeyForge/" + Program.Version;
            request.Timeout = 15000; request.ReadWriteTimeout = 30000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return request;
        }
        public static ApplicationRelease CheckLatest()
        {
            using (var response = Request("https://api.github.com/repos/muyuanjin/PBZKeyForge/releases/latest").GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream())) return Parse(reader.ReadToEnd());
        }
        public bool IsNewerThan(string current) { return new System.Version(Version) > new System.Version(current); }
    }

    public sealed class ApplicationUpdatePlan
    {
        public string Directory, Staging, Version;
        public int Parent;
        public string[] Files;
    }

    public static class ApplicationUpdate
    {
        public static readonly string[] PackageFiles = { "PBZKeyForge.exe", "PBZKeyForge-cli.exe", "README.md", "LICENSE", "THIRD-PARTY-NOTICES.md", "docs/cli.md", "docs/key-enhancement.md" };
        public static string Prepare(ApplicationRelease release, string targetDirectory)
        {
            string staging = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBZKeyForge", "updates", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(staging);
            string package = Path.Combine(staging, "package.zip");
            using (var response = ApplicationRelease.Request(release.Download).GetResponse())
            using (var input = response.GetResponseStream()) using (var output = File.Create(package)) input.CopyTo(output);
            return PreparePackage(package, staging, targetDirectory, release.Version, Process.GetCurrentProcess().Id);
        }

        public static string PreparePackage(string package, string staging, string targetDirectory, string version, int parent)
        {
            var files = new List<string>();
            System.IO.Directory.CreateDirectory(Path.Combine(staging, "payload"));
            using (var stream = File.OpenRead(package)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                foreach (string name in PackageFiles)
                {
                    var entry = zip.GetEntry(name);
                    if (entry == null)
                    {
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(I18n.Str("app.invalidupdate"));
                        continue;
                    }
                    string path = Path.Combine(staging, "payload", name.Replace('/', Path.DirectorySeparatorChar));
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var input = entry.Open()) using (var output = File.Create(path)) input.CopyTo(output);
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var assembly = AssemblyName.GetAssemblyName(path);
                        if (assembly.Name != Path.GetFileNameWithoutExtension(name) || assembly.Version.ToString(3) != new Version(version).ToString(3))
                            throw new InvalidDataException(I18n.Str("app.invalidupdate"));
                    }
                    files.Add(name);
                }
            string manifest = Path.Combine(staging, "update.json");
            File.WriteAllText(manifest, new JavaScriptSerializer().Serialize(new ApplicationUpdatePlan {
                Directory = Path.GetFullPath(targetDirectory), Staging = Path.GetFullPath(staging), Version = version, Parent = parent, Files = files.ToArray() }), new UTF8Encoding(false));
            return manifest;
        }

        public static void StartHelper(string manifest)
        {
            string helper = Path.Combine(Path.GetDirectoryName(manifest), "PBZKeyForge-update.exe");
            File.Copy(System.Windows.Forms.Application.ExecutablePath, helper);
            Process.Start(new ProcessStartInfo(helper, "--finish-update \"" + manifest + "\"") { UseShellExecute = false, CreateNoWindow = true });
        }

        public static void ApplyPrepared(ApplicationUpdatePlan plan)
        {
            if (plan.Files == null || !plan.Files.Contains("PBZKeyForge.exe") || !plan.Files.Contains("PBZKeyForge-cli.exe")
                || plan.Files.Any(name => !PackageFiles.Contains(name))) throw new InvalidDataException(I18n.Str("app.invalidupdate"));
            string backup = Path.Combine(plan.Staging, "backup");
            var changed = new List<string>(); var existed = new HashSet<string>();
            foreach (string name in plan.Files)
            {
                string target = Path.Combine(plan.Directory, name.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(target)) continue;
                string copy = Path.Combine(backup, name.Replace('/', Path.DirectorySeparatorChar));
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(copy)); File.Copy(target, copy, true); existed.Add(name);
            }
            try
            {
                foreach (string name in plan.Files)
                {
                    string target = Path.Combine(plan.Directory, name.Replace('/', Path.DirectorySeparatorChar));
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target));
                    ReplaceFile(Path.Combine(plan.Staging, "payload", name.Replace('/', Path.DirectorySeparatorChar)), target);
                    changed.Add(name);
                }
            }
            catch (Exception failure)
            {
                var errors = new List<string>();
                foreach (string name in changed.AsEnumerable().Reverse())
                    try
                    {
                        string target = Path.Combine(plan.Directory, name.Replace('/', Path.DirectorySeparatorChar));
                        if (existed.Contains(name)) ReplaceFile(Path.Combine(backup, name.Replace('/', Path.DirectorySeparatorChar)), target);
                        else File.Delete(target);
                    }
                    catch (Exception rollback) { errors.Add(name + ": " + rollback.Message); }
                if (errors.Count > 0) throw new IOException(failure.Message + "\r\n" + string.Join("\r\n", errors));
                throw;
            }
        }

        static void ReplaceFile(string source, string target)
        {
            string temporary = target + ".update-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(source, temporary); if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static int Finish(string manifest)
        {
            var plan = new JavaScriptSerializer().Deserialize<ApplicationUpdatePlan>(File.ReadAllText(manifest));
            try
            {
                try
                {
                    using (var parent = Process.GetProcessById(plan.Parent))
                        if (!parent.WaitForExit(60000)) throw new IOException(I18n.Str("app.closefirst"));
                }
                catch (ArgumentException) { }
                ApplyPrepared(plan);
                File.WriteAllText(Path.Combine(plan.Staging, "result.txt"), "ok");
                Process.Start(new ProcessStartInfo(Path.Combine(plan.Directory, "PBZKeyForge.exe")) { WorkingDirectory = plan.Directory });
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(plan.Staging, "result.txt"), ex.Message);
                System.Windows.Forms.Application.EnableVisualStyles();
                using (var dialog = new ApplicationAboutDialog(null)) { dialog.ShowFailure(ex.Message); dialog.ShowDialog(); }
                return 1;
            }
        }
    }
}
