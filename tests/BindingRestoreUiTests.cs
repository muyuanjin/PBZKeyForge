using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PBZKeyForge;

class BindingRestoreUiTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr key, IntPtr data);
    static int checks;
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethod(name, Hidden).Invoke(owner, args); }
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Click(Button button)
    {
        Check(button.Enabled, "Restore button is disabled");
        int x = button.Width / 2, y = button.Height / 2;
        Point previous = Cursor.Position;
        try
        {
            button.FindForm().Activate(); Cursor.Position = button.PointToScreen(new Point(x, y)); Application.DoEvents();
            SendMessage(button.Handle, 0x201, (IntPtr)1, (IntPtr)((y << 16) | x));
            SendMessage(button.Handle, 0x202, IntPtr.Zero, (IntPtr)((y << 16) | x));
            Application.DoEvents();
        }
        finally { Cursor.Position = previous; }
    }
    static object Row(MainForm form, string key, string function)
    {
        form.RenderSession(); Application.DoEvents(); var view = Field<Control>(form, "keyBindings");
        view.GetType().GetField("selectedKey", Hidden).SetValue(view, key); Call(view, "RenderSelectedKey"); Application.DoEvents();
        return Field<IEnumerable>(view, "actionRows").Cast<object>().Single(row => Field<BindingEntry>(row, "Entry").FunctionId == function);
    }
    static void Shot(Form form, string path)
    {
        form.Refresh(); Application.DoEvents();
        using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(path); }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args.Length == 0 ? "dist/binding-restore-ui" : args[0]); Directory.CreateDirectory(output);
            foreach (bool full in new[] { false, true })
            using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))) { TopMost = true })
            {
                form.UiOffline(); form.Show(); Application.DoEvents();
                var config = Field<KeyEnhancementConfiguration>(form, "keyEnhancement"); config.Enabled = true;
                var ws = new BindingWorkspace(form.Session, config, config.Clone(), false, -1);
                var sprint = ws.Entries("IA_MoveFast").Single(entry => entry.IsNative && entry.Keys.Contains("LeftShift"));
                ws.Remove(sprint); ws.Add("IA_Dodge", new BindingEntry { Keys = new[] { "LeftShift" }, Trigger = "default" });
                if (full)
                    foreach (string key in new[] { "J", "K", "L", "O" })
                    { if (!ws.CanAddNative("IA_MoveFast")) break; ws.Add("IA_MoveFast", new BindingEntry { Keys = new[] { key }, Trigger = "default" }); }
                string[] previous = form.Session.Relations("IA_MoveFast", false, -1).Keys;
                if (full) Check(!ws.CanAddNative("IA_MoveFast"), "Native-slot fixture is not full");
                var row = Row(form, "LeftShift", "IA_MoveFast"); Check(Field<BindingEntry>(row, "Entry").Suppressed, "Removed sprint is missing from its original key");
                Shot(form, Path.Combine(output, full ? "full-before.png" : "removed-sprint.png"));
                int clicks = 0; var operation = Field<Button>(row, "Operation"); operation.Click += delegate { clicks++; }; Click(operation);
                Check(clicks == 1, "Mouse click did not reach the restore button");
                Check(ws.Entries("IA_MoveFast").Any(entry => entry.Active && entry.Keys.Contains("LeftShift")), "Restore did not reactivate sprint on Left Shift; full=" + full + "; status=" + Field<Label>(form, "lblStatus").Text);
                Check(previous.All(key => ws.Entries("IA_MoveFast").Any(entry => entry.Active && entry.Keys.Contains(key))), "Restoring sprint removed another sprint key");
                Check(ws.Entries("IA_Dodge").Any(entry => entry.Active && entry.Keys.Contains("LeftShift")), "Restoring sprint removed Shift dodge");
                row = Row(form, "LeftShift", "IA_MoveFast"); Check(!Field<BindingEntry>(row, "Entry").Suppressed, "Restored sprint still looks removed");
                Shot(form, Path.Combine(output, full ? "full-restored.png" : "sprint-restored.png"));
            }
            using (var form = new MainForm(new EditorPreferences(Path.Combine(output, "runtime-preferences.txt"))) { TopMost = true })
            {
                form.UiOffline(); form.Show(); Application.DoEvents();
                var config = Field<KeyEnhancementConfiguration>(form, "keyEnhancement"); config.Enabled = true;
                var ws = new BindingWorkspace(form.Session, config, config.Clone(), false, -1) { LiveEditing = true };
                byte[] save = form.Session.ExportCurrentScheme();
                var jump = ws.Entries("IA_Jump").Single(entry => entry.IsNative && entry.Keys.Contains("SpaceBar"));
                ws.Replace(jump, new BindingEntry { Keys = new[] { "J" }, Trigger = "default" });
                var row = Row(form, "SpaceBar", "IA_Jump");
                Check(Field<BindingEntry>(row, "Entry").Suppressed, "Runtime replacement is missing from factory key details");
                Click(Field<Button>(row, "Operation"));
                Check(config.NativeBindings.Single().Keys.OrderBy(key => key).SequenceEqual(new[] { "J", "SpaceBar" }), "Factory restore button did not retain J and add Space");
                Check(ws.Entries("IA_Jump").Any(entry => entry.Active && entry.Keys.Contains("SpaceBar")), "Factory restore button left Space inactive");
                Check(form.Session.ExportCurrentScheme().SequenceEqual(save), "Factory restore button changed save bytes under runtime override");
                row = Row(form, "SpaceBar", "IA_Jump");
                Check(!Field<BindingEntry>(row, "Entry").Suppressed, "Runtime factory restore still looks removed");
                Shot(form, Path.Combine(output, "runtime-jump-restored.png"));
            }
            Console.WriteLine("BindingRestoreUiTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
