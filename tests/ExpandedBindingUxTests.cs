using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class ExpandedBindingUxTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static string output;
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static BindingEntry Spec(string key, string trigger) { return new BindingEntry { Keys = new[] { key }, Trigger = trigger }; }
    static void Save(UnifiedBindingEditDialog dialog) { Call(dialog, "Save"); Check(dialog.Result != null, "Editor did not save the binding"); }
    static void Choose(ComboBox combo, string key)
    {
        for (int i = 0; i < combo.Items.Count; i++)
            if (Field<string>(combo.Items[i], "Key") == key) { combo.SelectedIndex = i; return; }
        throw new Exception("Missing key " + key);
    }
    static void Watch(CheckedListBox list, string key)
    { for (int i = 0; i < list.Items.Count; i++) if (Field<string>(list.Items[i], "Key") == key) { list.SetItemChecked(i, true); Application.DoEvents(); return; } throw new Exception(key); }
    static BindingWorkspace Workspace(out EditorSession session, out KeyEnhancementConfiguration config)
    { session = EditorSession.CreateOffline(); config = new KeyEnhancementConfiguration { Enabled = true }; return new BindingWorkspace(session, config, config.Clone(), false, -1); }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            output = args.Length == 0 ? "dist/expanded-binding-ux-check" : args[0]; Directory.CreateDirectory(output);
            RestoreScope(); CrossDevice(); CustomLifecycle(); TriggerChanges(); Consumption(); NativeDisabled(); Duplicate(); RestoreAndPresets(); FailedPlans();
            Console.WriteLine("ExpandedBindingUxTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void RestoreScope()
    {
        EditorSession s; KeyEnhancementConfiguration c; var keyboard = Workspace(out s, out c);
        var pad = new BindingWorkspace(s, c, c.Clone(), true, s.ControllerFamilies.First().Index);
        keyboard.Add("IA_Jump", Spec("J", "hold")); pad.Add("IA_Jump", Spec("Gamepad_LeftShoulder", "hold"));
        string before = c.Rules[0].Id;
        pad.Restore("IA_Jump");
        Check(c.Rules.Count == 1 && c.Rules[0].Id == before && keyboard.Entries("IA_Jump").Any(entry => !entry.IsNative), "Restoring a pad function removed keyboard input");
        pad.Add("IA_Jump", Spec("Gamepad_LeftShoulder", "hold")); keyboard.Restore("IA_Jump");
        Check(c.Rules.Count == 1 && c.Rules[0].Keys[0] == "Gamepad_LeftShoulder", "Restoring a keyboard function removed pad input");
    }
    static void CrossDevice()
    {
        EditorSession s; KeyEnhancementConfiguration c; var w = Workspace(out s, out c);
        var current = w.Entries("IA_Jump").First(entry => entry.IsNative);
        byte[] bytes = s.ExportCurrentScheme();
        var result = w.Replace(current, Spec("Gamepad_FaceButton_Left", "default"));
        Check(!result.IsNative && s.ExportCurrentScheme().SequenceEqual(bytes), "Cross-device replacement wrote a keyboard save slot");
        var pad = new BindingWorkspace(s, c, c.Clone(), true, s.ControllerFamilies.First().Index);
        Check(pad.Entries("IA_Jump").Any(entry => entry.RuleId == result.RuleId), "Cross-device replacement was absent in its input device view");
        var added = pad.Add("IA_Jump", Spec("K", "default"));
        Check(!added.IsNative && w.Entries("IA_Jump").Any(entry => entry.RuleId == added.RuleId), "Cross-device addition used a native pad slot");
        c.Enabled = false; bytes = s.ExportCurrentScheme();
        var inactive = w.Add("IA_Jump", Spec("Gamepad_FaceButton_Top", "default"));
        Check(!inactive.IsNative && !inactive.Active && !c.Enabled && s.ExportCurrentScheme().SequenceEqual(bytes), "Inactive cross-device editing changed a native save or enabled execution");
    }
    static void CustomLifecycle()
    {
        EditorSession s; KeyEnhancementConfiguration c; var w = Workspace(out s, out c);
        var entry = w.AddCustom(new KeyEnhancementOutput { Kind="property", Property="bFlag", Value=true }, Spec("F9", "press"), "状态");
        string id = entry.FunctionId; w.Remove(entry);
        c = KeyEnhancementConfiguration.FromJson(c.ToJson()); w = new BindingWorkspace(s,c,c.Clone(),false,-1);
        Check(w.Functions.Any(f => f.Id == id) && w.Entries(id).Length == 0, "Unbinding discarded the custom function definition");
        var target = w.CustomOutput(id); target.Value = false; w.UpdateCustomOutput(id,target,"新状态");
        entry = w.Add(id, Spec("F10","press"));
        Check(!(bool)entry.Rule.Outputs[0].Value && w.Functions.Single(f=>f.Id==id).Label=="新状态", "Rebinding lost the updated unbound definition");
        w.Remove(entry);
        using(var dialog=new UnifiedActionBindingsDialog(w,id,null,null))
        {
            dialog.Show(); Application.DoEvents();
            Check(Field<Button>(dialog,"editCustomFunction").Enabled, "Unbound custom definition cannot be edited");
            using(var bitmap=new Bitmap(dialog.Width,dialog.Height)) { dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(output,"unbound-custom-function.png")); }
        }
        var baseline=c.Clone(); w=new BindingWorkspace(s,c,baseline,false,-1); w.DeleteCustomFunction(id);
        Check(!w.Functions.Any(f=>f.Id==id) && c.CustomFunctions.Count==0, "Explicit delete left a custom function in the catalog");
    }
    static void TriggerChanges()
    {
        EditorSession s; KeyEnhancementConfiguration c; var w = Workspace(out s,out c);
        var spec=Spec("J","sequence"); spec.Rule=new KeyEnhancementRule {Trigger="sequence",Consume=true};
        spec.Rule.Steps.Add(new[]{"J"});spec.Rule.Steps.Add(new[]{"O"});
        var current=w.Add("IA_Jump",spec);
        using(var dialog=new UnifiedBindingEditDialog(w,"IA_Jump",current,null,null))
        {
            dialog.Show();Application.DoEvents();Field<ComboBox>(dialog,"trigger").SelectedIndex=9;Save(dialog);
            var result=w.Replace(current,dialog.Result);
            Check(result.Keys.Length==0 && result.Rule.Steps.Count==0 && result.Rule.Watch.Count==0 && result.Axis!=null, "Axis retained hidden sequence or key conditions");
            File.WriteAllText(Path.Combine(output,"axis-config.lua"),c.CompileLua());
            current=result;
        }
        var pad=new BindingWorkspace(s,c,c.Clone(),true,s.ControllerFamilies.First().Index);
        using(var dialog=new UnifiedBindingEditDialog(pad,"IA_Jump",current,null,null))
        {
            dialog.Show();Application.DoEvents();Field<ComboBox>(dialog,"trigger").SelectedIndex=1;Choose(Field<ComboBox>(dialog,"mainKey"),"K");Save(dialog);
            var result=pad.Replace(current,dialog.Result);Check(result.Axis==null && result.Rule.Steps.Count==0,"Switching away from axis retained hidden input");
            w.Remove(result);
        }
        spec=Spec("J","custom");spec.Rule=new KeyEnhancementRule {Trigger="custom",EvaluateLua="function() return {active=true,pulse=true} end"};spec.Rule.Watch.Add("O");
        current=w.Add("IA_Jump",spec);
        using(var dialog=new UnifiedBindingEditDialog(w,"IA_Jump",current,null,null))
        {
            dialog.Show();Application.DoEvents();Field<ComboBox>(dialog,"trigger").SelectedIndex=1;Choose(Field<ComboBox>(dialog,"mainKey"),"J");Save(dialog);
            var result=w.Replace(current,dialog.Result);
            Check(result.Rule.Watch.Count==0 && result.Rule.EvaluateLua==null,"Press retained hidden script inputs");
        }
    }
    static void Consumption()
    {
        EditorSession s; KeyEnhancementConfiguration c;var w=Workspace(out s,out c);
        var spec=Spec("J","press");spec.Rule=new KeyEnhancementRule();spec.Rule.ConsumeKeys.Add("J");
        var current=w.Add("IA_Jump",spec);
        using(var dialog=new UnifiedBindingEditDialog(w,"IA_Jump",current,null,null))
        {
            dialog.Show();Application.DoEvents();Choose(Field<ComboBox>(dialog,"mainKey"),"K");Save(dialog);
            var result=w.Replace(current,dialog.Result);
            Check(result.Rule.Consume || result.Rule.ConsumeKeys.SequenceEqual(new[]{"K"}),"Rebinding retained consumption of the old key");
            Check(!result.Rule.ConsumeKeys.Contains("J"),"Old key remained consumed after rebinding");
        }
        using(var dialog=new UnifiedBindingEditDialog(w,"IA_Jump",null,null,null))
        {
            dialog.Show();Application.DoEvents();Field<ComboBox>(dialog,"trigger").SelectedIndex=11;
            Watch(Field<CheckedListBox>(dialog,"watchedKeys"),"J");Field<CheckBox>(dialog,"consume").Checked=true;
            Field<TextBox>(dialog,"evaluatorCode").Text="function(now,snapshot) local held=(snapshot.J or 0)>0; return {active=held,pulse=held,reserve=held,value=1} end";
            Check(Field<CheckedListBox>(dialog,"consumeKeys").Items.Count==1,"Watch input missing from consumption picker");Save(dialog);
            var result=w.Add("IA_Jump",dialog.Result);Check(result.Rule.Keys.Count==0 && result.Rule.Watch.SequenceEqual(new[]{"J"}) && result.Rule.Consume,"Watch-only consumption did not persist");
            File.WriteAllText(Path.Combine(output,"watch-config.lua"),c.CompileLua());
        }
    }
    static void NativeDisabled()
    {
        EditorSession s; KeyEnhancementConfiguration c; var w=Workspace(out s,out c);
        var current=w.Entries("IA_Jump").First(entry=>entry.IsNative);
        using(var dialog=new UnifiedBindingEditDialog(w,"IA_Jump",current,null,null))
        {
            dialog.Show();Application.DoEvents();Field<ComboBox>(dialog,"trigger").SelectedIndex=4;
            var toggle=Field<CheckBox>(dialog,"bindingEnabled");Check(toggle.Enabled,"Native replacement cannot be disabled while editing");toggle.Checked=false;Save(dialog);
            var result=w.Replace(current,dialog.Result);Check(!result.Enabled && !w.Entries("IA_Jump").First(entry=>entry.IsNative).Suppressed,"Disabled replacement suppressed its original source");
        }
    }
    static void Duplicate()
    {
        EditorSession s;KeyEnhancementConfiguration c;var w=Workspace(out s,out c);c.Enabled=false;
        for(int i=0;i<3;i++)s.SetKeyboard("IA_Jump",i,new[]{"J","K","L"}[i]);
        using(var dialog=new UnifiedActionChoiceDialog(w,"J",null))
        {
            dialog.Show();Application.DoEvents();Field<TextBox>(dialog,"search").Text="IA_Jump";var list=Field<ListBox>(dialog,"functions");
            for(int i=0;i<list.Items.Count;i++)if(((BindingFunction)list.Items[i]).Id=="IA_Jump")list.SelectedIndex=i;
            Call(dialog,"AddSelected");Check(dialog.DialogResult==DialogResult.OK && !c.Enabled,"Existing relation required enhancement when native slots were full");
        }
    }
    static void RestoreAndPresets()
    {
        EditorSession s;KeyEnhancementConfiguration c;var w=Workspace(out s,out c);
        for(int i=0;i<3;i++)s.SetKeyboard("IA_Jump",i,new[]{"J","K","L"}[i]);
        w.RestoreKey("SpaceBar");Check(w.Entries("IA_Jump").Any(entry=>!entry.IsNative&&entry.Keys.Contains("SpaceBar")),"Full native restoration did not use enhancement");
        Check(s.Relations("IA_Jump",false,-1).Keys.OrderBy(k=>k).SequenceEqual(new[]{"J","K","L"}),"Restore replaced another native key");
        int count=c.Rules.Count;w.RestoreKey("SpaceBar");Check(c.Rules.Count==count,"Repeated enhanced restore duplicated an input");
        for(int i=0;i<3;i++)s.SetKeyboard("IA_Guard",i,new[]{"J","K","L"}[i]);
        using(var form=new MainForm())
        {
            typeof(MainForm).GetProperty("Session").GetSetMethod(true).Invoke(form,new object[]{s}); typeof(MainForm).GetField("keyEnhancement",Hidden).SetValue(form,c);
            Call(form,"StageBindingPreset",Presets.ById("onekey"),PresetApplyMode.Append);
            var planned=Field<KeyEnhancementConfiguration>(form,"keyEnhancement");
            Check(planned.NativeBindings.Any(binding=>binding.FunctionId=="IA_Guard"&&binding.Device=="keyboard"&&binding.Keys.Contains("LeftMouseButton")),"Preset append did not use enhancement for full slots");
            Check(s.Relations("IA_Guard",false,-1).Keys.Contains("J"),"Preset append discarded existing native keys");
            Call(form,"StageBindingPreset",null,PresetApplyMode.Append);
            Check(Field<KeyEnhancementConfiguration>(form,"keyEnhancement").ToJson()==c.ToJson(),"Cancelling preset did not restore enhancement checkpoint");
        }
    }
    static void FailedPlans()
    {
        EditorSession s; KeyEnhancementConfiguration c; var w=Workspace(out s,out c);
        string key=s.Relations("IA_OpenMapMenu",false,-1).DefaultKeys.First();
        for(int i=0;i<3;i++)s.SetKeyboard("IA_OpenMapMenu",i,new[]{"F20","F21","F22"}[i]);
        w.Add("IA_Jump",Spec(key,"default"));w.Add("IA_Jump",Spec(key,"hold"));
        byte[] bytes=s.ExportCurrentScheme();string json=c.ToJson();bool rejected=false;
        try {w.RestoreKey(key);} catch(InvalidDataException) {rejected=true;}
        Check(rejected && bytes.SequenceEqual(s.ExportCurrentScheme()) && c.ToJson()==json,"Unroutable overflow partially committed a whole-key restore");
        using(var form=new MainForm())
        {
            typeof(MainForm).GetProperty("Session").GetSetMethod(true).Invoke(form,new object[]{s});
            typeof(MainForm).GetField("keyEnhancement",Hidden).SetValue(form,c);
            var preset=new Preset {Id="user_map_retry",UserName="追加地图",BaselineId=1,Scopes=new List<PresetScope>{new PresetScope()},
                UserMappings=new List<PresetMapping>{new PresetMapping {Action="IA_OpenMapMenu",Added=new[]{"F23"}}}};
            rejected=false;
            try {Call(form,"StageBindingPreset",preset,PresetApplyMode.Append);}catch(TargetInvocationException ex){rejected=ex.InnerException is InvalidDataException;}
            Check(rejected && bytes.SequenceEqual(s.ExportCurrentScheme()) && Field<KeyEnhancementConfiguration>(form,"keyEnhancement").ToJson()==json,
                "Unroutable preset overflow changed existing drafts");
            s.ChangeRelationship("IA_OpenMapMenu", "F22", false, false, -1);
            s.ChangeRelationship("IA_OpenMapMenu", "F21", false, false, -1);
            s.ChangeRelationship("IA_OpenMapMenu", "F20", false, false, -1);
            byte[] retryBytes = s.ExportCurrentScheme();
            Call(form,"StageBindingPreset",preset,PresetApplyMode.Append);
            var restored=Field<KeyEnhancementConfiguration>(form,"keyEnhancement");
            Check(restored.NativeBindings.Single(binding=>binding.FunctionId=="IA_OpenMapMenu"&&binding.Device=="keyboard").Keys.OrderBy(value=>value)
                .SequenceEqual(s.Relations("IA_OpenMapMenu",false,-1).DefaultKeys.Concat(new[]{"F23"}).OrderBy(value=>value)) && retryBytes.SequenceEqual(s.ExportCurrentScheme()),
                "Retry with a free native slot failed or changed the save draft");
            Call(form,"StageBindingPreset",null,PresetApplyMode.Append);
            Check(Field<KeyEnhancementConfiguration>(form,"keyEnhancement").ToJson()==KeyEnhancementConfiguration.FromJson(json).ToJson() && retryBytes.SequenceEqual(s.ExportCurrentScheme()),
                "Cancelling a successful retry did not restore the enhancement checkpoint");
        }
    }
}
