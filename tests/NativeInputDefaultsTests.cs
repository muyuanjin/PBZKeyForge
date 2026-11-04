using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class NativeInputDefaultsTests
{
    const string Shadow = "IA_ShadowArm";
    const string Default = "InputUserSettings.Profiles.Default";
    static int checks;
    static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Put(byte[] bytes, int offset, uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4); }
    static void Section(byte[] bytes, int index, int raw, uint rva, int size, bool executable)
    {
        int offset=0x188+index*40;
        Put(bytes,offset+8,(uint)size);Put(bytes,offset+12,rva);Put(bytes,offset+16,(uint)size);Put(bytes,offset+20,(uint)raw);
        Put(bytes,offset+36,executable?0x60000020u:0x40000040u);
    }
    static string Fixture(string root, string name, byte[] source, bool current)
    {
        string dir=Path.Combine(root,name),input=Path.Combine(dir,@"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(input));File.WriteAllBytes(input,source);
        string exe=Path.Combine(dir,@"platform\steam\PBZ.exe");Directory.CreateDirectory(Path.GetDirectoryName(exe));
        uint ctor=current?0x68bbb8du:0x688139du,table=current?0xc21f208u:0xc1ade08u,function=current?0x68cd250u:0x48606a0u;
        byte[] bytes=new byte[0xc00];bytes[0]=77;bytes[1]=90;Put(bytes,60,0x80);Put(bytes,0x80,0x4550);bytes[0x84]=0x64;bytes[0x85]=0x86;bytes[0x86]=3;bytes[0x94]=0xf0;bytes[0x98]=0x0b;bytes[0x99]=2;
        Buffer.BlockCopy(BitConverter.GetBytes(0x140000000UL),0,bytes,0xb0,8);
        Section(bytes,0,0x400,ctor,0x200,true);Section(bytes,1,0x600,table,0x400,false);Section(bytes,2,0xa00,function,0x200,true);
        new byte[]{0x48,0x8d,5,0,0,0,0,0x48,0x89,3}.CopyTo(bytes,0x400);Put(bytes,0x403,table-ctor-7);
        Buffer.BlockCopy(BitConverter.GetBytes(0x140000000UL+function),0,bytes,0x978,8);
        byte[] prologue=current?new byte[]{0x40,0x55,0x57,0x41,0x55,0x48,0x8d,0xac,0x24,0,0xff,0xff,0xff,0x48,0x81,0xec,0,2,0,0}
            :new byte[]{0x4c,0x89,0x44,0x24,0x18,0x48,0x89,0x54,0x24,0x10,0x48,0x89,0x4c,0x24,8};
        prologue.CopyTo(bytes,0xa00);
        if(current){bytes[0xa2d]=0xe8;Put(bytes,0xa2e,unchecked(0x488ade0u-function-0x32));}
        File.WriteAllBytes(exe,bytes);return input;
    }
    static BlobEntry Shared(SaveFile save, int raw)
    { return save.RecordFor(Default).Entries.Single(entry=>entry.Name==Shadow&&entry.Slot==raw); }
    static byte[] ShadowSave(byte[] source,string mirror)
    {
        var save=SaveFile.FromBytes(source);var entries=save.RecordFor(Default).Entries;
        entries.RemoveAll(entry=>entry.Name==Shadow);
        entries.Add(new BlobEntry{Name=Shadow,Slot=0,Key="F8",X="None",Y="None"});
        entries.Add(new BlobEntry{Name=Shadow,Slot=2,Key=mirror,X="None",Y="None"});
        foreach(var profile in save.GamepadProfiles)
            foreach(var item in profile.Keys.Items.Where(item=>SaveFile.Field(item,"MappingName")==Shadow&&InputDefaults.RawSlot(SaveFile.Field(item,"Slot"))==2))SaveFile.SetField(item,"Key","F11");
        return save.ToBytes();
    }
    static void Fail(Action action,string message)
    { bool failed=false;try{action();}catch(InvalidOperationException){failed=true;}Assert(failed,message); }
    static string[] Keys(EditorSession session) { return session.Relations(Shadow,false,-1).Keys.OrderBy(key=>key).ToArray(); }
    static void SameKeys(EditorSession session,params string[] expected)
    { Assert(Keys(session).SequenceEqual(expected.OrderBy(key=>key)),"Unexpected effective Shadow Arm keys: "+string.Join(",",Keys(session))); }
    static void NativeDetection(string oldPath,string newPath)
    {
        Assert(SaveFile.Load(oldPath).Defaults==InputDefaultSet.Legacy,"Old native implementation was not recognized.");
        Assert(SaveFile.Load(newPath).Defaults==InputDefaultSet.Current,"New native override was not recognized.");
        string exe=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(newPath),@"..\..\platform\steam\PBZ.exe"));byte[] good=File.ReadAllBytes(exe);
        foreach(int offset in new[]{0x400,0x403,0x978,0xa00,0xa2e})
        {
            byte[] damaged=(byte[])good.Clone();damaged[offset]^=1;File.WriteAllBytes(exe,damaged);
            Assert(SaveFile.Load(newPath).Defaults==InputDefaultSet.Legacy,"Incomplete native evidence selected the new input rules at "+offset);
        }
        File.WriteAllBytes(exe,good);
    }
    static void SessionBehavior(string oldPath,string newPath)
    {
        var old=EditorSession.Open(oldPath);old.SwitchProfile(Default);
        var current=EditorSession.Open(newPath);current.SwitchProfile(Default);
        Assert(old.Defaults==InputDefaultSet.Legacy&&current.Defaults==InputDefaultSet.Current,"Sessions shared a mutable input-rule flag.");
        Assert(current.Defaults.RegisteredSlots(Shadow,false).SequenceEqual(new[]{0,2}),"New keyboard registrations lost the dependent slot.");
        Assert(current.Defaults.RegisteredSlots(Shadow,true).SequenceEqual(new[]{1,3}),"New controller registrations still include raw 2.");
        Assert(Enumerable.Range(0,3).Select(i=>current.GetKeyboardRawSlot(Shadow,i)).SequenceEqual(new[]{0,4,5}),"Mirror displaced supported keyboard extra slots.");
        Assert(current.GetGamepadRawSlot(Shadow,0)==1&&current.GetGamepadRawSlot(Shadow,1)==3&&!current.HasGamepadSlot(Shadow,2),"New controller columns are not independent slots 1/3.");
        Assert(old.GetGamepadRawSlot(Shadow,1)==2&&old.HasGamepadSlot(Shadow,2),"Opening the new session changed the old session.");
        SameKeys(current,"F8");
        var mirror=current.BindingRecords(false,-1).Single(record=>record.Action==Shadow&&record.Slot=="2");
        Assert(mirror.Key=="Gamepad_RightTriggerAxis"&&mirror.ReadOnly&&mirror.SlotIndex==-1,"Raw mirror data was rewritten or presented as independently editable.");
        Fail(()=>current.SetBindingRecord(mirror.Id,"F9"),"Raw setter independently edited the mirror.");
        current.ChangeRelationship(Shadow,"F9",true,false,-1);SameKeys(current,"F8","F9");
        Assert(current.GetKeyboard(Shadow,0)=="F8"&&current.GetKeyboard(Shadow,1)=="F9","Adding a key treated the mirror as a redundant source and overwrote slot 0.");
        var saved=SaveFile.FromBytes(current.ExportCurrentScheme(),current.Defaults);
        Assert(Shared(saved,2).Key==mirror.Key,"Independent editing changed the saved mirror.");
        Assert(saved.Defaults==InputDefaultSet.Current&&saved.Clone().Defaults==InputDefaultSet.Current,"Cloning dropped input defaults.");
        current.ReplaceRelationship(Shadow,"F8","F10",false,-1);SameKeys(current,"F9","F10");
        current.ChangeRelationship(Shadow,"F10",false,false,-1);SameKeys(current,"F9");
        Assert(Shared(SaveFile.FromBytes(current.ExportCurrentScheme()),2).Key==mirror.Key,"Replacing/removing a key wrote the mirror.");
        current.RestoreAction(Shadow,false,-1);SameKeys(current,"LeftControl");
        Assert(Shared(SaveFile.FromBytes(current.ExportCurrentScheme()),2).Key==mirror.Key,"RestoreAction removed the preserved mirror record.");
        var preview=current.PreviewPreset(Presets.ById("space_sprint"));
        Assert(preview.Defaults==InputDefaultSet.Current&&!preview.HasGamepadSlot(Shadow,2),"Preset preview reverted to legacy input rules.");
        current.StagePreset(Presets.ById("vanilla_current"),PresetApplyMode.Replace);
        var restored=SaveFile.FromBytes(current.ExportCurrentScheme());
        Assert(Shared(restored,2).Key==mirror.Key,"Factory preset normalized the mirrored shared record.");
        Assert(restored.GamepadProfiles.Single(profile=>profile.ProfileId==Default&&profile.Family=="Xbox").Keys.Items.Any(item=>SaveFile.Field(item,"MappingName")==Shadow&&InputDefaults.RawSlot(SaveFile.Field(item,"Slot"))==2&&SaveFile.Field(item,"Key")=="F11"),"Factory preset rewrote an inactive controller record.");
        var onlyPreset = new Preset { UserName = "Current controller", BaselineId = 1,
            Scopes = new System.Collections.Generic.List<PresetScope> { new PresetScope { Family = "Xbox" } },
            UserMappings = new System.Collections.Generic.List<PresetMapping> { new PresetMapping { Family = "Xbox", Action = Shadow,
                Added = new[] { "Gamepad_FaceButton_Top" }, Removed = new[] { "Gamepad_RightTriggerAxis" } } } };
        var onlyOutput=SaveFile.Load(newPath);Presets.Apply(onlyOutput,onlyPreset,onlyOutput.RecordFor(Default),PresetApplyMode.OnlyPreset);
        Assert(onlyOutput.GamepadProfiles.Single(profile=>profile.ProfileId==Default&&profile.Family=="Xbox").Keys.Items.Any(item=>SaveFile.Field(item,"MappingName")==Shadow&&InputDefaults.RawSlot(SaveFile.Field(item,"Slot"))==2&&SaveFile.Field(item,"Key")=="F11"),"User OnlyPreset rewrote an inactive controller record.");
        var legacyRecord=old.BindingRecords(true,old.ControllerFamilies.First().Index).Single(r=>r.Action==Shadow&&InputDefaults.RawSlot(r.Slot)==2);
        Assert(!legacyRecord.ReadOnly,"The legacy controller raw 2 became read-only.");
    }
    static void FamilyMaterialization(string input)
    {
        var session=EditorSession.Open(input);session.SwitchProfile(Default);
        int ps=session.ControllerFamilies.Single(family=>family.Name=="PS").Index;
        session.SetGamepadFamily(Shadow,1,ps,"Gamepad_FaceButton_Top");
        var output=SaveFile.FromBytes(session.ExportCurrentScheme(),session.Defaults);
        var createdFamily=output.GamepadProfiles.Single(profile=>profile.ProfileId==Default&&profile.Family=="PS");
        Assert(createdFamily.Keys.Items.Where(item=>SaveFile.Field(item,"MappingName")==Shadow).Select(item=>InputDefaults.RawSlot(SaveFile.Field(item,"Slot"))).OrderBy(raw=>raw).SequenceEqual(new[]{1,3}),"Creating a family materialized the inactive legacy raw 2.");
        Assert(output.GamepadProfiles.Single(profile=>profile.ProfileId==Default&&profile.Family=="Xbox").Keys.Items.Any(item=>SaveFile.Field(item,"MappingName")==Shadow&&InputDefaults.RawSlot(SaveFile.Field(item,"Slot"))==2),"Creating another family deleted an existing inactive legacy record.");
    }
    static void SamePathUpdate(string root,byte[] source,string newPath)
    {
        string input=Fixture(root,"updating",ShadowSave(source,"Gamepad_RightTriggerAxis"),false);
        string exe=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(input),@"..\..\platform\steam\PBZ.exe"));
        string newExe=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(newPath),@"..\..\platform\steam\PBZ.exe"));
        var session=EditorSession.Open(input);session.SwitchProfile(Default);session.SetKeyboard("IA_Jump",0,"F9");
        File.Copy(newExe,exe,true);session.Refresh();
        Assert(session.Defaults==InputDefaultSet.Current&&!session.HasGamepadSlot(Shadow,2),"Same-path native update retained the legacy layout.");
        Assert(session.GetKeyboard("IA_Jump",0)=="F9"&&session.HasAnyDrafts,"Changing native rules lost an unrelated draft.");SameKeys(session,"F8");
        string conflicting=Fixture(root,"updating-conflict",source,false);
        var stale=EditorSession.Open(conflicting);stale.SwitchProfile(Default);stale.SetGamepad(Shadow,1,"Gamepad_FaceButton_Top");
        File.Copy(newExe,Path.GetFullPath(Path.Combine(Path.GetDirectoryName(conflicting),@"..\..\platform\steam\PBZ.exe")),true);
        bool rejected=false;try{stale.Refresh();}catch(IOException){rejected=true;}
        Assert(rejected&&stale.HasAnyDrafts&&stale.Defaults==InputDefaultSet.Legacy,"An incompatible raw-2 draft was silently carried into the new rules.");
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            byte[] source=File.ReadAllBytes(args[0]);string root=Path.GetFullPath(args[1]);Assert(!Directory.Exists(root),"Use a new artifact directory.");Directory.CreateDirectory(root);
            byte[] shadow=ShadowSave(source,"Gamepad_RightTriggerAxis");
            string old=Fixture(root,"old",shadow,false),current=Fixture(root,"current",shadow,true);
            NativeDetection(old,current);SessionBehavior(old,current);FamilyMaterialization(current);SamePathUpdate(root,source,current);
            foreach(string value in new[]{"F9","None"})
            {
                File.WriteAllBytes(current,ShadowSave(source,value));var session=EditorSession.Open(current);session.SwitchProfile(Default);SameKeys(session,"F8");
                session.SetKeyboard(Shadow,0,"None");SameKeys(session);
                Assert(Shared(SaveFile.FromBytes(session.ExportCurrentScheme()),2).Key==value,"Ignored mirror override was normalized.");
            }
            if(args.Length>=4)
            {
                Assert(SaveFile.Load(args[2]).Defaults==InputDefaultSet.Legacy,"Real launch-build installation selected incorrect input rules.");
                Assert(SaveFile.Load(args[3]).Defaults==InputDefaultSet.Current,"Real patched installation did not select the native override rules.");
            }
            Assert(InputDefaults.RegisteredSlots(Shadow,true).SequenceEqual(new[]{1,2,3}),"Static legacy defaults changed.");
            Console.WriteLine("PASS: "+checks+" native input-default assertions; artifacts: "+root);return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
