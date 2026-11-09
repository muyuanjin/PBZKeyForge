using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public static partial class Cli
    {
        static readonly JavaScriptSerializer AgentJson = new JavaScriptSerializer { MaxJsonLength = 16777216 };
        static string Option(Dictionary<string, string> options, string key, string fallback)
        { string value; return options.TryGetValue(key, out value) ? value : fallback; }
        static string TextValue(Dictionary<string, object> value, string key, string fallback = null)
        { object result; return value.TryGetValue(key, out result) ? Convert.ToString(result) : fallback; }
        static T ObjectValue<T>(Dictionary<string, object> value, string key)
        {
            object result;
            if (!value.TryGetValue(key, out result)) throw new ArgumentException("Missing " + key);
            ValidateTypedFields(result, typeof(T), key);
            return AgentJson.Deserialize<T>(AgentJson.Serialize(result));
        }

        static void ValidateTypedFields(object value, Type type, string path)
        {
            if (value == null) return;
            if (type.IsArray || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var items = value as object[];
                if (items != null) for (int i = 0; i < items.Length; i++)
                    ValidateTypedFields(items[i], type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0], path + "[" + i + "]");
                return;
            }
            if (type.Namespace != typeof(Cli).Namespace) return;
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var values = value as Dictionary<string, object>;
            if (values == null) return;
            foreach (var pair in values)
            {
                var field = fields.FirstOrDefault(item => item.Name == pair.Key);
                if (field == null)
                {
                    if (type.GetProperty(pair.Key) != null) continue;
                    throw new ArgumentException("Unknown field: " + path + "." + pair.Key + ". Field names are case-sensitive; see --agent-help schema.");
                }
                ValidateTypedFields(pair.Value, field.FieldType, path + "." + pair.Key);
            }
        }

        static Dictionary<string, string> AgentOptions(string[] args, string command)
        {
            string allowed = "--json";
            switch (command)
            {
                case "--inspect": case "--catalog": allowed += " --scheme --device --query"; break;
                case "--edit": allowed += " --scheme --device --query --request --out --apply --live --dry-run"; break;
                case "--share": allowed += " --scheme --name --scope"; break;
                case "--component": allowed += " --action --archive"; break;
                case "--gameplay-mod": allowed += " --action"; break;
            }
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool acceptsSource = command != "--help" && command != "--agent-help" && command != "--keys" && command != "--presets" && command != "--version";
            for (int i = 1; i < args.Length; i++)
            {
                string argument = args[i];
                if (!argument.StartsWith("--"))
                {
                    if (i == 1 && acceptsSource) continue;
                    throw new ArgumentException("Unexpected argument: " + argument);
                }
                if (!allowed.Split(' ').Contains(argument)) throw new ArgumentException("Unsupported option for " + command + ": " + argument);
                if (!seen.Add(argument)) throw new ArgumentException("Duplicate option: " + argument);
                if (argument == "--json" || argument == "--apply" || argument == "--live" || argument == "--dry-run") continue;
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--")) throw new ArgumentException("Missing value for " + argument);
                options[argument] = args[++i];
            }
            return options;
        }

        static int RunAgent(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false); Console.InputEncoding = new UTF8Encoding(false); I18n.Current = "zh-Hans";
            string stage = "validate";
            int operationIndex = -1;
            var outcome = new Dictionary<string, object> { { "bindingsSaved", false }, { "activationSaved", false }, { "enhancementSaved", false }, { "liveApplied", false }, { "exported", false }, { "liveState", "not_requested" } };
            try
            {
                string command = args.Length == 0 ? "--agent-help" : args[0];
                if (!new[] { "--help", "--agent-help", "--version", "--presets", "--keys", "--inspect", "--catalog", "--share", "--runtime-status", "--component", "--gameplay-mod", "--edit" }.Contains(command))
                    throw new ArgumentException("Unknown command: " + command + ". Run --agent-help for supported commands.");
                var options = AgentOptions(args, command);
                if (command == "--help" || command == "--agent-help") { Console.WriteLine(AgentJson.Serialize(AgentHelp())); return 0; }
                if (command == "--version") { Console.WriteLine(AgentJson.Serialize(new { ok = true, version = Program.Version, source = SourceInformation() })); return 0; }
                if (command == "--presets")
                { Console.WriteLine(AgentJson.Serialize(new { ok = true, presets = Presets.All().Concat(UserPresetStore.Default().Load()).Select(preset => new { preset.Id, preset.Name, preset.Desc, preset.Scopes, preset.Vanilla, preset.CurrentProfileVanilla }) })); return 0; }
                if (command == "--keys")
                { Console.WriteLine(AgentJson.Serialize(new { ok = true, keyboard = KeyNames.Keyboard, gamepad = KeyNames.Gamepad, axes = BindingWorkspace.AxisKeys })); return 0; }
                string source = command == "--catalog" && (args.Length < 2 || args[1].StartsWith("--")) ? "new" : Arg(args, 1);
                if (string.IsNullOrEmpty(source) || source.StartsWith("--")) throw new ArgumentException(command == "--component" || command == "--gameplay-mod"
                    ? "Specify an installed input SAV or the game root containing platform/steam/PBZ.exe."
                    : "Specify an input SAV, ZIP, configuration JSON, or new.");
                if (command == "--component" || command == "--gameplay-mod")
                { stage = "component"; return ManageComponent(command, source, options); }
                stage = "open_source";
                var document = BindingDocument.Open(source);
                var runtime = document.Installation == null ? null : document.Installation.LiveStatus();
                if (runtime != null && runtime.Available && document.Configuration.Schemes.Any(item => item.Id == runtime.ActiveSchemeId))
                    document.Configuration.ActiveSchemeId = runtime.ActiveSchemeId;
                var baseline = document.Configuration.Clone();
                string scheme = Option(options, "--scheme", null);
                if (scheme != null)
                {
                    if (document.Configuration.Enabled && document.Configuration.Schemes.Count > 0) document.Configuration.SelectScheme(scheme);
                    else document.Session.SwitchProfile(scheme);
                }
                string device = Option(options, "--device", "keyboard");
                var family = document.Session.ControllerFamilies.FirstOrDefault(item => item.Name.Equals(device, StringComparison.OrdinalIgnoreCase));
                if (device != "keyboard" && family == null) throw new ArgumentException("Unknown device. Use keyboard, Xbox, PS, NS, or Other from --inspect.");
                int familyIndex = family == null ? -1 : family.Index;
                if (command == "--share")
                {
                    if (!document.Configuration.Enabled && !document.Session.IsOffline && document.Session.ActiveProfile == null && scheme == null)
                        throw new ArgumentException("The active game scheme is unknown. Specify --scheme.");
                    var preset = UserPresetStore.Capture(document.Session, Option(options, "--name", "CLI bindings"), document.Configuration);
                    string scope = Option(options, "--scope", "all");
                    if (scope != "all")
                    {
                        var selected = preset.Scopes.Where(item => scope == "keyboard" ? item.Family == null : item.Family != null && item.Family.Equals(scope, StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (selected.Length == 0) throw new ArgumentException("Unknown share scope: " + scope);
                        preset = UserPresetStore.SelectScopes(preset, selected);
                    }
                    Console.WriteLine(AgentJson.Serialize(new { ok = true, share = UserPresetStore.Share(preset), preset = preset })); return 0;
                }
                if (command == "--runtime-status")
                {
                    var live = document.Installation == null ? null : document.Installation.LiveStatus();
                    Console.WriteLine(AgentJson.Serialize(new { ok = true, running = EditorSession.IsGameRunning, live = live,
                        activeScheme = document.Configuration.Enabled ? live == null ? document.Configuration.ActiveSchemeId : live.ActiveSchemeId ?? document.Configuration.ActiveSchemeId : document.Session.ActiveProfile,
                        nativeActiveScheme = document.Session.ActiveProfile,
                        schemeError = document.Configuration.Enabled ? null : document.Session.SchemeError, readiness = Readiness(document) })); return 0;
                }
                var workspace = new BindingWorkspace(document.Session, document.Configuration, baseline, family != null, familyIndex);
                workspace.LiveEditing = HasFlag(args, "--live");
                if (command == "--edit")
                {
                    if (new[] { "--apply", "--live", "--out" }.Count(flag => HasFlag(args, flag)) > 1) throw new ArgumentException("Choose one of --out, --apply or --live.");
                    bool preview = HasFlag(args, "--dry-run");
                    if (preview && (HasFlag(args, "--apply") || options.ContainsKey("--out"))) throw new ArgumentException("--dry-run may be used alone or with --live; it cannot be combined with --apply or --out.");
                    if (!document.Configuration.Enabled && !document.Session.IsOffline && document.Session.ActiveProfile == null && scheme == null)
                        throw new ArgumentException("The active game scheme is unknown. Specify --scheme using an ID from --inspect.");
                    string requestPath = Option(options, "--request", null);
                    if (requestPath == null) throw new ArgumentException("Specify --request <JSON file or ->. Without --out, --apply or --live this is a dry run.");
                    string requestText = requestPath == "-" ? Console.In.ReadToEnd() : File.ReadAllText(requestPath);
                    stage = "plan";
                    var request = AgentJson.Deserialize<Dictionary<string, object>>(requestText);
                    if (request == null || request.Keys.Any(key => key != "operations")) throw new ArgumentException("Request must be an object containing only operations.");
                    var operations = ObjectValue<object[]>(request, "operations");
                    if (operations == null) throw new ArgumentException("operations must be an array.");
                    for (int i = 0; i < operations.Length; i++)
                    {
                        operationIndex = i;
                        var operation = operations[i] as Dictionary<string, object>;
                        if (operation == null) throw new ArgumentException("Each operation must be an object.");
                        if (HasFlag(args, "--live") && new[] { "activate", "reset-all", "enhancement" }.Contains(TextValue(operation, "op")))
                            throw new ArgumentException("Live editing cannot activate schemes, reset all schemes, or enable/disable enhancement. Close the game and use --apply.");
                        string operationDevice = TextValue(operation, "device", device);
                        var operationFamily = document.Session.ControllerFamilies.FirstOrDefault(item => item.Name.Equals(operationDevice, StringComparison.OrdinalIgnoreCase));
                        if (operationDevice != "keyboard" && operationFamily == null) throw new ArgumentException("Unknown operation device: " + operationDevice);
                        workspace = new BindingWorkspace(document.Session, document.Configuration, baseline, operationFamily != null, operationFamily == null ? -1 : operationFamily.Index) { LiveEditing = HasFlag(args, "--live") };
                        EditOperation(document, workspace, operation);
                    }
                    operationIndex = -1;
                    document.Configuration.CompileLua();
                    if (HasFlag(args, "--live"))
                    {
                        if (document.Installation == null) throw new InvalidOperationException("Live apply requires an installed game save.");
                        if (document.Configuration.Schemes.Count == 0) document.Session.Refresh();
                        var candidate = BindingLivePlan.Create(document.Session, document.Configuration, familyIndex < 0 ? document.Session.ControllerFamilies.First().Index : familyIndex);
                        if (!candidate.Enabled || !baseline.Enabled) throw new InvalidOperationException("Live editing requires enabled enhancement. Close the game, use --component --action enable, then start the game.");
                        document.Configuration = candidate;
                        if (!preview)
                        {
                            stage = "live_apply";
                            if (!EditorSession.IsGameRunning) throw new InvalidOperationException("The game is closed. Use --apply, or start the game and inspect --runtime-status before --live.");
                            outcome["liveState"] = "pending";
                            document.Installation.ApplyLive(candidate).GetAwaiter().GetResult(); outcome["liveState"] = "confirmed"; outcome["liveApplied"] = outcome["enhancementSaved"] = true;
                        }
                    }
                    else if (HasFlag(args, "--apply"))
                    {
                        if (document.Session.IsOffline || document.Installation == null) throw new InvalidOperationException("Use --out for offline editing.");
                        if (EditorSession.IsGameRunning) throw new InvalidOperationException("The game is running. Use --live for bindings, or close the game for save/installation changes.");
                        stage = "save_bindings";
                        var applied = document.Configuration.Enabled && document.Configuration.Schemes.Count > 0 ? new EditorApplyResult { Success = true } : document.Session.Apply(); outcome["bindingsSaved"] = applied.BindingsSaved; outcome["activationSaved"] = applied.ActivationSaved;
                        outcome["inputBackup"] = applied.InputBackup; outcome["activationBackup"] = applied.ActivationBackup;
                        if (!applied.Success) throw new IOException(applied.Error);
                        stage = "save_enhancement";
                        if (document.Configuration.Enabled && document.Configuration.Schemes.Count > 0) document.Configuration.ActiveSchemeId = document.Configuration.SelectedSchemeId;
                        document.Installation.Apply(document.Configuration); outcome["enhancementSaved"] = true;
                    }
                    string output = Option(options, "--out", null);
                    if (output != null) { stage = "export"; document.Export(output); outcome["exported"] = true; outcome["output"] = Path.GetFullPath(output); }
                }
                if (command != "--edit" && command != "--inspect" && command != "--catalog") throw new ArgumentException("Unknown command: " + command);
                workspace = new BindingWorkspace(document.Session, document.Configuration, baseline, family != null, familyIndex);
                string query = Option(options, "--query", "");
                var snapshot = workspace.GetSnapshot();
                var data = new {
                    ok = true, command = command, dryRun = command == "--edit" && (HasFlag(args, "--dry-run") || !HasFlag(args, "--apply") && !HasFlag(args, "--live") && !options.ContainsKey("--out")), result = outcome,
                    source = source, selectedScheme = document.Configuration.Enabled ? document.Configuration.SelectedSchemeId : document.Session.SelectedProfile,
                    activeScheme = document.Configuration.Enabled ? document.Installation == null ? document.Configuration.ActiveSchemeId : document.Installation.LiveStatus().ActiveSchemeId ?? document.Configuration.ActiveSchemeId : document.Session.ActiveProfile,
                    schemeError = document.Configuration.Enabled ? null : document.Session.SchemeError,
                    schemes = document.Configuration.Enabled ? document.Configuration.Schemes.Select(item => item.Id).ToArray() : document.Session.Profiles,
                    schemeNames = document.Configuration.Schemes.Select(item => new { item.Id, Name = KeyEnhancementConfiguration.SchemeName(item) }), device = device, devices = document.Session.ControllerFamilies,
                    running = EditorSession.IsGameRunning, live = document.Installation == null ? null : document.Installation.LiveStatus(),
                    readiness = Readiness(document, scheme != null),
                    functions = workspace.FindFunctionChoices(query).Select(function => new { function.Id, function.Label, function.TechnicalName, function.GroupPath, function.GroupLabels,
                        function.RequiresKeyEnhancement, function.FactoryKeys, function.Targets,
                        bindings = snapshot.Where(entry => entry.FunctionId == function.Id), enhancementCapacity = function.NativeAction == null || KeyEnhancementInputPlan.Covers(function.NativeAction) ? "no_fixed_slots" : "active_native_pipelines", capacity = function.NativeAction == null ? null : document.Session.RelationshipCapacity(function.Id, family != null, familyIndex) }),
                    configuration = document.Configuration, coverage = workspace.UncoveredInputs,
                    nativeChanges = document.Session.SchemeDifferences(false)
                };
                Console.WriteLine(AgentJson.Serialize(data)); return 0;
            }
            catch (Exception ex)
            {
                if (Convert.ToString(outcome["liveState"]) == "pending") outcome["liveState"] = "unconfirmed";
                Console.WriteLine(AgentJson.Serialize(new { ok = false, error = ex.Message, code = ex is ArgumentException ? "invalid_request" : "operation_failed", stage = stage, operationIndex = operationIndex < 0 ? (int?)null : operationIndex, result = outcome,
                    next = "Inspect the source again; retain the request and retry only operations that were not saved. --agent-help describes the schema." }));
                return ex is ArgumentException ? 2 : 1;
            }
        }

        static void EditOperation(BindingDocument document, BindingWorkspace workspace, Dictionary<string, object> operation)
        {
            string op = TextValue(operation, "op"), function = TextValue(operation, "function"), id = TextValue(operation, "id");
            string parameters;
            switch (op)
            {
                case "add": parameters = "function binding"; break;
                case "add-functions": parameters = "functions binding"; break;
                case "replace": parameters = "id binding"; break;
                case "unbind": case "enable": case "disable": parameters = "id"; break;
                case "restore": parameters = "function id"; break;
                case "restore-key": parameters = "key"; break;
                case "move-key": case "swap-keys": parameters = "from to"; break;
                case "copy": parameters = "id function"; break;
                case "native-trigger": parameters = "function enabled"; break;
                case "custom-add": parameters = "name target"; break;
                case "custom-edit": parameters = "function name target"; break;
                case "custom-delete": parameters = "function"; break;
                case "hook-set": parameters = "index hook"; break;
                case "hook-delete": parameters = "index"; break;
                case "enhancement": parameters = "enabled"; break;
                case "scheme-add": parameters = "name"; break;
                case "scheme-rename": parameters = "scheme name"; break;
                case "scheme-delete": case "scheme-select": parameters = "scheme"; break;
                case "configuration": parameters = "value"; break;
                case "preset": parameters = "preset share mode"; break;
                case "restore-bindings": case "reset-all": case "activate": parameters = ""; break;
                default: throw new ArgumentException("Unknown operation: " + op);
            }
            var allowed = ("op device " + parameters).Split(' ');
            foreach (string key in operation.Keys)
                if (!allowed.Contains(key)) throw new ArgumentException("Unsupported field for " + op + ": " + key);
            if (op.StartsWith("scheme-", StringComparison.Ordinal) && !workspace.Configuration.Enabled)
                throw new KeyEnhancementRequiredException();
            BindingEntry entry = id == null ? null : workspace.GetSnapshot().FirstOrDefault(item => item.Id == id);
            if (id != null && entry == null) throw new ArgumentException("Binding not found: " + id + ". Inspect again for current IDs.");
            switch (op)
            {
                case "add": workspace.Add(function, ObjectValue<BindingEntry>(operation, "binding")); break;
                case "add-functions": workspace.AddFunctions(ObjectValue<string[]>(operation, "functions"), ObjectValue<BindingEntry>(operation, "binding")); break;
                case "replace": workspace.Replace(RequireEntry(entry), ObjectValue<BindingEntry>(operation, "binding")); break;
                case "unbind": workspace.Remove(RequireEntry(entry)); break;
                case "enable": case "disable": workspace.SetEnabled(RequireEntry(entry), op == "enable"); break;
                case "restore": if (entry != null) workspace.Restore(entry); else workspace.Restore(function); break;
                case "restore-key": workspace.RestoreKey(TextValue(operation, "key")); break;
                case "move-key": case "swap-keys": workspace.MoveKey(TextValue(operation, "from"), TextValue(operation, "to"), op == "swap-keys"); break;
                case "copy": workspace.CopyBinding(RequireEntry(entry), function); break;
                case "native-trigger": workspace.SetNativeTrigger(function, ObjectValue<bool>(operation, "enabled")); break;
                case "custom-add": workspace.AddCustomDefinition(ObjectValue<KeyEnhancementOutput>(operation, "target"), TextValue(operation, "name")); break;
                case "custom-edit": workspace.UpdateCustomOutput(function, ObjectValue<KeyEnhancementOutput>(operation, "target"), TextValue(operation, "name")); break;
                case "custom-delete": workspace.DeleteCustomFunction(function); break;
                case "hook-set": workspace.SetHook(operation.ContainsKey("index") ? ObjectValue<int>(operation, "index") : -1, ObjectValue<Dictionary<string, object>>(operation, "hook")); break;
                case "hook-delete": workspace.Configuration.Hooks.RemoveAt(ObjectValue<int>(operation, "index")); break;
                case "enhancement":
                    workspace.Configuration.SetEnabled(ObjectValue<bool>(operation, "enabled"));
                    if (workspace.Configuration.Enabled) workspace.Configuration.EnsureSchemes(document.Session);
                    break;
                case "scheme-add": workspace.Configuration.AddScheme(TextValue(operation, "name")); break;
                case "scheme-rename": workspace.Configuration.RenameScheme(TextValue(operation, "scheme"), TextValue(operation, "name")); break;
                case "scheme-delete": workspace.Configuration.DeleteScheme(TextValue(operation, "scheme")); break;
                case "scheme-select": workspace.Configuration.SelectScheme(TextValue(operation, "scheme")); break;
                case "configuration": workspace.Configuration.ReplaceWith(ObjectValue<KeyEnhancementConfiguration>(operation, "value")); break;
                case "preset":
                    var preset = operation.ContainsKey("share") ? UserPresetStore.ParseShare(TextValue(operation, "share")) : Presets.All().Concat(UserPresetStore.Default().Load()).FirstOrDefault(item => item.Id == TextValue(operation, "preset"));
                    if (preset == null) throw new ArgumentException("Unknown preset");
                    PresetApplyMode mode; if (!Enum.TryParse(TextValue(operation, "mode", "Append"), true, out mode) || !Enum.IsDefined(typeof(PresetApplyMode), mode)) throw new ArgumentException("Invalid preset mode");
                    var planned = BindingPresetPlan.Create(document.Session, workspace.Configuration, preset, mode, workspace.FamilyIndex);
                    planned.Apply(document.Session); workspace.Configuration.ReplaceWith(planned.Configuration); break;
                case "restore-bindings": workspace.Configuration.ReplaceWith(BindingLivePlan.RestoreBindings(document.Session, workspace.Configuration, workspace.FamilyIndex)); break;
                case "reset-all": document.Session.ResetAll(); workspace.Configuration.ReplaceWith(new KeyEnhancementConfiguration()); break;
                case "activate":
                    if (workspace.Configuration.Enabled && workspace.Configuration.Schemes.Count > 0) workspace.Configuration.ActiveSchemeId = workspace.Configuration.SelectedSchemeId;
                    else document.Session.SetActivation(true);
                    break;
                default: throw new ArgumentException("Unknown operation: " + op);
            }
        }

        static BindingEntry RequireEntry(BindingEntry entry)
        { if (entry == null) throw new ArgumentException("This operation requires a binding id from --inspect."); return entry; }

        static object Fields(Type type)
        { return type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Select(field => new { name = field.Name, type = FieldTypeName(field.FieldType) }); }

        static string FieldTypeName(Type type)
        {
            if (type.IsArray) return FieldTypeName(type.GetElementType()) + "[]";
            if (type.IsGenericType) return type.Name.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(FieldTypeName)) + ">";
            return type.Name;
        }

        static object AgentHelp()
        {
            return new {
                ok = true, version = Program.Version, schemaVersion = 1, source = SourceInformation(),
                documentation = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "cli.md"),
                transport = "No prompts or GUI. Standard output is one UTF-8 JSON object for the agent commands, even on failure. --json is optional. Legacy conversion commands use plain text. Use request files (UTF-8, with or without BOM) to avoid PowerShell 5.1 stdin encoding loss; --request - also accepts piped UTF-8 JSON. No arguments prints this help.",
                commands = new[] { "--inspect <sav|zip|json|new> [--scheme <id>] [--device keyboard|Xbox|PS] [--query <text>] --json",
                    "--catalog [source] [--query <text>] --json", "--share <source> [--name <text>] [--scheme <id>] [--scope all|keyboard|Xbox|PS] --json", "--presets --json", "--keys --json", "--runtime-status <installed.sav> --json",
                    "--edit <source> --request <file.json|-> [--scheme <id>] [--device <device>] [--out <zip|json|sav> | --apply | --live [--dry-run]] --json",
                    "--component <installed.sav|game-directory> --action status|enable|disable|uninstall|check-update|update [--archive <ue4ss.zip>] --json",
                    "--gameplay-mod <installed.sav|game-directory> --action status|enable|disable|uninstall --json", "--version --json", "--activate <installed.sav> <profileId>",
                    "--selftest <sav>", "--list <sav>", "--schemes <sav>", "--apply-preset <in> <out> <presetId> [--scheme <id>]", "--make-vanilla <in> <out>", "--gen-presets <in> <directory>" },
                workflow = "Inspect first. Use stable function IDs and current binding IDs. Submit all intended changes as one operations array. Preview with --edit and no output/apply option; use ZIP to retain all data. Ordinary mode edits the three native save schemes. Enhancement mode uses independent simulated schemes, initially copied from the native schemes. --scheme selects the editing target; --apply or --live deploys the library and activates that target. Enhanced ordinary bindings, rules and suppressions belong to that scheme and never write game saves.",
                liveWorkflow = "Close game -> --component <installed.sav> --action enable -> start game -> --runtime-status until readiness.canLiveApply -> --inspect for current IDs -> --edit --live --dry-run -> --edit --live using the same request -> inspect again. Preview does not send a request or write files. Lifecycle/activation/reset-all operations cannot be mixed into live requests. restore-bindings resets keyboard and the selected controller family in the runtime configuration, removes binding rules and suppressions, preserves enhancement/components/custom definitions, and does not change save files. Load screens may delay acknowledgement; timeout cancels pending requests. Read result.liveState before retrying.",
                componentWorkflow = "--component manages key enhancement and shared UE4SS. Enable installs the bundled version without network prompts; disable preserves settings; uninstall returns RemovedFiles/RetainedFiles/SharedLoaderRetained and exit 1 if files remain. check-update queries online; update explicitly downloads a newer release, or uses --archive for a local validated package. --gameplay-mod separately manages Perfect Deflect No Rapid Click Penalty for all schemes/difficulties. All component writes require a closed game and take effect on the next launch.",
                operations = new[] { "add(function,binding)", "add-functions(functions,binding)", "replace(id,binding)", "unbind(id)", "enable(id)", "disable(id)", "restore(function|id)", "restore-key(key)", "restore-bindings()", "move-key(from,to)", "swap-keys(from,to)", "copy(id,function)", "native-trigger(function,enabled)", "custom-add(name,target)", "custom-edit(function,name,target)", "custom-delete(function)", "hook-set(index?,hook)", "hook-delete(index)", "enhancement(enabled)", "scheme-add(name)", "scheme-rename(scheme,name)", "scheme-delete(scheme)", "scheme-select(scheme)", "configuration(value)", "preset(preset|share,mode=Append|Replace|OnlyPreset)", "reset-all()", "activate()" },
                request = new { operations = new object[] { new { op = "add", function = "IA_Jump", device = "keyboard", binding = new BindingEntry { Keys = new[] { "RightControl", "J" }, Trigger = "hold", Seconds = .4, Rule = new KeyEnhancementRule { ConsumeKeys = new List<string> { "J" } } } } } },
                semantics = new {
                    binding = "Keys is a simultaneous chord. Trigger=default uses native behavior. Sequence and script details belong to Rule; output vectors use OutputValue. Disabled enhancement settings may be edited without running them.",
                    axis = "Trigger=axis, Axis=UE key; Rule.AxisDirection=both|positive|negative. Keys must be empty. Custom scripts declare all sampled keys/axes in Rule.Watch.",
                    rule = "Consume consumes all declared inputs; ConsumeKeys consumes a subset. Priority controls rule ordering. InputGroup lets tap/hold variants share one input; the editor creates this automatically.",
                    scripts = "WhenLua is a Lua function(context); EvaluateLua is function(now,snapshot,context,state) returning {active,pulse,reserve,value}. CustomLua extends compiled config. Use trusted local scripts.",
                    hooks = "{path=UFunction path,value=typed return,when=condition}; value may use {object=UObject path}, nested arrays/objects. evaluate is a Lua function expression for computed returns.",
                    outputs = "Kind=action|mapping|ability|event|function|property|lua|scheme. Function uses Path, Receiver and Args; property uses Property and Value; event uses Tag and Payload; lua uses Code. Scheme uses Tag=scheme ID|next|previous; these management bindings are shared across schemes and require enhancement. UObject descriptors resolve recursively.",
                    schemes = "scheme-add copies and selects the current enhanced scheme. scheme-select changes only the editing target until apply/live. scheme-rename and scheme-delete manage additional schemes; built-in or active schemes cannot be deleted. Scheme operations require enabled enhancement. Presets and share codes contain only the selected scheme; ZIP/JSON retain the library. Custom definitions, CustomLua and hooks are installation-wide.",
                    safety = "Dry runs and export do not change installed game state. A failed operation aborts the in-memory request; apply may partially save native/enhancement components, reported in result. Reinspect before retrying. Live apply requires updated Mod heartbeat. Enhanced scheme activation is independent of native scheme authentication. Native-only pipelines retain their available runtime slot count; unsupported plans are rejected, never silently dropped."
                },
                schema = new { binding = Fields(typeof(BindingEntry)), rule = Fields(typeof(KeyEnhancementRule)), output = Fields(typeof(KeyEnhancementOutput)), suppression = Fields(typeof(KeyEnhancementSuppression)), configuration = Fields(typeof(KeyEnhancementConfiguration)), scheme = Fields(typeof(KeyEnhancementScheme)), nativeOverride = Fields(typeof(NativeBindingOverride)), triggers = new[] { "default" }.Concat(KeyEnhancementConfiguration.Triggers), axisDirections = new[] { "both", "positive", "negative" } },
                exitCodes = new { success = 0, failedOrPartial = 1, invalidRequest = 2 },
                legacy = "Legacy save conversion commands write output copies of native schemes. Use --edit with --apply for installed saves. Never overwrite installed saves using a conversion command. In ordinary mode, if activeScheme is unknown, specify --scheme for editing. Enhanced schemes use their own IDs from --inspect and do not require native activation."
            };
        }
    }
}
