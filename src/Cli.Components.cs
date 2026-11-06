using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PBZKeyForge
{
    public static partial class Cli
    {
        static object SourceInformation()
        {
            Dictionary<string, object> info = null;
            using (var stream = typeof(Cli).Assembly.GetManifestResourceStream("PBZKeyForge.BuildInfo.json"))
                if (stream != null) using (var reader = new StreamReader(stream))
                    info = AgentJson.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
            string revision = info == null ? null : TextValue(info, "revision");
            string reference = info == null ? null : TextValue(info, "sourceRef");
            bool recorded = !string.IsNullOrEmpty(reference);
            bool modified = info == null || ObjectValue<bool>(info, "modified");
            return new {
                repository = ProjectLinks.Project, revision = revision, sourceRef = reference,
                recorded = recorded, modified = modified, exact = recorded && !modified,
                browse = recorded ? ProjectLinks.Project + "/tree/" + reference : null,
                archive = recorded ? ProjectLinks.Project + "/archive/" + reference + ".zip" : null,
                clone = "git clone " + ProjectLinks.Project + ".git",
                checkout = recorded ? "git checkout --detach " + reference : null,
                note = !recorded ? "Source revision was not recorded. Obtain the matching release tag from the publisher; do not assume the latest branch matches this executable."
                    : modified ? "This build includes local source changes. The revision URL contains the base source; obtain the modified workspace from the builder for the exact source."
                    : "The recorded revision identifies the source used by this build."
            };
        }

        static object Readiness(BindingDocument document, bool explicitScheme = false)
        {
            bool running = EditorSession.IsGameRunning;
            var installation = document.Installation;
            var live = installation == null ? null : installation.LiveStatus();
            bool enabled = installation != null && installation.ConfigurationText != null
                && KeyEnhancementConfiguration.FromJson(installation.ConfigurationText).Enabled;
            string reason = installation == null ? "not_installed_source"
                : !running ? "game_closed"
                : !enabled ? "enhancement_disabled"
                : document.Session.SchemeError != null || document.Session.ActiveProfile == null ? "scheme_unknown"
                : document.Session.SelectedProfile != document.Session.ActiveProfile ? "scheme_not_active"
                : live == null || !live.Available ? "waiting_for_runtime" : null;
            return new {
                canApply = installation != null && !running && (document.Session.ActiveProfile != null || explicitScheme),
                canLiveApply = reason == null, liveBlockedBy = reason,
                enhancementEnabled = enabled,
                next = reason == null ? "Preview with --edit --live --dry-run, then send the same request with --live."
                    : reason == "game_closed" ? "Use --apply while closed; if activeScheme is unknown, explicitly select --scheme from --inspect. For live editing, enable the component while closed, start the game, then poll --runtime-status."
                    : reason == "waiting_for_runtime" ? "Wait for the game to finish loading and poll --runtime-status. If it never connects, close the game and repair with --component --action enable."
                    : reason == "enhancement_disabled" ? "Close the game and use --component --action enable, then start the game."
                    : reason == "scheme_not_active" ? "Omit --scheme or select activeScheme for live editing. Change the active scheme only after closing the game."
                    : reason == "scheme_unknown" ? "The game scheme could not be authenticated; inspect schemeError. Live editing cannot guess a scheme."
                    : "Use an installed EnhancedInputUserSettings.sav for apply/live. Use --out edited.zip for offline work."
            };
        }

        static string ComponentGameDirectory(string source)
        {
            if (!Directory.Exists(source)) return KeyEnhancementInstallation.FindGameDirectory(source);
            string root = Path.GetFullPath(source);
            if (!File.Exists(Path.Combine(root, @"platform\steam\PBZ.exe")))
                throw new ArgumentException("Specify the game root containing platform/steam/PBZ.exe, or an installed input SAV.");
            return root;
        }

        static int ManageComponent(string command, string source, Dictionary<string, string> options)
        {
            string action = Option(options, "--action", "status");
            bool gameplay = command == "--gameplay-mod";
            string[] actions = gameplay ? new[] { "status", "enable", "disable", "uninstall" }
                : new[] { "status", "enable", "disable", "uninstall", "check-update", "update" };
            if (!actions.Contains(action)) throw new ArgumentException("--action: " + string.Join(", ", actions));
            if (options.ContainsKey("--archive") && action != "update") throw new ArgumentException("--archive requires --action update.");
            string root = ComponentGameDirectory(source);
            if (gameplay)
            {
                var mod = new PerfectDeflectNoRapidClickPenaltyMod(root);
                if (action != "status") mod.Apply(action == "enable");
                Console.WriteLine(AgentJson.Serialize(new {
                    ok = true, component = "perfect-deflect-no-rapid-click-penalty", action = action,
                    state = mod.ReadState().ToString(), directory = mod.InstallDirectory,
                    supported = mod.HasTargetResource(), running = EditorSession.IsGameRunning,
                    effect = "All schemes and difficulties. Enable/disable takes effect on the next game launch; the game must be closed for changes."
                }));
                return 0;
            }

            var installation = new KeyEnhancementInstallation(root);
            KeyEnhancementUninstallResult removed = null;
            Ue4ssRelease latest = null;
            string backup = null;
            if (action == "uninstall") removed = installation.Uninstall();
            else if (action == "enable" || action == "disable")
            {
                var configuration = installation.LoadConfiguration();
                configuration.Enabled = action == "enable";
                installation.Apply(configuration);
            }
            else if (action == "check-update" || action == "update")
            {
                if (action == "update" && EditorSession.IsGameRunning)
                    throw new InvalidOperationException("Close the game before updating UE4SS.");
                string archive = Option(options, "--archive", null);
                if (archive != null)
                {
                    Ue4ssRelease.ReadPackage(archive);
                    Directory.CreateDirectory(Ue4ssRelease.CacheDirectory);
                    string cached = Path.Combine(Ue4ssRelease.CacheDirectory, "manual-" + Guid.NewGuid().ToString("N") + ".zip");
                    File.Copy(archive, cached); archive = cached;
                    latest = new Ue4ssRelease { Provider = "Manual", Version = Path.GetFileNameWithoutExtension(options["--archive"]),
                        Asset = Path.GetFileName(options["--archive"]), Published = "", Source = "" };
                }
                else
                {
                    latest = Ue4ssRelease.CheckLatest();
                    if (action == "update" && latest.IsNewerThan(installation.CurrentRuntimeRelease()))
                        archive = latest.DownloadPackage(null);
                }
                if (action == "update" && archive != null) backup = installation.UpdateRuntime(archive, latest);
            }
            bool complete = removed == null || removed.Complete;
            Console.WriteLine(AgentJson.Serialize(new {
                ok = complete, component = "key-enhancement", action = action,
                state = installation.ReadState().ToString(), loaderState = installation.ReadLoaderState().ToString(),
                conflict = installation.ConflictDetail, configuration = installation.ConfigurationPath,
                directory = installation.InstallDirectory, live = installation.LiveStatus(), running = EditorSession.IsGameRunning,
                uninstall = removed, currentRuntime = installation.CurrentRuntimeRelease(), latestRuntime = latest, backup = backup,
                next = !complete ? "Uninstall retained modified/shared files. Inspect uninstall.RetainedFiles; other Mods may still need the loader."
                    : action == "enable" ? "Bundled runtime installed without online prompts. Start the game, poll --runtime-status, then use --edit --live. Check UE4SS updates explicitly with --action check-update."
                    : "Component changes take effect on the next launch. Disable retains configuration; uninstall reports removed and retained files."
            }));
            return complete ? 0 : 1;
        }
    }
}
