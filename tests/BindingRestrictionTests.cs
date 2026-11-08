using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using PBZKeyForge;

class BindingRestrictionTests
{
    public sealed class RestrictionEntry { public Dictionary<string, object> preparation = new Dictionary<string, object>(); }
    public sealed class RestrictionDocument { public Dictionary<string, RestrictionEntry> entries = new Dictionary<string, RestrictionEntry>(); }
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string Move = "move:Weapon.Weapon_Hun_ChiRen.GA_Hun_ChiRen_B";
    const string OtherMove = "move:Weapon.Weapon_Hun_ChiRen.GA_Hun_ChiRen_BB";
    static readonly string[] Kinds = { "equipment", "training", "space", "stance", "predecessor", "cooldown" };
    static int checks;
    static string output;
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] args)
    {
        try { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static BindingEntry Specification(string key, bool resources, bool restrictions, string[] kinds)
    {
        return new BindingEntry { Keys = new[] { key }, Trigger = "default", BypassResources = resources,
            BypassRestrictions = restrictions, RestrictionBypasses = kinds.ToArray() };
    }
    static void AssertRule(KeyEnhancementRule rule, bool resources, bool restrictions, string[] kinds, string operation)
    {
        Check(rule.BypassResources == resources && rule.BypassRestrictions == restrictions
            && rule.RestrictionBypasses.SequenceEqual(kinds), operation + " lost independent bypass settings");
    }
    static void AssertEntry(BindingEntry entry, bool resources, bool restrictions, string[] kinds, string operation)
    {
        Check(entry.BypassResources == resources && entry.BypassRestrictions == restrictions
            && entry.RestrictionBypasses.SequenceEqual(kinds), operation + " lost independent bypass settings");
    }
    static void Shot(Form form, string name)
    {
        using (var bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, name + ".png")); }
    }
    static void Save(UnifiedBindingEditDialog dialog)
    { Call(dialog, "Save"); Check(dialog.Result != null, "The binding editor did not save"); }

    static void Persistence()
    {
        var session = EditorSession.CreateOffline();
        foreach (bool resources in new[] { false, true })
        foreach (bool restrictions in new[] { false, true })
        {
            string name = "bypass-" + resources + "-" + restrictions;
            string[] selected = { "equipment", "space", "predecessor" };
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
            workspace.Add(Move, Specification("F9", resources, restrictions, selected));
            AssertRule(config.Rules.Single(), resources, restrictions, selected, "Add " + name);
            AssertRule(config.Clone().Rules.Single(), resources, restrictions, selected, "Clone " + name);
            var json = KeyEnhancementConfiguration.FromJson(config.ToJson());
            AssertRule(json.Rules.Single(), resources, restrictions, selected, "JSON " + name);
            var preset = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session, name, config)));
            var shared = preset.KeyEnhancement;
            AssertRule(shared.Rules.Single(), resources, restrictions, selected, "Share " + name);
            var planType = typeof(BindingWorkspace).Assembly.GetType("PBZKeyForge.BindingPresetPlan");
            foreach (var mode in new[] { PresetApplyMode.Append, PresetApplyMode.Replace, PresetApplyMode.OnlyPreset })
            {
                var plan = planType.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                    new object[] { EditorSession.CreateOffline(), new KeyEnhancementConfiguration(), preset, mode, -1 });
                var applied = Field<KeyEnhancementConfiguration>(plan, "Configuration");
                AssertRule(applied.Rules.Single(rule => rule.Outputs.Any(target => target.Tag == Move)),
                    resources, restrictions, selected, "Preset " + mode + " " + name);
            }
            string jsonPath = Path.Combine(output, name + ".json");
            new BindingDocument { Session = session, Configuration = config }.Export(jsonPath);
            AssertRule(BindingDocument.Open(jsonPath).Configuration.Rules.Single(), resources, restrictions, selected, "JSON document " + name);
            string zip = Path.Combine(output, name + ".zip");
            new BindingDocument { Session = session, Configuration = shared }.Export(zip);
            AssertRule(BindingDocument.Open(zip).Configuration.Rules.Single(), resources, restrictions, selected, "ZIP " + name);
            string lua = shared.CompileLua();
            Check(lua.Contains("[\"bypass_resources\"]") == resources, "Resource compiler setting is coupled to restrictions");
            Check(lua.Contains("[\"restriction_bypasses\"]") == restrictions, "Restriction compiler setting is coupled to resources");
            Check(lua.Contains("[\"restriction_groups\"]") == restrictions, "Compiled target gates must follow restriction master");
            if (restrictions)
            {
                Check(lua.Contains("[\"equipment_configs\"]") && lua.Contains("[\"equipment_slot\"]")
                    && lua.Contains("Weapon.Slot.Main"), "Compiled weapon binding omitted native equipment preparation");
                Check(lua.Contains("[\"restriction_tags\"]"), "Compiled restrictions omitted tag classification");
                Check(lua.Contains("[\"armed_state\"]") && lua.Contains("[\"armed\"]=true"),
                    "Compiled weapon binding omitted its real armed-state preparation");
            }
            File.WriteAllText(Path.Combine(output, name + ".lua"), lua);
        }
        foreach (string kind in Kinds)
        {
            var config = new KeyEnhancementConfiguration { Enabled = true };
            new BindingWorkspace(session, config, config.Clone(), false, -1).Add(Move,
                Specification("F10", false, true, new[] { kind }));
            AssertRule(UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session, kind, config)))
                .KeyEnhancement.Rules.Single(), false, true, new[] { kind }, "Individual " + kind);
            var compiled = Regex.Match(config.CompileLua(), "\\[\"restriction_bypasses\"\\]\\s*=\\s*\\{([^}]*)\\}");
            Check(compiled.Success && compiled.Groups[1].Value.Contains("\"" + kind + "\"")
                && Kinds.Where(other => other != kind).All(other => !compiled.Groups[1].Value.Contains("\"" + other + "\"")),
                "The compiler merged independently selected restriction kinds: " + kind);
        }
    }

    static void Defaults()
    {
        Check(!new KeyEnhancementRule().BypassResources && !new KeyEnhancementRule().BypassRestrictions,
            "Bypass must be opt-in for existing configurations");
        var legacy = KeyEnhancementConfiguration.FromJson("{\"Enabled\":true,\"Rules\":[{\"Keys\":[\"F9\"],\"Outputs\":[{\"Kind\":\"move\",\"Tag\":\"" + Move + "\"}]}]}");
        AssertRule(legacy.Rules.Single(), false, false, Kinds, "Legacy JSON");
        var shared = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(EditorSession.CreateOffline(), "Legacy binding", legacy))).KeyEnhancement;
        AssertRule(shared.Rules.Single(), false, false, Kinds, "Default-field share");
        string golden = "PBZ2:gold:" + Convert.ToBase64String(new byte[] { 2, 8, 1, 18, 0, 42, 1, 1 }).TrimEnd('=');
        Check(UserPresetStore.ParseShare(golden).KeyEnhancement.Rules.Count == 0, "Existing share-format vector no longer decodes");
        Check(!shared.CompileLua().Contains("[\"restriction_bypasses\"]"), "Existing bindings must retain normal restrictions");
    }

    static void WorkspaceAndEditor(bool pad)
    {
        var session = EditorSession.CreateOffline();
        byte[] save = session.ExportCurrentScheme();
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var keyboard = new BindingWorkspace(session, config, config.Clone(), false, -1);
        var gamepad = new BindingWorkspace(session, config, config.Clone(), true, 0);
        var workspace = pad ? gamepad : keyboard;
        var opposite = pad ? keyboard : gamepad;
        string key = pad ? "Gamepad_FaceButton_Left" : "F9";
        string captured = pad ? "Gamepad_FaceButton_Top" : "F10";
        string[] selected = { "training", "stance" };
        var current = workspace.Add(Move, Specification(key, false, true, selected));
        AssertEntry(workspace.Entries(Move).Single(), false, true, selected, "Device entries");
        AssertEntry(workspace.GetSnapshot().Single(entry => entry.RuleId == current.RuleId), false, true, selected, "Device snapshot");
        Check(!opposite.Entries(Move).Any(entry => entry.RuleId == current.RuleId), "Restriction binding leaked into the other input device");
        var copy = workspace.CopyBinding(current, OtherMove);
        AssertEntry(copy, false, true, selected, "Copy");
        copy.RestrictionBypasses[0] = "equipment";
        AssertEntry(workspace.Entries(Move).Single(), false, true, selected, "Copy array isolation");
        using (var dialog = new UnifiedBindingEditDialog(workspace, Move, current, null, null))
        {
            dialog.Show(); Application.DoEvents();
            var master = Field<CheckBox>(dialog, "bypassRestrictions");
            var resources = Field<CheckBox>(dialog, "bypassResources");
            var choices = Field<CheckedListBox>(dialog, "restrictionBypasses");
            Check(master.Visible && master.Checked && !resources.Checked && choices.Visible && choices.Items.Count == Kinds.Length,
                "The editor did not restore the independent restriction controls");
            Check(choices.Height >= choices.ItemHeight * Kinds.Length
                && choices.Height <= choices.ItemHeight * (Kinds.Length + 1),
                "The restriction picker must fit its rows without excessive blank space: height="
                + choices.Height + ", itemHeight=" + choices.ItemHeight);
            for (int index = 0; index < Kinds.Length; index++)
            {
                Check(choices.GetItemChecked(index) == selected.Contains(Kinds[index]), "The editor lost " + Kinds[index]);
                Check(Convert.ToString(choices.Items[index]) == I18n.Str("binding.restrictions." + Kinds[index]), "Unresolved restriction label");
            }
            master.Checked = false; Application.DoEvents();
            Check(!choices.Visible && !resources.Checked, "Restriction master changed resource settings or retained visible suboptions");
            master.Checked = true; resources.Checked = true;
            for (int index = 0; index < Kinds.Length; index++) choices.SetItemChecked(index, index != 1);
            dialog.GetType().GetField("recording", Hidden).SetValue(dialog, true);
            Call(dialog, "Captured", captured);
            dialog.GetType().GetField("recording", Hidden).SetValue(dialog, false);
            Shot(dialog, pad ? "controller-restrictions" : "keyboard-restrictions");
            Save(dialog); current = workspace.Replace(current, dialog.Result);
        }
        string[] updated = Kinds.Where(kind => kind != "training").ToArray();
        AssertEntry(current, true, true, updated, "Capture and edit");
        Check(current.Keys.SequenceEqual(new[] { captured }), "Recording changed bypass settings without storing the recorded key");
        using (var dialog = new UnifiedBindingEditDialog(workspace, Move, current, null, null))
        {
            dialog.Show(); Application.DoEvents();
            Check(Field<CheckBox>(dialog, "bypassRestrictions").Checked && Field<CheckBox>(dialog, "bypassResources").Checked,
                "Reopening the editor lost one bypass master");
            var choices = Field<CheckedListBox>(dialog, "restrictionBypasses");
            for (int index = 0; index < Kinds.Length; index++)
                Check(choices.GetItemChecked(index) == updated.Contains(Kinds[index]), "Reopening lost a selected restriction kind");
            Shot(dialog, pad ? "controller-restrictions-saved" : "keyboard-restrictions-saved");
            Field<CheckBox>(dialog, "bypassResources").Checked = false;
            Save(dialog); current = workspace.Replace(current, dialog.Result);
        }
        AssertEntry(current, false, true, updated, "Disable resources independently");
        Check(!BindingWorkspace.IsPlainBinding(current), "Restriction-only tuning was treated as a plain binding");
        using (var dialog = new UnifiedBindingEditDialog(workspace, Move, current, null, null))
        {
            dialog.Show(); Application.DoEvents();
            Field<CheckBox>(dialog, "bypassResources").Checked = true;
            Field<CheckBox>(dialog, "bypassRestrictions").Checked = false;
            Save(dialog); current = workspace.Replace(current, dialog.Result);
        }
        AssertEntry(current, true, false, updated, "Disable restrictions independently");
        AssertRule(config.Rules.Single(rule => rule.Id == current.RuleId), true, false, updated, "Save disabled restriction master");
        var menu = workspace.Entries("IA_OpenMapMenu").First(entry => entry.IsNative && !entry.ReadOnly);
        using (var dialog = new UnifiedBindingEditDialog(workspace, "IA_OpenMapMenu", menu, null, null))
        {
            dialog.Show(); Application.DoEvents();
            Check(!Field<CheckBox>(dialog, "bypassRestrictions").Visible
                && !Field<CheckedListBox>(dialog, "restrictionBypasses").Visible,
                "Menu bindings must not display combat restriction bypass");
        }
        Check(session.ExportCurrentScheme().SequenceEqual(save), "Restriction editing modified the native input save");
    }

    static void Fixture(string name, string[] identities)
    {
        var config = new KeyEnhancementConfiguration { Enabled = true };
        foreach (bool pad in new[] { false, true })
        for (int index = 0; index < identities.Length; index++)
        {
            string identity = identities[index];
            var rule = new KeyEnhancementRule { Id = (pad ? "pad-" : "keyboard-") + index,
                Trigger = "down", DefaultTrigger = true, BypassResources = true, BypassRestrictions = true,
                RestrictionBypasses = Kinds.ToArray(), DamageMultiplier = pad ? 2.5 : 1.5,
                StartupSpeed = pad ? 1.75 : 1.25, RecoverySpeed = pad ? 3 : 2 };
            rule.Keys.Add(pad ? "Gamepad_FaceButton_Left" : "F9");
            rule.Outputs.Add(new KeyEnhancementOutput { Kind = identity.StartsWith("ability:") ? "ability" : "move",
                Tag = identity.StartsWith("ability:") ? identity.Substring(8) : identity,
                Device = pad ? "gamepad" : "keyboard" });
            config.Rules.Add(rule);
        }
        string lua = config.CompileLua();
        Check(Regex.Matches(lua, "\\[\"restriction_bypasses\"\\]").Count == identities.Length * 2,
            name + " must retain all keyboard/controller restriction policies");
        File.WriteAllText(Path.Combine(output, name + ".lua"), lua);
        File.WriteAllLines(Path.Combine(output, name + ".txt"), identities);
    }

    static void NativeFixtures()
    {
        Fixture("native-inputs", new[] {
            "move:Shadow.DuYanBow.GA_DuYan_Bow_Normal",
            "ability:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser",
            "move:Ability.Action.Hun.ShachiAbility.Default.DuYanBow.Shoot.Stage1",
            "move:Ability.Action.Hun.ShachiAbility.Default.DuYanBow.Shoot.Stage2",
            "move:Ability.Action.Hun.ShachiAbility.Default.DuYanBow.Shoot.Stage3",
            "move:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser.Shoot" });
        RestrictionDocument document;
        using (var stream = typeof(BindingWorkspace).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement.move-restrictions.json"))
        using (var reader = new StreamReader(stream))
            document = new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<RestrictionDocument>(reader.ReadToEnd());
        var holds = document.entries.Where(pair => pair.Value.preparation.ContainsKey("nativeHoldInputs"))
            .Select(pair => pair.Key).OrderBy(identity => identity, StringComparer.Ordinal).ToArray();
        Check(holds.Contains("move:Shadow.DuYanBow.GA_DuYan_Bow_Normal")
            && holds.Contains("ability:Ability.Action.Hun.ShachiAbility.DuYanBow.Laser"),
            "Native hold discovery omitted the bow preparation targets");
        Fixture("native-holds", holds);
        var variants = document.entries.Where(pair => pair.Value.preparation.ContainsKey("nativeHoldVariants"))
            .Select(pair => pair.Key).OrderBy(identity => identity, StringComparer.Ordinal).ToArray();
        Check(variants.Contains("ability:Ability.Action.Hun.CombatArt.Atk01"),
            "The shared combat-art entrance must prepare only the current weapon's native input");
        Fixture("native-hold-variants", variants);
        var phases = document.entries.Where(pair => pair.Value.preparation.ContainsKey("nativeActivation"))
            .Select(pair => pair.Key).OrderBy(identity => identity, StringComparer.Ordinal).ToArray();
        Check(phases.Contains("move:WeaponPhase.GA_Hun_Jigsaw_C.HasSawHitStage2")
            && phases.Contains("move:WeaponPhase.GA_Hun_Jigsaw_C.HasSawHitFinal")
            && phases.Contains("move:WeaponPhase.GA_Hun_ZhanMa_C.NormalFinal"),
            "Independent phases omitted their ordinary-activation preparation");
        Fixture("independent-phases", phases.Concat(new[] {
            "move:WeaponPhase.GA_Hun_ZhanMa_C.GrabFinal",
            "move:WeaponPhase.GA_Hun_SlitheringSword_C.PerfectFinal" }).ToArray());
    }

    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? "dist/binding-restrictions"); Directory.CreateDirectory(output);
            NativeFixtures();
            if (args.Contains("--fixtures-only"))
            { Console.WriteLine("BindingRestrictionTests fixtures PASS " + checks); return 0; }
            Defaults(); Persistence(); WorkspaceAndEditor(false); WorkspaceAndEditor(true);
            Console.WriteLine("BindingRestrictionTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
