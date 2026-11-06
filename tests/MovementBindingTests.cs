using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PBZKeyForge;

class MovementBindingTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static BindingEntry Key(string key) { return new BindingEntry { Keys = new[] { key } }; }
    static double Component(object value, string component) { return Convert.ToDouble(((Dictionary<string, object>)value)[component]); }
    static void Main(string[] args)
    {
        I18n.Current = "zh-Hans";
        var session = EditorSession.CreateOffline();
        byte[] saved = session.ExportCurrentScheme();
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(session, config, config.Clone(), false, -1);
        string[] directions = { "forward", "backward", "left", "right" };
        string[] originals = { "W", "S", "A", "D" };
        string[] replacements = { "I", "K", "J", "L" };
        double[] x = { 1, -1, 0, 0 }, y = { 0, 0, -1, 1 };
        Check(workspace.Functions.Where(item => string.Join("/", item.GroupPath) == "locomotion/movement").Take(4).Select(item => item.Id).SequenceEqual(directions.Select(direction => "movement:" + direction)),
            "Movement category must start with directional movement.");
        config.Enabled = false;
        Check(workspace.Functions.Take(21).All(item => !item.RequiresKeyEnhancement), "Ordinary mode must start with its 21 usable save controls.");
        config.Enabled = true;
        Check(workspace.Functions.Where(item => string.Join("/", item.GroupPath) == "locomotion/movement").Take(4).Select(item => item.Id).SequenceEqual(directions.Select(direction => "movement:" + direction)),
            "Re-enabling key enhancement changed movement ordering.");
        Check(workspace.Add("movement:forward", Key("W")).IsNative && config.Rules.Count == 0, "Adding an existing movement relation duplicated its output.");
        for (int i = 0; i < directions.Length; i++)
        {
            string id = "movement:" + directions[i];
            var function = workspace.Functions.Single(item => item.Id == id);
            Check(function.SearchText.Contains("WASD") && function.GroupPath[0] == "locomotion", "Movement is not discoverable in its player category.");
            var original = workspace.Entries(id).Single(item => item.Keys.Contains(originals[i]));
            Check(original.IsNative && original.ReadOnly && original.Active, "Fixed movement source missing.");
            var entry = workspace.Replace(original, Key(replacements[i]));
            Check(!entry.IsNative && entry.Trigger == "down" && Component(entry.OutputValue, "x") == x[i] && Component(entry.OutputValue, "y") == y[i],
                "Rebinding movement lost its native direction or continuous trigger.");
            Check(workspace.Entries(id).Single(item => item.Id == original.Id).Suppressed
                && workspace.Entries(id).Any(item => item.IsNative && item.Active), "Replacement must suppress only the selected key and preserve the arrow key.");
        }
        var loaded = KeyEnhancementConfiguration.FromJson(config.ToJson());
        workspace = new BindingWorkspace(session, loaded, config.Clone(), false, -1);
        Check(directions.All(direction => workspace.Entries("movement:" + direction).Any(item => !item.IsNative)), "Direction identities were lost after JSON reload.");
        var left = workspace.Entries("movement:left").Single(item => !item.IsNative);
        left = workspace.Replace(left, Key("H"));
        Check(Component(left.OutputValue, "x") == 0 && Component(left.OutputValue, "y") == -1, "Editing a directional binding reset it to forward.");
        workspace.Restore("movement:forward");
        Check(workspace.Entries("movement:forward").All(item => item.IsNative && item.Active), "Restoring forward did not restore its fixed keys.");
        Check(workspace.Entries("movement:backward").Single(item => item.Keys.Contains("S")).Suppressed
            && workspace.Entries("movement:left").Single(item => item.Keys.Contains("A")).Suppressed, "Restoring one direction re-enabled other directions' suppressed keys.");
        workspace.Add("movement:forward", Key("I"));
        Check(workspace.Entries("movement:forward").Where(item => item.IsNative).All(item => item.Active), "Adding a movement key removed the original keys.");
        loaded.Enabled = false;
        Check(workspace.Entries("movement:left").Where(item => item.IsNative).All(item => item.Active && item.ExecutionOwner == "game")
            && workspace.Entries("movement:left").Where(item => !item.IsNative).All(item => !item.Active), "Disabling key enhancement did not restore native movement.");
        loaded.Enabled = true;
        string sheath = "action:/SGameCombatSystem/Weapon/Input/IA_ChangeArmed";
        var sheathFunction = workspace.Functions.Single(item => item.Id == sheath);
        Check(sheathFunction.Label == "收起武器" && sheathFunction.LabelSource == "semantic" && new[] { "收刀", "入鞘", "sheathe" }.All(sheathFunction.SearchText.Contains)
            && string.Join("/", sheathFunction.GroupPath) == "weapons/equipment", "Sheathing lacks its own name, aliases or weapon entry.");
        Check(workspace.Functions.Single(item => item.Id == "ability:Ability.UpperBody.Hun.ChangeArmed").Label == "收/拔武器",
            "The input action's sheathe-only name replaced the separate ability name.");
        Check(workspace.Add(sheath, Key("F9")).Trigger == "press", "A standalone sheathe key must trigger once per press.");
        Check(session.ExportCurrentScheme().SequenceEqual(saved), "Key enhancement movement and sheathing edited the save file.");
        string output = args.Length > 0 ? args[0] : "dist/movement-bindings.lua";
        File.WriteAllText(output, loaded.CompileLua());
        Console.WriteLine("MovementBindingTests PASS " + checks);
    }
}
