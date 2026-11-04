using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using PBZKeyForge;

public static class SchemeDiscoveryTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
    static int checks;
    static string root;
    static readonly byte[] Pattern = {
        0xf3,0x0f,0x6f,0x05,0,0,0,0,0xf3,0x0f,0x6f,0x0d,0,0,0,0,
        0xf3,0x0f,0x6f,0x15,0,0,0,0,0x0f,0x57,0xc8,0xf3,0x0f,0x6f,0x05,0,0,0,0,
        0xf3,0x0f,0x7f,0x0a,0x0f,0x57,0xd0,0xf3,0x0f,0x7f,0x52,0x10,0xb0,0x01,0xc3 };
    static readonly int[] Disp = { 4, 12, 20, 31 };
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    static void Put(byte[] b, int at, uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, at, 4); }
    static byte[] Key(int seed) { return Enumerable.Range(0,32).Select(i => (byte)(seed+i*3)).ToArray(); }
    static void Section(byte[] b, int at, uint raw, uint rva, uint size, bool exec)
    {
        Encoding.ASCII.GetBytes(exec ? "arbitrary" : "values").Take(8).ToArray().CopyTo(b,at);
        Put(b,at+8,size); Put(b,at+12,rva); Put(b,at+16,size); Put(b,at+20,raw); Put(b,at+36,exec?0x60000020u:0x40000040u);
    }
    static void Function(byte[] b, int raw, int functionRva, int dataRva, int dataRaw, byte[] key, bool wrong)
    {
        Buffer.BlockCopy(Pattern,0,b,raw,50);
        for (int n=0;n<4;n++)
        {
            int address=dataRaw+n*16;
            for(int j=0;j<16;j++) b[address+j]=(byte)(n%2==0 ? (wrong?77:0) : key[n/2*16+j]);
            Put(b,raw+Disp[n],unchecked((uint)(dataRva+n*16-functionRva-Disp[n]-4)));
        }
    }
    // Data RVA before/after code exercises both displacement signs. Raw addresses deliberately differ.
    static string Image(string name, byte[] key, bool negative, bool decoy, bool onlyNonExec, bool large)
    {
        string dir=Path.Combine(root,name); Directory.CreateDirectory(Path.Combine(dir,@"platform\steam")); Directory.CreateDirectory(Path.Combine(dir,@"ProjectPBZ\Saved"));
        int codeSize=large?128*1024*1024:131072;
        byte[] b=new byte[0x400+codeSize+0x1000];
        b[0]=77;b[1]=90;Put(b,60,0x80);Put(b,0x80,0x4550);b[0x84]=0x64;b[0x85]=0x86;b[0x86]=2;b[0x94]=0xf0;b[0x98]=0x0b;b[0x99]=2;
        int codeRva=negative?0x10000000:0x1000, dataRva=negative?0x1000:codeRva+codeSize+0x8000;
        Section(b,0x188,0x400,(uint)codeRva,(uint)codeSize,true);
        Section(b,0x1b0,(uint)(0x400+codeSize),(uint)dataRva,0x1000,false);
        int target=large?codeSize-64:65536-20;
        if(decoy) Function(b,0x480,codeRva+0x80,dataRva+0x100,0x400+codeSize+0x100,key,true);
        if(!onlyNonExec) Function(b,0x400+target,codeRva+target,dataRva,0x400+codeSize,key,false);
        else Function(b,0x400+codeSize+0x200,dataRva+0x200,dataRva,0x400+codeSize,key,false);
        string exe=Path.Combine(dir,@"platform\steam\PBZ.exe"); File.WriteAllBytes(exe,b); return dir;
    }
    static string Exe(string dir) { return Path.Combine(dir,@"platform\steam\PBZ.exe"); }
    static string Save(string dir) { return Path.Combine(dir,@"ProjectPBZ\Saved\PBZSharedSetting.sav"); }
    static string Input(string dir) { return Path.Combine(dir,@"ProjectPBZ\Saved\EnhancedInputUserSettings.sav"); }
    static void Settings(string dir, byte[] key, string scheme, bool invalid)
    {
        byte[] text=Encoding.UTF8.GetBytes(invalid?"{}":"{\"Unrelated\":123,\"KeyBindingScheme\":\""+scheme+"\"}");
        byte[] plain=new byte[text.Length+5];Put(plain,0,(uint)(text.Length+1));Buffer.BlockCopy(text,0,plain,4,text.Length);
        byte[] nonce=Enumerable.Range(1,12).Select(i=>(byte)i).ToArray(),tag=new byte[16];
        byte[] cipher=(byte[])typeof(GameSchemeSettings).GetMethod("Gcm",Static).Invoke(null,new object[]{plain,key,nonce,tag,true});
        byte[] encoded=new byte[cipher.Length+36];new byte[]{80,66,90,83,1,1,0,0}.CopyTo(encoded,0);nonce.CopyTo(encoded,8);tag.CopyTo(encoded,20);cipher.CopyTo(encoded,36);File.WriteAllBytes(Save(dir),encoded);
    }
    static GameSchemeSettings Load(string dir) { return GameSchemeSettings.Load(Save(dir),Exe(dir)); }
    static EditorSession Fast(string dir) { return (EditorSession)typeof(EditorSession).GetMethod("Open",Static).Invoke(null,new object[]{Input(dir),false}); }
    static GameSchemeSettings FastLoad(string dir) { return (GameSchemeSettings)typeof(GameSchemeSettings).GetMethod("LoadForInputSave",Static).Invoke(null,new object[]{Input(dir),false}); }
    static bool Required(EditorSession s) { return (bool)typeof(EditorSession).GetProperty("SchemeDiscoveryRequired",Private).GetValue(s,null); }
    static void Fail(Action action,string message)
    {
        bool failed=false;try{action();}catch(Exception ex){while(ex is TargetInvocationException)ex=ex.InnerException;failed=ex is IOException || ex is InvalidDataException || ex is System.Security.Cryptography.CryptographicException;}Assert(failed,message);
    }
    static void Pump(Func<bool> ready)
    {
        var watch=Stopwatch.StartNew();while(!ready()&&watch.ElapsedMilliseconds<20000){Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();Assert(ready(),"Timed out waiting for discovery.");
    }
    static object Field(object o,string name) { return o.GetType().GetField(name,Private).GetValue(o); }
    static bool Busy(MainForm f) { return Field(f,"discoverySession")!=null; }
    static void ModalGui(string fixture, bool confirm)
    {
        string dir=Image(confirm?"modal-confirm":"modal-cancel",Key(confirm?51:52),true,false,false,true);
        Settings(dir,Key(confirm?51:52),"DefaultPreset",false);File.Copy(fixture,Input(dir));
        byte[] original=File.ReadAllBytes(Input(dir));
        using(var form=new MainForm(new EditorPreferences(Path.Combine(dir,"prefs"))))
        using(var timer=new System.Windows.Forms.Timer{Interval=10})
        {
            form.Opacity=0;form.ShowInTaskbar=false;form.UiSmoke(Input(dir));
            string selected=form.Session.SelectedProfile;
            Assert(selected!="InputUserSettings.Profiles.Default","Modal test needs a fallback scheme different from the discovered scheme.");
            Exception failure=null;bool dialogSeen=false;
            timer.Tick+=delegate
            {
                var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.GetType().Name=="BindingDialog");
                if(dialog==null)return;
                timer.Stop();dialogSeen=true;
                try
                {
                    Pump(()=>!Busy(form));
                    Assert(form.Session.SelectedProfile==selected&&form.Session.ActiveProfile==null,"Discovery changed scheme while a binding dialog was open.");
                    Assert(Field(form,"completedDiscoverySession")==form.Session,"Modal dialog did not retain the discovery result.");
                    typeof(MainForm).GetMethod("ConsumeSchemeDiscovery",Private).Invoke(form,null);
                    Assert(Field(form,"completedDiscoverySession")==form.Session,"Modal consumption discarded the queued result.");
                    if(confirm)
                    {
                        ((ComboBox)Field(dialog,"keys")).Text="F9";
                        ((Button)dialog.AcceptButton).PerformClick();
                    }
                    else ((Button)dialog.CancelButton).PerformClick();
                }
                catch(Exception ex){failure=ex;dialog.DialogResult=DialogResult.Cancel;}
            };
            form.Shown+=delegate
            {
                form.ShowActionView(true);
                var buttons=(System.Collections.IDictionary)Field(form,"kbButtons");
                timer.Start();((BindingButton[])buttons["IA_Interact"])[0].PerformClick();
            };
            form.Show();Application.DoEvents();
            if(failure!=null)throw failure;
            Assert(dialogSeen,"Modal binding dialog was not exercised.");
            Assert(Field(form,"completedDiscoverySession")==null&&form.Session.ActiveProfile=="InputUserSettings.Profiles.Default","Closing a modal dialog did not consume discovery.");
            if(confirm)
            {
                Assert(form.Session.SelectedProfile==selected&&form.Session.GetKeyboard("IA_Interact",0)=="F9","Confirmed binding moved to the discovered scheme.");
                form.Session.SwitchProfile("InputUserSettings.Profiles.Default");
                Assert(form.Session.GetKeyboard("IA_Interact",0)!="F9","Confirmed binding also edited the other scheme.");
                form.Session.SwitchProfile(selected);
                Assert(form.Session.GetKeyboard("IA_Interact",0)=="F9","Confirmed binding was not retained in its original scheme.");
            }
            else Assert(!form.Session.HasAnyDrafts&&form.Session.SelectedProfile==form.Session.ActiveProfile,"Cancel did not leave the untouched discovered scheme selected.");
            Assert(original.SequenceEqual(File.ReadAllBytes(Input(dir))),"Modal discovery wrote the input fixture.");
        }
    }
    static void Gui(string fixture)
    {
        string dir=Image("gui",Key(9),true,true,false,true);Settings(dir,Key(9),"Custom2",false);File.Copy(fixture,Input(dir));
        using(var form=new MainForm(new EditorPreferences(Path.Combine(root,"prefs"))))
        {
            form.Opacity=0;form.ShowInTaskbar=false;form.UiSmoke(Input(dir));
            Assert(Required(form.Session)&&form.Session.ActiveProfile==null,"GUI fast open invented an active scheme.");
            int beats=0;using(var timer=new System.Windows.Forms.Timer{Interval=10})
            {
                timer.Tick+=delegate{beats++;};timer.Start();form.Show();Application.DoEvents();
                Assert(Busy(form),"Shown did not start discovery: visible="+form.Visible+" required="+Required(form.Session)+" active="+form.Session.ActiveProfile+" error="+form.Session.SchemeError);
                Assert(((ToolTip)Field(form,"tip")).GetToolTip((Control)Field(form,"chkActivate"))==I18n.Str("editor.discovering"),"Pending activation tooltip showed a failure or implementation detail.");
                form.SelectProfile("PBZ.KeyProfile.Custom1");form.Session.SetKeyboard("IA_Interact",0,"Q");
                Pump(()=>!Busy(form));
                Assert(beats>0,"The UI heartbeat stopped during discovery.");
                Assert(form.Session.ActiveProfile=="PBZ.KeyProfile.Custom2","GUI discovery did not authenticate current scheme.");
                Assert(form.Session.SelectedProfile=="PBZ.KeyProfile.Custom1"&&form.Session.GetKeyboard("IA_Interact",0)=="Q","Discovery overwrote profile choice or draft.");
            }
            // Passive activation refresh starts a new request when the same-path EXE changes.
            string updated=Image("gui-updated",Key(27),false,false,false,true);Settings(dir,Key(27),"DefaultPreset",false);
            File.Copy(Exe(updated),Exe(dir),true);File.SetLastWriteTimeUtc(Exe(dir),DateTime.UtcNow.AddSeconds(4));
            typeof(MainForm).GetMethod("RefreshFromDisk",Private,null,new[]{typeof(bool)},null).Invoke(form,new object[]{false});
            Assert(Busy(form)&&form.Session.ActiveProfile==null,"Passive refresh ignored a changed EXE discovery request.");Pump(()=>!Busy(form));
            Assert(form.Session.ActiveProfile=="InputUserSettings.Profiles.Default"&&form.Session.SelectedProfile=="PBZ.KeyProfile.Custom1"&&form.Session.GetKeyboard("IA_Interact",0)=="Q","Changed-EXE discovery lost the current choice or draft.");
            // An old session's completion cannot affect its replacement.
            string other=Image("old-session",Key(12),false,false,false,true);Settings(other,Key(12),"Custom2",false);File.Copy(fixture,Input(other));
            form.UiSmoke(Input(other));Assert(Busy(form),"Second session did not discover.");
            form.UiSmoke(Input(dir));var current=form.Session;Pump(()=>!Busy(form));Assert(form.Session==current&&current.ActiveProfile=="InputUserSettings.Profiles.Default","Stale completion replaced the current session.");
            // A real worker completion must defer all Session mutation while Apply owns it.
            string deferred=Image("deferred",Key(22),true,false,false,true);Settings(deferred,Key(22),"Custom1",false);File.Copy(fixture,Input(deferred));
            form.UiSmoke(Input(deferred));var deferredSession=form.Session;
            typeof(MainForm).GetProperty("IsApplying").GetSetMethod(true).Invoke(form,new object[]{true});
            Pump(()=>!Busy(form));
            Assert(Field(form,"completedDiscoverySession")==deferredSession&&deferredSession.ActiveProfile==null&&Required(deferredSession),"Apply did not defer discovery completion.");
            typeof(MainForm).GetProperty("IsApplying").GetSetMethod(true).Invoke(form,new object[]{false});
            typeof(MainForm).GetMethod("ConsumeSchemeDiscovery",Private).Invoke(form,null);
            Assert(Field(form,"completedDiscoverySession")==null&&deferredSession.ActiveProfile=="PBZ.KeyProfile.Custom1"&&deferredSession.SelectedProfile=="PBZ.KeyProfile.Custom1","Deferred completion did not select the untouched initial active scheme.");
            // Dispose avoids the normal unsaved-draft close dialog.
        }
        string bad=Image("gui-failure",Key(33),false,false,true,false);Settings(bad,Key(33),"Custom1",false);File.Copy(fixture,Input(bad));
        using(var form=new MainForm(new EditorPreferences(Path.Combine(root,"prefs2"))))
        {
            form.Opacity=0;form.ShowInTaskbar=false;form.UiSmoke(Input(bad));form.Show();Application.DoEvents();Pump(()=>!Busy(form));
            Assert(form.Session.ActiveProfile==null,"Failed discovery supplied an active scheme.");
            typeof(MainForm).GetMethod("RefreshFromDisk",Private,null,new[]{typeof(bool)},null).Invoke(form,new object[]{false});
            Application.DoEvents();Assert(!Busy(form),"Passive refresh restarted a failed probe.");
            form.RefreshFromDisk();Assert(Busy(form),"Explicit refresh did not retry discovery.");Pump(()=>!Busy(form));
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        int result=1;
        EventHandler run=null;
        run=delegate { Application.Idle-=run; result=Run(args); Application.ExitThread(); };
        Application.Idle+=run; Application.Run(); return result;
    }
    static int Run(string[] args)
    {
        try
        {
            bool readOnly=args.Contains("--read-only");var paths=args.Where(a=>a!="--read-only").ToArray();root=Path.GetFullPath(paths[1]);Assert(!Directory.Exists(root),"Use a new artifact directory.");Directory.CreateDirectory(root);
            string positive=Image("positive",Key(3),false,true,false,false);Settings(positive,Key(3),"Custom2",false);File.Copy(paths[0],Input(positive));
            var pending=Fast(positive);Assert(Required(pending)&&pending.ActiveProfile==null,"Uncached fast open performed discovery.");
            Assert(Load(positive).ProfileId=="PBZ.KeyProfile.Custom2","Positive RIP, raw/RVA mapping, wrong candidate or block boundary failed.");
            Assert(FastLoad(positive).ProfileId=="PBZ.KeyProfile.Custom2","Cached fast reader failed.");
            // Keys and authentication are never cached, even with stable EXE metadata.
            byte[] stableImage=File.ReadAllBytes(Exe(positive));DateTime stableStamp=File.GetLastWriteTimeUtc(Exe(positive));
            byte[] updatedImage=(byte[])stableImage.Clone();byte[] updatedKey=Key(11);
            for(int n=0;n<2;n++)for(int j=0;j<16;j++)updatedImage[0x400+131072+(n*2+1)*16+j]=updatedKey[n*16+j];
            File.WriteAllBytes(Exe(positive),updatedImage);File.SetLastWriteTimeUtc(Exe(positive),stableStamp);Settings(positive,updatedKey,"Custom1",false);
            Assert(FastLoad(positive).ProfileId=="PBZ.KeyProfile.Custom1","Cached location reused a key or authentication result.");
            File.WriteAllBytes(Exe(positive),stableImage);File.SetLastWriteTimeUtc(Exe(positive),stableStamp);Settings(positive,Key(3),"Custom2",false);
            string badFirst=Image("bad-first",Key(41),false,true,false,false);Settings(badFirst,Key(41),"Custom1",false);
            byte[] repaired=File.ReadAllBytes(Save(badFirst)),damaged=(byte[])repaired.Clone();damaged[20]^=1;File.WriteAllBytes(Save(badFirst),damaged);
            Fail(()=>Load(badFirst),"Initial damaged save authenticated.");File.WriteAllBytes(Save(badFirst),repaired);
            Assert(FastLoad(badFirst).ProfileId=="PBZ.KeyProfile.Custom1","A failed initial authentication discarded discovered locations.");
            string negative=Image("negative",Key(7),true,false,false,false);Settings(negative,Key(7),"Custom1",false);Assert(Load(negative).ProfileId=="PBZ.KeyProfile.Custom1","Negative displacement failed.");
            string fake=Image("nonexec",Key(8),false,false,true,false);Settings(fake,Key(8),"Custom1",false);Fail(()=>Load(fake),"A nonexecutable pattern was accepted.");
            string recovered=Image("nonexec-recovered",Key(8),true,false,false,false);File.Copy(Exe(recovered),Exe(fake),true);
            Assert(Load(fake).ProfileId=="PBZ.KeyProfile.Custom1","An empty scan was permanently cached.");
            byte[] good=File.ReadAllBytes(Save(positive)),bad=(byte[])good.Clone();bad[20]^=1;File.WriteAllBytes(Save(positive),bad);Fail(()=>Load(positive),"Tampered PBZS accepted.");
            File.WriteAllBytes(Save(positive),good);Assert(FastLoad(positive).ProfileId=="PBZ.KeyProfile.Custom2","Bad-save recovery lost the complete location cache.");
            // Replacing same-path image changes both key and references; old objects must reauthenticate.
            var old=Load(negative);byte[] original=File.ReadAllBytes(Save(negative));
            string replacement=Image("replacement",Key(19),false,true,false,false);File.Copy(Exe(replacement),Exe(negative),true);File.SetLastWriteTimeUtc(Exe(negative),DateTime.UtcNow.AddSeconds(2));
            Fail(()=>Load(negative),"Old ciphertext authenticated with a replaced executable key.");
            if(!readOnly){Fail(()=>old.Activate("PBZ.KeyProfile.Custom2"),"Old settings object wrote using a new executable key.");Fail(()=>old.Activate("PBZ.KeyProfile.Custom1"),"An activation no-op skipped current-EXE authentication.");Assert(original.SequenceEqual(File.ReadAllBytes(Save(negative))),"Rejected activation changed settings.");}
            Settings(negative,Key(19),"DefaultPreset",false);Assert(Load(negative).ProfileId=="InputUserSettings.Profiles.Default","Same-path executable update did not rediscover.");
            // Cached function bytes are rechecked even when length and timestamp match.
            DateTime stamp=File.GetLastWriteTimeUtc(Exe(negative));byte[] image=File.ReadAllBytes(Exe(negative));image[0x400+65536-20]=0;File.WriteAllBytes(Exe(negative),image);File.SetLastWriteTimeUtc(Exe(negative),stamp);Fail(()=>FastLoad(negative),"Invalid cached function was trusted.");
            Settings(positive,Key(3),"Custom2",true);Fail(()=>Load(positive),"Authenticated invalid content was accepted.");Settings(positive,Key(3),"Custom2",false);
            if(!readOnly)
            {
                string moved=Image("moved-same-key",Key(3),true,false,false,false);var held=Load(positive);File.Copy(Exe(moved),Exe(positive),true);File.SetLastWriteTimeUtc(Exe(positive),DateTime.UtcNow.AddSeconds(3));
                Assert(held.Activate("PBZ.KeyProfile.Custom1")!=null&&Load(positive).ProfileId=="PBZ.KeyProfile.Custom1","Same-key relocation broke activation.");
            }
            ModalGui(paths[0],true);ModalGui(paths[0],false);Gui(paths[0]);
            Console.WriteLine("PASS: "+checks+" scheme-discovery assertions"+(readOnly?" (read-only activation mode)":"")+"; artifacts: "+root);return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
