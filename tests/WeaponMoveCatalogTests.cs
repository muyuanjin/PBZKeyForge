using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using PBZKeyForge;

class WeaponMoveCatalogTests
{
    public sealed class Move
    {
        public string id, abilityClass, inputAction, category, prepareInput, entryPoint;
        public bool activateByClass, nativeEntry;
    }
    public sealed class Document { public Move[] moves; }
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }

    static Move[] Read(string filename)
    {
        using (var stream = typeof(BindingWorkspace).Assembly.GetManifestResourceStream("PBZKeyForge.KeyEnhancement." + filename))
        using (var reader = new StreamReader(stream))
            return new JavaScriptSerializer { MaxJsonLength = 16777216 }.Deserialize<Document>(reader.ReadToEnd()).moves;
    }

    static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        I18n.Current = "zh-Hans";
        var weapons = Read("weapon-moves.json");
        var shadows = Read("shadow-moves.json");
        var moves = weapons.Concat(shadows).ToArray();
        Check(weapons.Length > 100, "Weapon moves must cover the weapon inventory beyond a single weapon.");
        Check(weapons.Select(move => move.category.Split('/')[1]).Distinct().Count() >= 20,
            "The weapon inventory must retain its equipment groups.");
        foreach (string ability in new[] { "GA_CaoOuSiXian", "GA_JianKeZhaoJiLing", "GA_KuYeSiZhuChuan", "GA_XuKongHuaShan" })
            Check(shadows.Any(move => move.abilityClass.Contains(ability)), "A shared-default Phantom Edge has no equipment-specific entry: " + ability);
        var chiRen = moves.Where(move => move.abilityClass.Contains("/LongBlade/ChiRen/")).ToArray();
        foreach (string attack in new[] { "A", "AA", "AAA", "AAAA", "AB", "ABB", "AAB", "AABB", "AAAB", "AAABB", "AAAAB", "AAAABB", "B", "BB", "BBB", "BD", "BSup" })
            Check(chiRen.Any(move => move.abilityClass.EndsWith("/GA_Hun_ChiRen_" + attack + ".GA_Hun_ChiRen_" + attack + "_C", StringComparison.Ordinal)),
                "A native ChiRen attack has no weapon-specific entry: " + attack);
        var protectedMoves = Read("complete-moves.json");
        foreach (int stage in new[] { 1, 2, 3 })
            Check(protectedMoves.Any(move => move.id == "move:Ability.Action.Hun.CombatArt.ChiRen.Release" + stage
                && move.abilityClass.EndsWith("/GA_Hun_ChiRen_C.GA_Hun_ChiRen_C_C", StringComparison.Ordinal)
                && move.prepareInput == "/PBZInput/InputActions/IA_CombatArt_Hold.IA_CombatArt_Hold"
                && move.entryPoint == "ToRelease"), "ChiRen combat art must retain its protected charge recipe: " + stage);
        var session = EditorSession.CreateOffline();
        var original = session.ExportCurrentScheme();
        foreach (bool pad in new[] { false, true })
        {
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(session, config, config.Clone(), pad, pad ? 0 : -1);
            var functions = workspace.Functions.ToDictionary(function => function.Id);
            var search = new HashSet<string>(workspace.FindFunctions("炽刃").Select(function => function.Id));
            foreach (var move in moves)
            {
                BindingFunction function;
                Check(functions.TryGetValue(move.id, out function), "An embedded weapon move is missing from the picker: " + move.id);
                Check(move.activateByClass && move.nativeEntry && !string.IsNullOrEmpty(move.abilityClass) && !string.IsNullOrEmpty(move.inputAction),
                    "A weapon move must retain its precise granted class and native input: " + move.id);
                Check(function.GroupPath.SequenceEqual(move.category.Split('/')) && function.GroupLabels.All(label => !string.IsNullOrWhiteSpace(label)),
                    "A weapon move lost its equipment classification: " + move.id);
                Check(function.Label.Contains(" · ") && !function.Label.Contains("catalog.") && !function.Label.Contains("moves."),
                    "A weapon move has an unresolved name: " + move.id);
                if (chiRen.Contains(move))
                    Check(function.Label.StartsWith("炽刃 · ") && search.Contains(move.id), "ChiRen search must include every precise attack: " + move.id);
                workspace.Add(move.id, new BindingEntry { Keys = new[] { pad ? "Gamepad_DPad_Down" : "G" } });
            }
            var shared = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session, "武器招式", config))).KeyEnhancement;
            Check(shared.Rules.Count == config.Rules.Count && shared.Rules.All(rule => rule.DefaultTrigger),
                "Weapon-specific move identities and default triggers must survive sharing.");
            string lua = shared.CompileLua();
            foreach (var move in moves)
                Check(lua.Contains("[\"ability_class\"]=\"" + move.abilityClass + "\""), "Compilation lost a weapon's class: " + move.id);
            Check(!lua.Contains("[\"force\"]=true"), "Weapon entries must retain normal game eligibility.");
            File.WriteAllText(Path.Combine(directory, pad ? "weapon-pad.lua" : "weapon-keyboard.lua"), lua);
            foreach (var rule in config.Rules)
            {
                rule.BypassResources = true;
                rule.DamageMultiplier = 2.5;
                rule.StartupSpeed = 1.5;
                rule.RecoverySpeed = .75;
            }
            var tuned = UserPresetStore.ParseShare(UserPresetStore.Share(UserPresetStore.Capture(session, "招式倍率", config))).KeyEnhancement;
            Check(tuned.Rules.Count == config.Rules.Count && tuned.Rules.All(rule => rule.BypassResources
                && rule.DamageMultiplier == 2.5 && rule.StartupSpeed == 1.5 && rule.RecoverySpeed == .75),
                "Every weapon move must preserve resource bypass, damage and both phase speeds through sharing.");
            var tunedWorkspace = new BindingWorkspace(session, tuned, tuned.Clone(), pad, pad ? 0 : -1);
            foreach (var move in moves)
                Check(tunedWorkspace.Entries(move.id).Any(entry => entry.BypassResources && entry.DamageMultiplier == 2.5
                    && entry.StartupSpeed == 1.5 && entry.RecoverySpeed == .75), "Binding details lost a move's effects: " + move.id);
            File.WriteAllText(Path.Combine(directory, pad ? "weapon-pad-tuned.lua" : "weapon-keyboard-tuned.lua"), tuned.CompileLua());
        }
        Check(session.ExportCurrentScheme().SequenceEqual(original), "Weapon move bindings must not change the input save.");
        var previewConfig = new KeyEnhancementConfiguration { Enabled = true };
        var preview = new BindingWorkspace(session, previewConfig, previewConfig.Clone(), false, -1);
        foreach (var move in weapons.Where(move => move.id.Contains("Weapon_Hun_Temp_") && move.category.EndsWith("/IA_HeavyAttack", StringComparison.Ordinal)))
            Check(preview.ResourceKinds(move.id).Contains("shachi"), "A temporary weapon heavy attack lost its inherited resource bypass: " + move.id);
        var heavy = chiRen.Single(move => move.abilityClass.EndsWith("/GA_Hun_ChiRen_B.GA_Hun_ChiRen_B_C", StringComparison.Ordinal));
        Check(preview.ResourceKinds(heavy.id).Contains("shachi"), "ChiRen heavy attacks must expose resource bypass.");
        using (var dialog = new UnifiedBindingEditDialog(preview, heavy.id, null, "G", null))
        {
            dialog.Show(); Application.DoEvents();
            Check(!dialog.Text.Contains(I18n.Str("moves.kind")), "A native weapon entrance must not promise automatic charge release.");
            Check(Field<CheckBox>(dialog, "bypassResources").Visible, "A weapon-specific heavy attack must expose resource bypass.");
            foreach (string field in new[] { "damageMultiplier", "startupSpeed", "recoverySpeed" })
                Check(Field<NumericUpDown>(dialog, field).Enabled, "A weapon-specific move disabled its tuning control: " + field);
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
            {
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(directory, "chiren-binding-effects.png"));
            }
        }
        using (var form = new Form { ClientSize = new Size(1440, 860), Font = new Font("Microsoft YaHei UI", 10f) })
        using (var view = new UnifiedActionView())
        {
            form.Controls.Add(view); form.Show(); view.Render(preview); Application.DoEvents();
            Field<TextBox>(view, "search").Text = "炽刃"; Application.DoEvents();
            Check(Field<DataGridView>(view, "actions").RowCount >= chiRen.Length, "The visible picker hides ChiRen attacks.");
            using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(directory, "chiren-actions.png"));
            }
        }
        Console.WriteLine("WeaponMoveCatalogTests PASS " + checks + "; weapon moves=" + moves.Length + "; ChiRen=" + chiRen.Length);
    }

    [STAThread]
    static int Main(string[] args)
    {
        try { Application.EnableVisualStyles(); Run(args[0]); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
