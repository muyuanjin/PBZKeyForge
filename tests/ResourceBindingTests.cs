using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class ResourceBindingTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string Move = "move:Ability.Action.Hun.Heavy.ComboAtk_N3H3";
    static int checks;
    static string output;
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static BindingEntry Key(string key, bool bypass)
    { return new BindingEntry { Keys = new[] { key }, Trigger = "default", BypassResources = bypass }; }
    static void Shot(Form form, string name)
    {
        using (var bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, name + ".png")); }
    }
    static void MenuBindings()
    {
        foreach (bool enabled in new[] { false, true })
        foreach (bool pad in new[] { false, true })
        {
            var session = EditorSession.CreateOffline();
            var config = new KeyEnhancementConfiguration { Enabled = enabled };
            var workspace = new BindingWorkspace(session, config, config.Clone(), pad, pad ? 0 : -1);
            foreach (var function in workspace.Functions.Where(item => item.NativeAction != null))
                Check(workspace.ResourceKinds(function.Id) != null, "Native resource query failed: " + function.Id);
            foreach (string id in new[] { "IA_OpenMapMenu", "IA_OpenCharacterMenu" })
            {
                Check(workspace.ResourceKinds(id).Length == 0, "Menu inputs must not expose combat resource costs");
                var current = workspace.Entries(id).First(entry => entry.IsNative && !entry.ReadOnly);
                string replacement = pad ? "Gamepad_DPad_Down" : "F24";
                using (var dialog = new UnifiedBindingEditDialog(workspace, id, current, null, null))
                {
                    dialog.Show(); Application.DoEvents();
                    Check(!Field<CheckBox>(dialog, "bypassResources").Visible, "A menu binding exposes resource bypass");
                    Call(dialog, "SetKey", replacement); Call(dialog, "Save");
                    Check(dialog.Result != null && dialog.Result.Trigger == "default", "Ordinary menu edit failed");
                    var edited = workspace.Replace(current, dialog.Result);
                    Check(edited.IsNative && edited.Keys.Single() == replacement, "Menu edit did not preserve native storage");
                }
                Check(config.Rules.Count == 0 && config.Enabled == enabled, "Menu editing changed enhancement rules or mode");
            }
        }
    }
    static void TagResources()
    {
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
        Check(workspace.ResourceKinds(Move).SequenceEqual(new[] { "shachi" }), "The tag-only N3H3 move must expose its shachi cost");
        foreach (string tag in new[] { "Ability.Action.Hun.Light.SlitheringSword.DA", "Ability.Action.Hun.Light.SnakeDual.DA",
            "Ability.Action.Hun.HeiMoShang.Reborn", "Ability.Action.Hun.XuKongHuaShan.Reborn" })
            Check(ResourceCostCatalog.ForOutput(new KeyEnhancementOutput { Kind = "ability", Tag = tag }).Contains("shachi"),
                "Independent resource costs must expose bypass: " + tag);
        foreach (string id in new[] { "ability:Ability.Action.Hun.Heavy.ShuangYue.DB",
            "move:WeaponSpecial.GA_Hun_ShuangYue_DB_SWT", "move:WeaponSwitch.ShuangYue" })
        {
            Check(workspace.ResourceKinds(id).Contains("weapon_charge"), "Dual-axe entry must expose its independent energy: " + id);
            using (var dialog = new UnifiedBindingEditDialog(workspace, id, null, "G", null))
            {
                dialog.Show(); Application.DoEvents();
                var bypass = Field<CheckBox>(dialog, "bypassResources");
                Check(bypass.Visible && bypass.Text.Contains(I18n.Str("binding.resources.weapon_charge")),
                    "Direct, switch-leaf and wrapper bindings must expose weapon-energy bypass");
                if (id == "ability:Ability.Action.Hun.Heavy.ShuangYue.DB") Shot(dialog, "dual-axe-resource-option");
            }
        }
        using (var dialog = new UnifiedBindingEditDialog(workspace, Move, null, "G", null))
        {
            dialog.Show(); Application.DoEvents();
            var bypass = Field<CheckBox>(dialog, "bypassResources");
            Check(bypass.Visible && !bypass.Checked && bypass.Text.Contains(ResourceCostCatalog.Describe(new[] { "shachi" })),
                "The tag-only move must show the resource option with its actual cost");
            bypass.Checked = true; Shot(dialog, "tag-resource-option"); Call(dialog, "Save");
            workspace.Add(Move, dialog.Result);
        }
        Check(config.Rules.Single().BypassResources && config.CompileLua().Contains("bypass_resources"), "Tag-only move lost its saved bypass");
        workspace.Add(Move, Key("H", false));
        File.WriteAllText(Path.Combine(output, "merged-resource-move.lua"), config.CompileLua());
    }
    static void UndoResources(bool original)
    {
        using (var host = new MainForm(new EditorPreferences(Path.Combine(output, "undo-" + original + ".txt"))))
        {
            host.UiOffline(); host.Show(); Application.DoEvents();
            var config = Field<KeyEnhancementConfiguration>(host, "keyEnhancement"); config.Enabled = true;
            var workspace = (BindingWorkspace)Call(host, "BindingDialogWorkspace", false, -1);
            var current = workspace.Add(Move, Key("G", original));
            var changed = (BindingEntry)Call(host, "StageBindingFromDialog", workspace, Move, current, Key("G", !original));
            Check(changed != null && changed.BypassResources != original, "Fixture did not stage the resource edit");
            using (var dialog = new UnifiedBindingEditDialog(workspace, Move, changed, null, null))
            {
                dialog.Show(host); Application.DoEvents();
                typeof(UnifiedBindingEditDialog).GetField("directHost", Hidden).SetValue(dialog, host);
                Field<CheckBox>(dialog, "bypassResources").Focus();
                object[] keys = { Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Z };
                Check((bool)typeof(UnifiedBindingEditDialog).GetMethod("ProcessCmdKey", Hidden).Invoke(dialog, keys), "Ctrl+Z did not handle the live edit");
                var restored = Field<BindingEntry>(dialog, "current");
                Check(restored.BypassResources == original && Field<CheckBox>(dialog, "bypassResources").Checked == original,
                    "Undo must restore the model and resource checkbox together");
                typeof(UnifiedBindingEditDialog).GetField("directHost", Hidden).SetValue(dialog, null);
                Field<NumericUpDown>(dialog, "seconds").Value = .8m; Call(dialog, "Save");
                workspace = Field<BindingWorkspace>(dialog, "workspace"); workspace.Replace(restored, dialog.Result);
                Check(workspace.Entries(Move).Single().BypassResources == original && Math.Abs(workspace.Entries(Move).Single().Seconds - .8) < .001,
                    "Saving another change restored the undone resource setting");
            }
        }
    }
    static void Tuning()
    {
        const string finisher = "move:Ability.Action.Hun.CombatArt.BronzeSword.Final";
        var session = EditorSession.CreateOffline();
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
        Check(workspace.ResourceKinds(finisher).SequenceEqual(new[] { "ultimate" }), "Bronze finisher uses pure shachi, rather than the source heavy attack's resource");
        var entry = workspace.Add(finisher, Key("G", true));
        using (var dialog = new UnifiedBindingEditDialog(workspace, finisher, entry, null, null))
        {
            dialog.Show(); Application.DoEvents();
            Check(Field<NumericUpDown>(dialog,"damageMultiplier").Value == 1, "Damage tuning must default to the game's damage");
            Field<NumericUpDown>(dialog,"damageMultiplier").Value = 2.5m;
            Field<NumericUpDown>(dialog,"startupSpeed").Value = 1.75m;
            Field<NumericUpDown>(dialog,"recoverySpeed").Value = 3m;
            Shot(dialog,"finisher-tuning"); Call(dialog,"Save");
            entry = workspace.Replace(entry,dialog.Result);
        }
        Check(entry.BypassResources && entry.DamageMultiplier == 2.5 && entry.StartupSpeed == 1.75 && entry.RecoverySpeed == 3, "Editing must retain independent resource and tuning settings");
        var copied = workspace.CopyBinding(entry,finisher);
        Check(copied.DamageMultiplier == 2.5 && copied.StartupSpeed == 1.75 && copied.RecoverySpeed == 3 && copied.BypassResources, "Copying lost tuning settings");
        var restored = KeyEnhancementConfiguration.FromJson(config.ToJson());
        var preset = UserPresetStore.Capture(session,"Resource tuning",restored);
        var imported = UserPresetStore.ParseShare(UserPresetStore.Share(preset)).KeyEnhancement;
        Check(imported.Rules.All(rule => rule.BypassResources && rule.DamageMultiplier == 2.5 && rule.StartupSpeed == 1.75 && rule.RecoverySpeed == 3), "Sharing lost resource or tuning settings");
        string lua = imported.CompileLua();
        Check(lua.Contains("damage_multiplier") && lua.Contains("startup_speed") && lua.Contains("recovery_speed") && lua.Contains("bypass_resources"), "Compiled configuration omitted tuning settings");
        File.WriteAllText(Path.Combine(output,"compiled-tuning.lua"),lua);
        string bundle = Path.Combine(output,"tuning.zip");
        new BindingDocument {Session=session,Configuration=restored}.Export(bundle);
        var unzipped = BindingDocument.Open(bundle).Configuration;
        Check(unzipped.Rules.All(rule => rule.BypassResources && rule.DamageMultiplier == 2.5 && rule.StartupSpeed == 1.75 && rule.RecoverySpeed == 3), "ZIP export lost resource or tuning settings");
        var thunder = new KeyEnhancementRule(); thunder.Keys.Add("H"); thunder.Outputs.Add(new KeyEnhancementOutput {Kind="ability",Tag="Ability.Action.Hun.ShachiAbility.ThunderSpear.Common_04"});
        restored.Rules.Add(thunder);
        Check(restored.CompileLua().Contains("/PBZInput/InputActions/IA_ShachiAbility.IA_ShachiAbility"), "Direct thunder activation must carry the actual shadow armament input action");
        thunder.DamageMultiplier = double.NaN;
        try { restored.CompileLua(); throw new Exception("Invalid damage multiplier accepted"); }
        catch (InvalidDataException) { checks++; }
        foreach (string language in I18n.Languages)
        {
            I18n.Current = language;
            foreach (string key in new[] {"binding.tuning.damage","binding.tuning.startup","binding.tuning.recovery"})
                Check(I18n.Str(key) != key,"Missing tuning translation: " + language + ":" + key);
        }
        I18n.Current = "zh-Hans";
    }
    static void ActiveTuning()
    {
        const string ghost = "move:Accessory.RandomDrink.GhostFire.Launch";
        const string shield = "move:Ability.Action.Hun.ShachiAbility.Default.ZhuoBiDun.Counter";
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
        foreach (string id in new[] {ghost, shield})
        using (var dialog = new UnifiedBindingEditDialog(workspace, id, null, id == ghost ? "G" : "H", null))
        {
            dialog.Show(); Application.DoEvents();
            Check(Field<NumericUpDown>(dialog,"damageMultiplier").Visible, "Active moves must expose damage tuning");
            Check(Field<NumericUpDown>(dialog,"startupSpeed").Visible == (id == shield)
                && Field<NumericUpDown>(dialog,"recoverySpeed").Visible == (id == shield),
                "Only a timeline release can expose phase speed controls");
            Field<NumericUpDown>(dialog,"damageMultiplier").Value = 3;
            if (id == shield)
            {
                Field<NumericUpDown>(dialog,"startupSpeed").Value = 2;
                Field<NumericUpDown>(dialog,"recoverySpeed").Value = 4;
            }
            else Shot(dialog,"ghost-fire-tuning");
            Call(dialog,"Save"); workspace.Add(id,dialog.Result);
        }
        File.WriteAllText(Path.Combine(output,"compiled-active-tuning.lua"),config.CompileLua());
        var ghostRule = config.Rules.Single(rule => rule.Outputs.Single().Tag == ghost);
        ghostRule.StartupSpeed = 2;
        try { config.CompileLua(); throw new Exception("Callback-only speed settings accepted"); }
        catch (InvalidDataException) { checks++; }
        ghostRule.StartupSpeed = 1; ghostRule.RecoverySpeed = 2;
        try { config.CompileLua(); throw new Exception("Callback-only recovery settings accepted"); }
        catch (InvalidDataException) { checks++; }
    }
    static void AttachedEffects()
    {
        var session = EditorSession.CreateOffline();
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
        Check(!new KeyEnhancementRule().SuperArmor && !new KeyEnhancementRule().Invincible,
            "Attached effects must be opt-in for existing configurations");
        using (var dialog = new UnifiedBindingEditDialog(workspace, Move, null, "G", null))
        {
            dialog.Show(); Application.DoEvents();
            var armor = Field<CheckBox>(dialog,"superArmor"); var invincible = Field<CheckBox>(dialog,"invincible");
            Check(armor.Visible && invincible.Visible && !armor.Checked && !invincible.Checked,
                "New move bindings must expose two independent unchecked effect options");
            Check(armor.PointToScreen(Point.Empty).Y < invincible.PointToScreen(Point.Empty).Y
                && invincible.PointToScreen(Point.Empty).Y < Field<NumericUpDown>(dialog,"startupSpeed").PointToScreen(Point.Empty).Y,
                "Attached effects must be immediately above the phase speed settings");
            armor.Checked = invincible.Checked = true;
            Shot(dialog,"attached-effects"); Call(dialog,"Save"); workspace.Add(Move,dialog.Result);
        }
        var entry = workspace.Entries(Move).Single();
        Check(entry.SuperArmor && entry.Invincible && !BindingWorkspace.IsPlainBinding(entry),
            "Effect-only tuning must retain enhancement ownership");
        using (var dialog = new UnifiedBindingEditDialog(workspace, Move, entry, null, null))
        {
            dialog.Show(); Application.DoEvents();
            Check(Field<CheckBox>(dialog,"superArmor").Checked && Field<CheckBox>(dialog,"invincible").Checked,
                "Editing an existing move lost its effect settings");
            Field<CheckBox>(dialog,"superArmor").Checked = false; Call(dialog,"Save");
            entry = workspace.Replace(entry,dialog.Result);
        }
        Check(!entry.SuperArmor && entry.Invincible,"Effect checkboxes must be independent");
        var copy = workspace.CopyBinding(entry,Move);
        Check(!copy.SuperArmor && copy.Invincible,"Copy lost independent effect settings");
        var restored = KeyEnhancementConfiguration.FromJson(config.ToJson());
        var shared = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session,"Attached effects",restored))).KeyEnhancement;
        Check(shared.Rules.Count == 2 && shared.Rules.All(rule => !rule.SuperArmor && rule.Invincible),
            "Share roundtrip lost effect-only tuning");
        string bundle = Path.Combine(output,"effects.zip");
        new BindingDocument {Session=session,Configuration=shared}.Export(bundle);
        var unzipped = BindingDocument.Open(bundle).Configuration;
        Check(unzipped.Rules.All(rule => !rule.SuperArmor && rule.Invincible),"ZIP roundtrip lost effect settings");
        string lua = unzipped.CompileLua();
        Check(lua.Contains("invincible") && !lua.Contains("super_armor") && lua.Contains("resource_inputs"),
            "Effect-only bindings must compile their enabled policy and activation inputs");
        restored.Rules[0].SuperArmor = true;
        Check(restored.CompileLua().Contains("super_armor"),"Super armor was omitted from compiled configuration");
        var menu = workspace.Entries("IA_OpenMapMenu").First(item => item.IsNative && !item.ReadOnly);
        using (var dialog = new UnifiedBindingEditDialog(workspace,"IA_OpenMapMenu",menu,null,null))
        {
            dialog.Show(); Application.DoEvents();
            Check(!Field<CheckBox>(dialog,"superArmor").Visible && !Field<CheckBox>(dialog,"invincible").Visible,
                "Menu bindings must not expose move-only attached effects");
        }
        foreach (string language in I18n.Languages)
        {
            I18n.Current = language;
            foreach (string key in new[] {"binding.effects.superArmor","binding.effects.invincible","binding.effects.help"})
                Check(I18n.Str(key) != key,"Missing effect translation: " + language + ":" + key);
        }
        I18n.Current = "zh-Hans";
    }
    static void TuningChains()
    {
        var config = new KeyEnhancementConfiguration { Enabled = true };
        foreach (string id in new[] {
            "move:Ability.Action.Hun.Heavy.GuiTouDao.BHold.Release",
            "move:Ability.Action.Hun.Heavy.GuiTouDao.BHold.EarlyRelease",
            "move:WeaponSwitch.ChaosBlade", "move:WeaponSwitch.DualHook", "move:WeaponSwitch.ShuangYue" })
        {
            var rule = new KeyEnhancementRule { BypassResources = true, DamageMultiplier = 3, StartupSpeed = 2, RecoverySpeed = 4, SuperArmor = true, Invincible = true };
            rule.Keys.Add("G"); rule.Outputs.Add(new KeyEnhancementOutput { Kind = "move", Tag = id });
            config.Rules.Add(rule);
        }
        File.WriteAllText(Path.Combine(output,"compiled-tuning-chains.lua"),config.CompileLua());
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            output = Path.GetFullPath(args.FirstOrDefault() ?? "dist/resource-binding-check"); Directory.CreateDirectory(output);
            MenuBindings(); TagResources(); UndoResources(false); UndoResources(true); Tuning(); ActiveTuning(); AttachedEffects(); TuningChains();
            Console.WriteLine("ResourceBindingTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
