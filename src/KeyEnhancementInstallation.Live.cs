using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PBZKeyForge
{
    public sealed class KeyEnhancementLiveStatus
    {
        public bool Available;
        public bool Suspended;
        public bool RoutingEnabled;
        public bool Waiting;
        public bool NeverConnected;
        public DateTime UpdatedUtc;
        public string Detail;
        public string ErrorDetail;
        public string ActiveSchemeId;
    }

    public sealed partial class KeyEnhancementInstallation
    {
        string lastLiveState, lastLiveDetail;
        DateTime lastLiveUpdate;

        public KeyEnhancementLiveStatus LiveStatus()
        {
            string path = Path.Combine(InstallDirectory, "live-status.txt");
            try
            {
                var updated = File.GetLastWriteTimeUtc(path);
                string[] lines = File.Exists(path) ? File.ReadAllText(path).Replace("\r", "").Split(new[] { '\n' }, 4) : new string[0];
                string state = lines.Length > 2 ? lines[2] : null;
                if (lines.Length == 4 && lines[0] == "PBZ-LIVE-1"
                    && (state == "ready" || state == "suspended" || state == "disabled"))
                {
                    lastLiveState = state;
                    lastLiveDetail = lines[3];
                    lastLiveUpdate = updated;
                }
            }
            // The runtime writes this file on the game thread. A partial read must not
            // discard the last complete heartbeat or extend its lifetime.
            catch (IOException) { }
            bool connected = lastLiveState != null && DateTime.UtcNow - lastLiveUpdate < TimeSpan.FromSeconds(4);
            bool seen = lastLiveState != null;
            if (!connected && seen)
                foreach (var process in System.Diagnostics.Process.GetProcessesByName("PBZ"))
                    using (process)
                    {
                        try { if (process.StartTime.ToUniversalTime() > lastLiveUpdate) seen = false; }
                        catch (InvalidOperationException) { }
                    }
            string scheme = null;
            try { string schemePath = Path.Combine(InstallDirectory, "scheme-state.txt"); if (connected && File.Exists(schemePath)) scheme = File.ReadAllText(schemePath).Trim(); }
            catch (IOException) { }
            return new KeyEnhancementLiveStatus { Available = connected, Waiting = !connected && seen, NeverConnected = !seen, ActiveSchemeId = scheme,
                Suspended = connected && lastLiveState == "suspended",
                RoutingEnabled = connected && lastLiveState == "ready", UpdatedUtc = lastLiveUpdate,
                ErrorDetail = seen && lastLiveState == "suspended" ? I18n.Str("live.suspended", lastLiveDetail) : null,
                Detail = !connected ? I18n.Str(seen ? "live.waitingdetail" : "flow.notconnected.detail") : lastLiveState == "suspended"
                    ? I18n.Str("live.suspended", lastLiveDetail)
                    : I18n.Str(lastLiveState == "disabled" ? "live.disabled" : "live.ready") };
        }

        internal static void WriteAtomicText(string path, string text)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        async Task SendLive(string compiled, int timeoutMs)
        {
            string id = Guid.NewGuid().ToString("N");
            WriteAtomicText(Path.Combine(InstallDirectory, "live-request.lua"),
                "return {id=\"" + id + "\", configuration=(function()\n" + compiled + "\nend)()}\n");
            WriteAtomicText(Path.Combine(InstallDirectory, "live-request.id"), id);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string response = Path.Combine(InstallDirectory, "live-response.txt");
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(20).ConfigureAwait(false);
                string value;
                try { value = File.Exists(response) ? File.ReadAllText(response).Replace("\r", "") : ""; }
                catch (IOException) { continue; }
                var lines = value.Split(new[] { '\n' }, 3);
                if (lines.Length < 2 || lines[0] != id) continue;
                if (lines[1] != "ok") throw new IOException(I18n.Str("live.rejected", lines.Length > 2 ? lines[2] : value));
                return;
            }
            // A canceled request must not become a delayed apply when the game resumes.
            WriteAtomicText(Path.Combine(InstallDirectory, "live-request.id"), "cancel-" + id);
            throw new IOException(I18n.Str("live.timeout"));
        }

        public async Task ApplyLive(KeyEnhancementConfiguration candidate)
        {
            candidate = candidate.Clone();
            string compiled = candidate.CompileLua(), json = candidate.ToJson();
            var status = LiveStatus();
            if (!status.Available) throw new IOException(status.Detail);
            EnsureConfigurationUnchanged();
            var previous = LoadConfiguration();
            if (previous.Schemes.Exists(item => item.Id == status.ActiveSchemeId)) previous.ActiveSchemeId = status.ActiveSchemeId;
            if (!previous.Enabled || !candidate.Enabled) throw new IOException(I18n.Str("live.lifecycle"));
            string lockPath = Path.Combine(InstallDirectory, "live-edit.lock");
            using (var editLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                string luaPath = LocalPath(InstallDirectory, LuaConfigurationFile);
                string oldLua = File.ReadAllText(luaPath, Utf8), oldJson = ConfigurationText;
                string backup = Path.Combine(GameSchemeSettings.BackupDirectory(), "live-bindings-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(backup);
                File.WriteAllText(Path.Combine(backup, "router-config.lua"), oldLua, Utf8);
                if (oldJson != null) File.WriteAllText(Path.Combine(backup, "configuration.json"), oldJson, Utf8);
                await SendLive(compiled, 6000).ConfigureAwait(false);
                Exception failure = null;
                try
                {
                    EnsureConfigurationUnchanged();
                    WriteAtomicText(luaPath, compiled);
                    WriteAtomicText(ConfigurationPath, json);
                    WriteAtomicText(RetainedConfigurationPath, json);
                    ConfigurationText = json; configurationLoaded = true;
                }
                catch (Exception error)
                {
                    failure = error;
                }
                if (failure != null)
                {
                    string rollbackError = null;
                    try
                    {
                        await SendLive(previous.CompileLua(), 6000).ConfigureAwait(false);
                    }
                    catch (Exception rollback) { rollbackError = rollback.Message; }
                    try
                    {
                        if (File.ReadAllText(luaPath, Utf8) != oldLua) WriteAtomicText(luaPath, oldLua);
                    }
                    catch (Exception rollback) { rollbackError = rollbackError == null ? rollback.Message : rollbackError + Environment.NewLine + rollback.Message; }
                    try
                    {
                        if (oldJson != null && ReadConfigurationText() == json) WriteAtomicText(ConfigurationPath, oldJson);
                    }
                    catch (Exception rollback) { rollbackError = rollbackError == null ? rollback.Message : rollbackError + Environment.NewLine + rollback.Message; }
                    if (rollbackError != null) throw new IOException(I18n.Str("live.partial", failure.Message, rollbackError));
                    throw new IOException(failure.Message, failure);
                }
            }
        }
    }
}
