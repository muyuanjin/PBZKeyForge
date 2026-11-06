using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PBZKeyForge;

class BindingWorkspaceTests
{
    static int assertions;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); assertions++; }
    static BindingEntry Key(string key, string trigger = "default") { return new BindingEntry { Keys = new[] { key }, Trigger = trigger }; }
    static ActionRelations[] Relations(BindingWorkspace workspace)
    {
        var snapshot = typeof(BindingWorkspace).GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(workspace, null);
        return (ActionRelations[])snapshot.GetType().GetField("Current").GetValue(snapshot);
    }
    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
    static void Run(string[] args)
    {
        I18n.Current = "zh-Hans";
        var reusedSession = EditorSession.CreateOffline();
        var reusedConfig = new KeyEnhancementConfiguration();
        var reused = new BindingWorkspace(reusedSession, reusedConfig, reusedConfig.Clone(), false, -1);
        var catalog = reused.Functions;
        Check(object.ReferenceEquals(catalog, reused.Functions), "An unchanged catalog must be reused.");
        var cachedOutput = new KeyEnhancementOutput { Id = "reused", Kind = "function", Name = "Custom call", Path = "/Script/Test:First" };
        reusedConfig.CustomFunctions.Add(cachedOutput);
        Check(reused.Functions.Single(item => item.Id == "custom:reused").TechnicalName == cachedOutput.Path, "A reused workspace missed a newly added custom function.");
        cachedOutput.Path = "/Script/Test:Second";
        Check(reused.Functions.Single(item => item.Id == "custom:reused").SearchText.Contains(cachedOutput.Path), "A reused workspace kept an outdated custom target.");
        reusedConfig.CustomFunctions.Clear();
        Check(!reused.Functions.Any(item => item.Id == "custom:reused"), "A reused workspace retained a removed function.");
        reusedSession.SwitchProfile(reusedSession.Profiles.Last());
        Check(!object.ReferenceEquals(catalog, reused.Functions), "Switching schemes must refresh native factory bindings.");
        var session = EditorSession.CreateOffline();
        var config = new KeyEnhancementConfiguration();
        var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
        Check(workspace.Functions.Count(item => !item.RequiresKeyEnhancement) == 21, "Original functions must retain their mapping identities.");
        Check(workspace.Functions.Take(21).All(item => !item.RequiresKeyEnhancement), "Ordinary mode must start with usable save controls.");
        Check(workspace.Functions.All(item => item.GroupPath.Length > 0 && item.GroupPath.Length == item.GroupLabels.Length
            && item.GroupLabels.All(label => !string.IsNullOrWhiteSpace(label) && !label.StartsWith("unified."))), "Action groups must have translated labels at every level.");
        var pipa = workspace.Functions.Single(item => item.Id == "ability:Ability.Action.Hun.ShachiAbility.PiPa.Dash");
        var hook = workspace.Functions.Single(item => item.Id == "ability:Ability.Action.Hun.ShachiAbility.ShachiHook.CAA");
        Check(pipa.GroupPath[0] == "shadow" && hook.GroupPath[0] == "shadow" && pipa.GroupPath[1] != hook.GroupPath[1]
            && pipa.GroupLabels[1] == "催心琵琶" && hook.GroupLabels[1] == "追命锁镰", "Phantom weapons must have separate named branches from native resource evidence.");
        var saw = workspace.Functions.Where(item => item.Id.StartsWith("ability:Ability.Action.Hun.Light.JigSaw.")).ToArray();
        Check(saw.Length == 4 && saw.All(item => item.GroupPath[0] == "weapons" && item.GroupLabels[1] == "断肢锯"
            && item.Label.Contains("断肢锯")), "Case differences in native weapon tags lost the weapon name or group.");
        var camera = workspace.Functions.Single(item => item.Id == "action:/SGameLocomotionDriver/Input/IA_MouseLookInput");
        var nextTab = workspace.Functions.Single(item => item.Id == "action:/Game/PBZContent/Input/InputActions/IA_NextTab");
        var previousTab = workspace.Functions.Single(item => item.Id == "action:/Game/PBZContent/Input/InputActions/IA_PreviousTab");
        Check(camera.GroupPath.SequenceEqual(new[] { "targeting", "camera" }) && camera.Label.Contains("摄像机") && camera.Label.Contains("鼠标")
            && nextTab.GroupPath.SequenceEqual(new[] { "interface", "tabs" })
            && previousTab.GroupPath.SequenceEqual(nextTab.GroupPath)
            && nextTab.Label.Contains("下一个") && previousTab.Label.Contains("上一个"),
            "Camera and menu inputs must expose their player purpose and scene rather than resource-table categories.");
        string deflect = KeyEnhancementCatalog.Targets.First(item => item.Name == "IA_Deflect").Id;
        if (args.Contains("--localization-fixture"))
        {
            var localized = workspace.Functions.Single(item => item.Id == deflect);
            Check(localized.Label == "测试防御名称" && localized.LabelSource == "native", "Native labels were not loaded from the embedded localization catalog.");
            Check(localized.SearchText.Contains("测试资源别名"), "Localization aliases were not searchable.");
        }
        var inactive = workspace.Add(deflect, Key("F9"));
        Check(!config.Enabled && !inactive.Active && config.Rules.Count == 1, "Disabled enhancement must allow editing without enabling execution.");
        workspace.Remove(inactive);
        var native = workspace.Add("IA_Jump", Key("J"));
        Check(native.IsNative && session.Relations("IA_Jump", false, -1).Keys.Contains("J"), "A supported ordinary binding must use the save slot.");
        config.Enabled = true;
        Check(workspace.Functions.Where(item => string.Join("/", item.GroupPath) == "locomotion/movement").Take(4).Select(item => item.Id).SequenceEqual(new[] { "movement:forward", "movement:backward", "movement:left", "movement:right" }),
            "Movement category must start with directional movement.");
        var extra = workspace.Add(deflect, Key("F9", "press"));
        Check(!extra.IsNative && extra.FunctionId == deflect, "Independent deflect input must not become full guard.");
        workspace.Remove(extra);
        Check(config.Rules.Count == 0, "Removing the final output must remove its rule.");
        var guard = workspace.Entries("IA_Guard").First(item => item.IsNative);
        var replacement = workspace.Replace(guard, Key("G", "tap"));
        Check(config.Rules[0].Outputs[0].Kind == "mapping" && config.Rules[0].Outputs[0].MappingActions.Length == 2,
            "Full guard must clone both native input pipelines.");
        Check(config.Rules[0].Suppressions.Count == 2 && workspace.Entries("IA_Guard").Any(item => item.IsNative && item.Suppressed),
            "Replacing a native binding must suppress its source only while the replacement is active.");
        config.Rules[0].Outputs.Add(new KeyEnhancementOutput { Kind = "action", Path = KeyEnhancementCatalog.Targets.First(item => item.Id == deflect).Path });
        config.Rules[0].WhenLua = "function(context) return context.gameplay end";
        replacement = workspace.Entries("IA_Guard").First(item => !item.IsNative);
        workspace.Replace(replacement, Key("H", "hold"));
        Check(config.Rules.Count == 2 && config.Rules.All(rule => rule.Outputs.Count == 1), "Editing one shared output must preserve the other output.");
        var edited = workspace.Entries("IA_Guard").First(item => !item.IsNative);
        Check(edited.Rule.WhenLua != null && edited.Rule.Suppressions.Count == 2, "Detached output lost its conditions or replacement ownership.");
        workspace.Remove(edited);
        Check(workspace.Entries("IA_Guard").All(item => !item.Suppressed), "Deleting a replacement must restore the original input.");
        Check(workspace.Entries(deflect).Length == 1, "Deleting a full-guard output removed the independent deflect output.");
        var sequence = new BindingEntry { Keys = new string[0], Trigger = "sequence", Rule = new KeyEnhancementRule { Trigger = "sequence" } };
        sequence.Rule.Steps.Add(new[] { "A" }); sequence.Rule.Steps.Add(new[] { "B" });
        var added = workspace.Add(deflect, sequence);
        sequence.Rule.Steps[1] = new[] { "C" };
        workspace.Replace(added, sequence);
        Check(config.Rules.Last().Steps[1][0] == "C", "Editing a sequence lost the updated steps.");
        workspace.Restore(deflect);
        Check(config.Rules.Count == 0, "Restoring a function left its custom bindings.");
        Check(Relations(workspace).Any(item => item.Action == "IA_Jump" && item.Keys.Contains("J")), "Unified snapshot lost a save binding.");
        workspace.Add("IA_Jump", Key("J", "hold"));
        config.Enabled = false;
        workspace.RemoveRelationship("IA_Jump", "J");
        Check(!session.Relations("IA_Jump", false, -1).Keys.Contains("J") && config.Rules.Count == 1,
            "Removing a native binding with key enhancement disabled must preserve inactive advanced configuration.");
        workspace.RestoreKey("J");
        Check(config.Rules.Count == 0, "Restoring a key left its disabled enhanced configuration behind.");
        config.Enabled = true;
        workspace.Add("IA_Jump", Key("J"));
        Check(Relations(workspace).Single(item => item.Action == "IA_Jump").Keys.Count(key => key == "J") == 1,
            "Unified key view counted a save binding and key enhancement binding twice.");
        workspace.RemoveRelationship("IA_Jump", "J");
        Check(!Relations(workspace).Single(item => item.Action == "IA_Jump").Keys.Contains("J"),
            "Removing a unified relationship left a duplicate source active.");
        Check(workspace.Functions.First(item => item.Id == "ability:Ability.Action.Hun.SpecialDash").SearchText.Contains("瞬身"),
            "Stance dash must be searchable by its player-facing name.");
        var custom = new KeyEnhancementRule(); custom.Keys.Add("F12");
        custom.Outputs.Add(new KeyEnhancementOutput { Kind = "function", Path = "/Script/Engine.Character:Jump", Args = new object[] { 7 } });
        config.Rules.Add(custom);
        var refreshed = new BindingWorkspace(session, config, config.Clone(), false, -1);
        var customFunction = refreshed.Functions.First(item => item.Id.StartsWith("target:function:", StringComparison.Ordinal));
        refreshed.Add(customFunction.Id, Key("F11"));
        Check(config.Rules.Last().Outputs[0].Args.Length == 1 && Convert.ToInt32(config.Rules.Last().Outputs[0].Args[0]) == 7,
            "Adding a binding to an imported custom function lost its typed arguments.");
        var evaluator = new KeyEnhancementRule { Trigger = "custom", EvaluateLua = "function() return {active=true} end" };
        evaluator.Keys.Add("F10"); evaluator.Outputs.Add(new KeyEnhancementOutput { Kind = "action", Path = KeyEnhancementCatalog.Targets.First(item => item.Id == deflect).Path });
        config.Rules.Add(evaluator);
        var customEntry = refreshed.Entries(deflect).First(item => item.RuleId == evaluator.Id);
        var changedTrigger = Key("F10", "hold"); changedTrigger.Rule = customEntry.Rule;
        refreshed.Replace(customEntry, changedTrigger);
        Check(evaluator.Trigger == "hold" && evaluator.EvaluateLua == null, "Changing a custom recognizer to a normal trigger kept the old evaluator active.");
        var named = refreshed.AddCustom(new KeyEnhancementOutput { Kind = "property", Property = "bFlag", Value = false }, Key("F8"), "自定义状态切换");
        Check(refreshed.Functions.Single(item => item.Id == named.FunctionId).Label == "自定义状态切换" && config.Rules.Last().Outputs[0].Name == "自定义状态切换",
            "Custom function name was not preserved with its binding.");
        Check(config.Rules.Last().Trigger == "press", "Custom function default must call once on press.");
        refreshed.Add(named.FunctionId, Key("F7", "hold"));
        var updatedTarget = refreshed.CustomOutput(named.FunctionId); updatedTarget.Value = true;
        string updatedId = refreshed.UpdateCustomOutput(named.FunctionId, updatedTarget, "更新状态");
        Check(updatedId == named.FunctionId && refreshed.Entries(updatedId).Length == 2
            && refreshed.Functions.Single(item => item.Id == updatedId).Label == "更新状态",
            "Editing a custom function lost its identity, display name or bindings.");
        Check(config.Rules.Where(rule => rule.Outputs.Any(output => output.Id == updatedTarget.Id)).All(rule => (bool)rule.Outputs[0].Value)
            && refreshed.Entries(updatedId).Any(item => item.Trigger == "hold"), "Editing a function changed its triggers or left stale target values.");
        refreshed.AddHook(new System.Collections.Generic.Dictionary<string, object> { { "path", "/Script/Test:Enabled" }, { "value", false } });
        Check(config.Hooks.Count == 1, "A return hook was not stored in the shared configuration.");
        bool duplicateHook = false;
        try { refreshed.AddHook(new System.Collections.Generic.Dictionary<string, object> { { "path", "/Script/Test:Enabled" }, { "value", true } }); }
        catch (ArgumentException) { duplicateHook = true; }
        Check(duplicateHook && config.Hooks.Count == 1, "A duplicate return hook path was silently accepted.");
        refreshed.SetHook(0, new System.Collections.Generic.Dictionary<string, object> { { "path", "/Script/Test:Enabled" }, { "value", true } });
        Check((bool)config.Hooks[0]["value"], "Editing an existing hook rejected its unchanged function path.");
        refreshed.AddHook(new System.Collections.Generic.Dictionary<string, object> { { "path", "/Script/Test:Other" }, { "value", false } });
        duplicateHook = false;
        try { refreshed.SetHook(1, new System.Collections.Generic.Dictionary<string, object> { { "path", "/Script/Test:Enabled" }, { "value", false } }); }
        catch (ArgumentException) { duplicateHook = true; }
        Check(duplicateHook && (string)config.Hooks[1]["path"] == "/Script/Test:Other", "Editing a hook replaced another function's hook or changed the rejected draft.");
        var directed = Key("F13"); directed.OutputValue = new System.Collections.Generic.Dictionary<string, object> { { "x", 0 }, { "y", 1 }, { "z", 0 } };
        var directedEntry = refreshed.Add("IA_Jump", directed);
        Check(!directedEntry.IsNative && directedEntry.OutputValue != null && !session.Relations("IA_Jump", false, -1).Keys.Contains("F13"),
            "A directed action value was discarded into an ordinary save slot.");
        directed.OutputValue = new System.Collections.Generic.Dictionary<string, object> { { "x", -1 }, { "y", 0 }, { "z", 0 } };
        var redirected = refreshed.Replace(directedEntry, directed);
        Check(Convert.ToInt32(((System.Collections.Generic.Dictionary<string, object>)redirected.OutputValue)["x"]) == -1,
            "Editing a binding lost its output vector.");
        var consumed = Key("F14"); consumed.Priority = 10; consumed.Rule = new KeyEnhancementRule(); consumed.Rule.ConsumeKeys.Add("F14");
        var consumedEntry = refreshed.Add("IA_Jump", consumed);
        Check(!consumedEntry.IsNative && consumedEntry.Priority == 10 && consumedEntry.Rule.ConsumeKeys.SequenceEqual(new[] { "F14" }),
            "An ordinary-looking key binding discarded its priority or input consumption.");
        string specialDash = "ability:Ability.Action.Hun.SpecialDash";
        refreshed.SetNativeTrigger(specialDash, false);
        Check(!refreshed.UsesNativeTrigger(specialDash) && config.NativeAbilitySuppressions.Contains("Ability.Action.Hun.SpecialDash")
            && config.CompileLua().Contains("ability_suppressions"), "The native stance/dodge trigger could not be disabled independently of dodge.");
        Check(config.NativeAbilitySuppressions.Contains("Ability.Action.Hun.DeflectDash"),
            "Disabling native Flash Step left its guard-start variant reachable from dodge.");
        refreshed.Restore(specialDash);
        Check(refreshed.UsesNativeTrigger(specialDash) && !config.NativeAbilitySuppressions.Any(),
            "Restoring an ability left its original trigger suppressed.");
        var shared = new KeyEnhancementRule { Id = "snapshot-shared", Trigger = "hold" };
        shared.Keys.Add("F15");
        shared.Outputs.Add(new KeyEnhancementOutput { Kind = "mapping", MappingName = "IA_Jump", MappingActions = KeyEnhancementCatalog.ForMapping("IA_Jump").Select(target => target.Path).ToArray() });
        shared.Outputs.Add(new KeyEnhancementOutput { Kind = "ability", Tag = "Ability.Action.Hun.SpecialDash" });
        config.Rules.Add(shared);
        var sharedEntries = refreshed.GetSnapshot().Where(item => item.RuleId == shared.Id).ToArray();
        Check(sharedEntries.Length == 2 && sharedEntries.All(item => item.Pending && item.Active && item.Shared)
            && sharedEntries.Select(item => item.FunctionId).Contains(specialDash), "Batch snapshots lost shared outputs, pending state or active state.");
        sharedEntries[0].Rule.Keys.Add("F16");
        Check(shared.Keys.Count == 1 && sharedEntries[1].Rule.Keys.Count == 1, "Editing one snapshot mutated live configuration or another binding.");
        UnsupportedMappingRules();
        RestoreSuppressedRelationship();
        RestorePhysicalTriggers();
        LiveFixedInputs();
        RestoreRuntimeRelationship();
        Console.WriteLine("BindingWorkspaceTests: " + assertions + " assertions passed");
    }

    static void LiveFixedInputs()
    {
        var session = EditorSession.CreateOffline();
        byte[] save = session.ExportCurrentScheme();
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1) { LiveEditing = true };
        string[] directions = { "forward", "backward", "left", "right" };
        string[] keys = { "W", "S", "A", "D" };
        for (int i = 0; i < directions.Length; i++)
        {
            string id = "movement:" + directions[i];
            var original = workspace.Entries(id).Single(entry => entry.Keys.Contains(keys[i]));
            workspace.Remove(original);
            config.CompileLua();
            Check(config.NativeBindings.Count == 0 && config.Suppressions.Any(item => item.FunctionId == id && item.Key == keys[i]),
                "Live fixed unbinding must use suppression instead of a native mapping override");
            Check(workspace.Entries(id).Single(entry => entry.Keys.Contains(keys[i])).Suppressed
                && workspace.Entries(id).Any(entry => entry.Active), "Unbinding a fixed direction affected its arrow source");
            workspace.Restore(original);
            Check(workspace.Entries(id).All(entry => entry.Active) && config.Suppressions.Count == 0, "Single fixed-input restore did not clear suppression");
            var replacement = workspace.Replace(original, Key("F9"));
            config.CompileLua();
            Check(!replacement.IsNative && replacement.OutputValue != null && config.NativeBindings.Count == 0
                && workspace.Entries(id).Single(entry => entry.Keys.Contains(keys[i])).Suppressed,
                "Plain live replacement of a fixed direction did not preserve its directional rule");
            workspace.Restore(id);
        }
        Check(session.ExportCurrentScheme().SequenceEqual(save), "Live fixed editing changed save bytes");
    }

    static void RestoreRuntimeRelationship()
    {
        foreach (bool pad in new[] { false, true })
        foreach (bool live in new[] { false, true })
        {
            var session = EditorSession.CreateOffline();
            byte[] save = session.ExportCurrentScheme();
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(session, config, config.Clone(), pad, pad ? session.ControllerFamilies.First().Index : -1) { LiveEditing = true };
            string originalKey = pad ? "Gamepad_FaceButton_Bottom" : "SpaceBar";
            string replacementKey = pad ? "Gamepad_FaceButton_Left" : "J";
            var original = workspace.Entries("IA_Jump").Single(entry => entry.IsNative && entry.Keys.Contains(originalKey));
            workspace.Replace(original, Key(replacementKey)); workspace.LiveEditing = live;
            workspace.Restore(original);
            Check(config.NativeBindings.Single().Keys.OrderBy(key => key).SequenceEqual(new[] { originalKey, replacementKey }.OrderBy(key => key)),
                "Single factory restore ignored the effective runtime override or removed its custom key");
            string before = config.ToJson(); workspace.Restore(original);
            Check(config.ToJson() == before, "Repeated runtime factory restore duplicated the original key");
            var runtime = workspace.Entries("IA_Jump").Single(entry => entry.Keys.Contains(originalKey));
            workspace.Remove(runtime); workspace.Restore(runtime);
            Check(workspace.Entries("IA_Jump").Any(entry => entry.Active && entry.Keys.Contains(originalKey)), "Restoring a removed runtime entry left it unbound");
            config.CompileLua();
            Check(session.ExportCurrentScheme().SequenceEqual(save), "Runtime relationship restoration changed save bytes");
        }
    }

    static void RestorePhysicalTriggers()
    {
        foreach (string side in new[] { "Right", "Left" })
        foreach (bool axisName in new[] { false, true })
        {
            var session = EditorSession.CreateOffline();
            int family = session.ControllerFamilies.First(item => item.Name == "Xbox").Index;
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(session, config, config.Clone(), true, family);
            string button = "Gamepad_" + side + "Trigger", axis = button + "Axis";
            string action = side == "Right" ? "IA_ShadowArm" : "IA_UltimateSkill";
            var before = session.Relations(action, true, family);
            foreach (string key in new[] { button, axis }.Where(before.Keys.Contains))
                session.ChangeRelationship(action, key, false, true, family);
            workspace.Add("IA_Jump", Key(axis, "hold"));
            workspace.Add("IA_Jump", Key(button, "tap"));
            var unrelated = workspace.Add("IA_Jump", Key("Gamepad_FaceButton_Left", "hold"));
            foreach (string key in new[] { button, axis, "Gamepad_FaceButton_Left" })
            {
                config.Suppressions.Add(new KeyEnhancementSuppression { ActionPath = KeyEnhancementCatalog.ForMapping(action)[0].Path, Key = key });
                config.Rules.Last().Suppressions.Add(new KeyEnhancementSuppression { ActionPath = KeyEnhancementCatalog.ForMapping(action)[0].Path, Key = key });
            }
            workspace.RestoreKey(axisName ? axis : button);
            Check(session.Relations(action, true, family).Keys.OrderBy(key => key).SequenceEqual(before.Keys.OrderBy(key => key)), "Physical trigger restore omitted its native axis or button.");
            Check(config.Rules.Count == 1 && config.Rules[0].Id == unrelated.RuleId && config.Suppressions.Single().Key == "Gamepad_FaceButton_Left"
                && config.Rules[0].Suppressions.Single().Key == "Gamepad_FaceButton_Left", "Physical trigger restore omitted enhanced inputs/suppressions or changed another key.");
            string json = config.ToJson(); byte[] save = session.ExportCurrentScheme();
            workspace.RestoreKey(button);
            Check(json == config.ToJson() && save.SequenceEqual(session.ExportCurrentScheme()), "Repeated physical trigger restoration changed the draft.");
        }

        var full = EditorSession.CreateOffline();
        int xbox = full.ControllerFamilies.First(item => item.Name == "Xbox").Index;
        var rules = new KeyEnhancementConfiguration { Enabled = true };
        var ws = new BindingWorkspace(full, rules, rules.Clone(), true, xbox);
        full.ChangeRelationship("IA_Jump", "Gamepad_FaceButton_Bottom", false, true, xbox);
        full.ChangeRelationship("IA_Jump", "Gamepad_RightTrigger", true, true, xbox);
        foreach (string key in full.Relations("IA_ShadowArm", true, xbox).Keys.ToArray())
            full.ChangeRelationship("IA_ShadowArm", key, false, true, xbox);
        string[] replacements = { "Gamepad_FaceButton_Left", "Gamepad_FaceButton_Top", "Gamepad_LeftShoulder" };
        for (int index = 0; index < replacements.Length && full.HasGamepadSlot("IA_ShadowArm", index, xbox); index++)
        {
            full.ChangeRelationship("IA_ShadowArm", replacements[index], true, true, xbox);
        }
        ws.Add("IA_Jump", Key("Gamepad_RightTriggerAxis", "hold"));
        rules.Enabled = false;
        string priorRules = rules.ToJson(); byte[] priorSave = full.ExportCurrentScheme(); bool rejected = false;
        try { ws.RestoreKey("Gamepad_RightTrigger"); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && priorRules == rules.ToJson() && priorSave.SequenceEqual(full.ExportCurrentScheme()), "Failed axis restore committed button edits or removed enhancement rules.");
        rules.Enabled = true;
        full.ChangeRelationship("IA_ShadowArm", replacements[0], false, true, xbox);
        ws.RestoreKey("Gamepad_RightTrigger");
        Check(full.Relations("IA_ShadowArm", true, xbox).Keys.Contains("Gamepad_RightTriggerAxis")
            && !full.Relations("IA_Jump", true, xbox).Keys.Contains("Gamepad_RightTrigger") && rules.Rules.Count == 0, "Retry after freeing a slot failed to restore the whole trigger.");
    }

    static void UnsupportedMappingRules()
    {
        const string map = "IA_OpenMapMenu";
        var session = EditorSession.CreateOffline();
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
        var ordinary = workspace.Add(map, Key("K"));
        Check(ordinary.IsNative && ordinary.Active && ordinary.ExecutionOwner == "game", "Native-only functions must still accept ordinary save bindings.");
        string before = config.ToJson(); byte[] save = session.ExportCurrentScheme();
        bool rejected = false;
        try { workspace.Add(map, Key("K", "hold")); }
        catch (InvalidDataException ex) { rejected = ex.Message.Contains(map) && ex.Message.Contains(BindingWorkspace.FunctionLabel(map)) && ex.Message.Contains("普通按键"); }
        Check(rejected && config.ToJson() == before && save.SequenceEqual(session.ExportCurrentScheme()), "Unsupported complex add must explain the function and leave the draft intact.");
        rejected = false;
        try { workspace.Replace(ordinary, Key("L", "hold")); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && config.ToJson() == before && workspace.Entries(map).Single(entry => entry.Id == ordinary.Id).Active,
            "Rejected complex replacement suppressed or changed the ordinary map relation.");
        for (int index = 15; index < 24 && workspace.CanAddNative(map); index++) workspace.Add(map, Key("F" + index));
        Check(!workspace.CanAddNative(map), "Fixture failed to fill native map slots.");
        save = session.ExportCurrentScheme(); rejected = false;
        try { workspace.Add(map, Key("F24")); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && config.ToJson() == before && save.SequenceEqual(session.ExportCurrentScheme()), "Full native-only slots silently created an unroutable extra binding.");
        var output = new KeyEnhancementOutput { Kind = "mapping", MappingName = map, MappingActions = KeyEnhancementCatalog.ForMapping(map).Select(target => target.Path).ToArray() };
        rejected = false;
        try { workspace.AddCustom(output, Key("F14", "hold"), "地图"); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && config.ToJson() == before, "Custom output entry bypassed native-only mapping validation.");
        var imported = new KeyEnhancementRule { Id = "unsupported-map", Trigger = "hold" };
        imported.Keys.Add("K"); imported.Outputs.Add(output); config.Rules.Add(imported);
        var loaded = KeyEnhancementConfiguration.FromJson(config.ToJson());
        var importedEntry = new BindingWorkspace(session, loaded, loaded.Clone(), false, -1).GetSnapshot().Single(item => item.RuleId == imported.Id);
        Check(importedEntry.ExecutionOwner == "uncovered" && !importedEntry.Active && loaded.Rules.Count == 1, "Imported unsupported mapping was presented as scheduled or discarded.");
        rejected = false;
        try { loaded.CompileLua(); } catch (InvalidDataException ex) { rejected = ex.Message.Contains(map); }
        Check(rejected, "Compilation accepted an imported mapping that runtime expansion always filters.");
        loaded.Enabled = false;
        Check(loaded.CompileLua().Contains("return config") && loaded.Rules[0].Enabled,
            "Turning key enhancement off must preserve unsupported rules and allow native input to be restored.");
        loaded.Enabled = true;
        loaded.Rules[0].Enabled = false;
        Check(loaded.CompileLua().Contains("return config") && loaded.Rules.Count == 1, "Disabling unsupported imported rules must preserve them and allow supported configuration to compile.");
    }

    static void RestoreSuppressedRelationship()
    {
        var session = EditorSession.CreateOffline(); var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
        var original = workspace.Entries("IA_Guard").Single(entry => entry.IsNative && entry.Keys.Contains("RightMouseButton"));
        var replacement = workspace.Replace(original, Key("G", "tap"));
        var unrelated = new KeyEnhancementSuppression { ActionPath = KeyEnhancementCatalog.ForMapping("IA_Dodge")[0].Path, Key = "RightMouseButton" };
        config.Suppressions.Add(unrelated);
        var otherKey = new KeyEnhancementSuppression { ActionPath = KeyEnhancementCatalog.ForMapping("IA_Guard")[0].Path, Key = "LeftMouseButton" };
        config.Rules[0].Suppressions.Add(otherKey);
        byte[] save = session.ExportCurrentScheme();
        var restored = workspace.Add("IA_Guard", Key("RightMouseButton"));
        Check(restored.Active && !restored.Suppressed && restored.IsNative && save.SequenceEqual(session.ExportCurrentScheme()),
            "Restoring the key-view native relationship failed to clear suppression without rewriting saved keys.");
        Check(config.Rules.Count == 1 && config.Rules[0].Id == replacement.RuleId && config.Rules[0].Keys.SequenceEqual(new[] { "G" }) && config.Rules[0].Trigger == "tap",
            "Restoring the original relationship removed or changed its replacement binding.");
        Check(config.Suppressions.Contains(unrelated) && config.Rules[0].Suppressions.Count == 1 && config.Rules[0].Suppressions[0] == otherKey,
            "Restoring one key/function relationship changed other suppression relationships.");
        string before = config.ToJson(); workspace.Add("IA_Guard", Key("RightMouseButton"));
        Check(config.ToJson() == before && workspace.Entries("IA_Guard").Count(entry => entry.IsNative && entry.Keys.Contains("RightMouseButton")) == 1,
            "Repeated restoration duplicated bindings or changed other rules.");
        var custom = workspace.Add("IA_Guard", Key("F12")); workspace.Replace(custom, Key("F11", "tap"));
        var restoredCustom = workspace.Add("IA_Guard", Key("F12"));
        Check(restoredCustom.Active && !restoredCustom.Suppressed && session.Relations("IA_Guard", false, -1).Keys.Contains("F12"),
            "Restoring a suppressed custom native key reset it to factory state instead of reactivating it.");
    }
}
