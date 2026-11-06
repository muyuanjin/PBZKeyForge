using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

public static class DeviceAlignmentTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    static T Field<T>(object owner, string name)
    { return (T)owner.GetType().GetField(name, Hidden | BindingFlags.Public).GetValue(owner); }
    static object Call(object owner, string name, params object[] args)
    { return owner.GetType().GetMethod(name, Hidden | BindingFlags.Public).Invoke(owner, args); }
    static PointF Center(RectangleF box)
    { return new PointF(box.Left + box.Width / 2, box.Top + box.Height / 2); }
    static Delegate StateFactory(Type state, string countedKey, int count, bool complete)
    {
        var key = Expression.Parameter(typeof(string), "key");
        Expression number = countedKey == null ? (Expression)Expression.Constant(count)
            : Expression.Condition(Expression.Equal(key, Expression.Constant(countedKey)), Expression.Constant(count), Expression.Constant(1));
        return Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(string), state),
            Expression.MemberInit(Expression.New(state), Expression.Bind(state.GetField("Count"), number),
                Expression.Bind(state.GetField("Complete"), Expression.Constant(complete))), new[] { key }).Compile();
    }
    static void Save(Control control, string path)
    {
        control.Refresh();
        using (var bitmap = new Bitmap(control.Width, control.Height))
        { control.DrawToBitmap(bitmap, control.ClientRectangle); bitmap.Save(path); }
    }
    static void Reference(Control diagram, string device, string key, RectangleF source, Size canvas)
    {
        var image = Field<Dictionary<string, RectangleF>>(diagram, "imageBounds")[device];
        var actual = Field<Dictionary<string, RectangleF>>(diagram, "targets")[key];
        var expected = new RectangleF(image.X + source.X / canvas.Width * image.Width,
            image.Y + source.Y / canvas.Height * image.Height, source.Width / canvas.Width * image.Width, source.Height / canvas.Height * image.Height);
        Check(Math.Abs(actual.X - expected.X) < .05f && Math.Abs(actual.Y - expected.Y) < .05f
            && Math.Abs(actual.Width - expected.Width) < .05f && Math.Abs(actual.Height - expected.Height) < .05f,
            device + " " + key + " does not follow the artwork's key face: " + actual + " expected " + expected);
    }
    static void References(Control diagram, string device)
    {
        // Pixel measurements of the shipped artwork, including staggered rows and narrow shoulder bands.
        if (device == "keyboard")
        {
            var size = new Size(1497, 572);
            Reference(diagram, device, "B", new RectangleF(537, 377, 51, 53), size);
            Reference(diagram, device, "A", new RectangleF(198, 305, 50, 53), size);
            Reference(diagram, device, "LeftShift", new RectangleF(67, 377, 144, 53), size);
            Reference(diagram, device, "RightShift", new RectangleF(989, 377, 178, 53), size);
            Reference(diagram, device, "SpaceBar", new RectangleF(346, 449, 448, 53), size);
            Reference(diagram, device, "Menu", new RectangleF(1005, 449, 65, 53), size);
            Reference(diagram, device, "Up", new RectangleF(1295, 375, 54, 54), size);
            Reference(diagram, "mouse", "MiddleMouseButton", new RectangleF(204, 108, 43, 129), new Size(457, 745));
            Reference(diagram, "mouse", "ThumbMouseButton2", new RectangleF(26, 275, 40, 102), new Size(457, 745));
        }
        else if (device == "xbox")
        {
            var size = new Size(1058, 726);
            Reference(diagram, device, "Gamepad_LeftShoulder", new RectangleF(205, 83, 152, 35), size);
            Reference(diagram, device, "Gamepad_LeftThumbstick", new RectangleF(203, 194, 119, 119), size);
            Reference(diagram, device, "Gamepad_FaceButton_Left", new RectangleF(712, 218, 61, 61), size);
        }
        else
        {
            var size = new Size(1103, 711);
            Reference(diagram, device, "Gamepad_LeftShoulder", new RectangleF(184, 70, 126, 24), size);
            Reference(diagram, device, "Gamepad_Special_Left", new RectangleF(329, 122, 24, 53), size);
            Reference(diagram, device, "Gamepad_RightThumbstick", new RectangleF(652, 333, 117, 117), size);
        }
    }
    static void EnhancedPreviewInputs()
    {
        var session = EditorSession.CreateOffline();
        int family = session.ControllerFamilies.Single(item => item.Name == "Xbox").Index;
        var config = new KeyEnhancementConfiguration { Enabled = true };
        var workspace = new BindingWorkspace(session, config, new KeyEnhancementConfiguration(), true, family);
        workspace.Add("IA_Jump", new BindingEntry { Axis = "Gamepad_RightTriggerAxis", Trigger = "axis" });
        workspace.Add("IA_Guard", new BindingEntry { Trigger = "sequence", Rule = new KeyEnhancementRule {
            Trigger = "sequence", Steps = new List<string[]> { new[] { "Gamepad_FaceButton_Top" }, new[] { "Gamepad_FaceButton_Right" } } } });
        workspace.Add("IA_HeavyAttack", new BindingEntry { Trigger = "custom", Rule = new KeyEnhancementRule {
            Trigger = "custom", Watch = new List<string> { "Gamepad_LeftShoulder" }, EvaluateLua = "return false" } });
        var preset = UserPresetStore.Capture(session, "输入预览", config);
        object[] args = { session.PreviewPreset(preset), preset, true, family, null };
        var state = (Delegate)typeof(MainForm).Assembly.GetType("PBZKeyForge.UnifiedBindingUi").GetMethod("PresetKeyStates", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        var keys = (string[])args[4];
        Check(keys.Contains("Gamepad_RightTrigger") && !keys.Contains("Gamepad_RightTriggerAxis"), "Enhanced preview draws a duplicate trigger axis key");
        var current = Field<ActionRelations[]>(Call(workspace, "Snapshot"), "Current");
        foreach (string key in new[] { "Gamepad_RightTrigger", "Gamepad_FaceButton_Top", "Gamepad_FaceButton_Right", "Gamepad_LeftShoulder" })
        {
            int expected = current.Count(relation => relation.Keys.Select(input => input == "Gamepad_RightTriggerAxis" ? "Gamepad_RightTrigger" : input).Contains(key));
            object badge = state.DynamicInvoke(key);
            Check(keys.Contains(key) && Field<int>(badge, "Count") == expected && Field<bool>(badge, "Differs"), "Enhanced preview omits or miscounts " + key);
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            EnhancedPreviewInputs();
            var assembly = typeof(MainForm).Assembly;
            var state = assembly.GetType("PBZKeyForge.KeyBadgeState");
            var factory = StateFactory(state, null, 1, true);
            foreach (string device in new[] { "keyboard", "xbox", "playstation" })
            using (var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) })
            {
                Control diagram = null; string selected = null;
                Action<string> choose = delegate(string key) { selected = key; Call(diagram, "SelectKey", key); };
                diagram = (Control)Activator.CreateInstance(assembly.GetType("PBZKeyForge.DeviceDiagram"),
                    new object[] { device != "keyboard", device == "playstation" ? "PS" : "Xbox", factory, choose, null, new string[0] });
                host.Controls.Add(diagram); host.Show();
                foreach (var size in new[] { new Size(1400, 680), new Size(700, 400), new Size(450, 200) })
                {
                    host.ClientSize = size; Application.DoEvents(); diagram.PerformLayout(); References(diagram, device);
                    var targets = Field<Dictionary<string, RectangleF>>(diagram, "targets");
                    Check(targets.Count == (device == "keyboard" ? 92 : 16), "Missing physical keys for " + device);
                    foreach (var target in targets)
                    {
                        Point center = Point.Round(Center(target.Value));
                        Call(diagram, "OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1, center.X, center.Y, 0));
                        Check(selected == target.Key, device + " " + size + " clicks a different key at " + center + ": " + selected);
                        // The center line near either edge must belong to the same button, including wide keys.
                        foreach (float fraction in new[] { .15f, .85f })
                        {
                            Point point = Point.Round(new PointF(target.Value.Left + target.Value.Width * fraction, center.Y));
                            Check((string)Call(diagram, "Hit", point) == target.Key, "Key edge selects a neighbor: " + device + " " + size + " " + target.Key + " at " + point + " hit " + Call(diagram, "Hit", point));
                        }
                    }
                    if (device != "keyboard")
                    {
                        foreach (string side in new[] { "Left", "Right" })
                            Check(!targets["Gamepad_" + side + "Trigger"].IntersectsWith(targets["Gamepad_" + side + "Shoulder"]), "Trigger and shoulder overlap");
                    }
                    Call(diagram, "SelectKey", device == "keyboard" ? "B" : "Gamepad_LeftShoulder");
                    Save(diagram, Path.Combine(output, device + "-" + size.Width + "x" + size.Height + ".png"));
                    if (size.Width == 1400)
                    {
                        const int cellWidth = 190, cellHeight = 150, columns = 8;
                        using (var sheet = new Bitmap(columns * cellWidth, ((targets.Count + columns - 1) / columns) * cellHeight))
                        using (var graphics = Graphics.FromImage(sheet))
                        using (var font = new Font("Segoe UI", 8f))
                        using (var snapshot = new Bitmap(diagram.Width, diagram.Height))
                        {
                            graphics.Clear(Color.FromArgb(24, 29, 33)); int index = 0;
                            foreach (var target in targets)
                            {
                                Call(diagram, "SelectKey", target.Key); diagram.Refresh(); diagram.DrawToBitmap(snapshot, diagram.ClientRectangle);
                                Rectangle crop = Rectangle.Intersect(diagram.ClientRectangle, Rectangle.Ceiling(RectangleF.Inflate(target.Value, 12, 12)));
                                int left = index % columns * cellWidth, top = index / columns * cellHeight;
                                float ratio = Math.Min(1f, Math.Min((cellWidth - 10f) / crop.Width, (cellHeight - 25f) / crop.Height));
                                graphics.DrawImage(snapshot, new RectangleF(left + (cellWidth - crop.Width * ratio) / 2, top + 22, crop.Width * ratio, crop.Height * ratio), crop, GraphicsUnit.Pixel);
                                graphics.DrawString(target.Key.Replace("Gamepad_", ""), font, Brushes.White, left + 5, top + 3); index++;
                            }
                            sheet.Save(Path.Combine(output, device + "-all-keys.png"));
                        }
                    }
                    var scroll = (ScrollableControl)diagram;
                    if (scroll.VerticalScroll.Visible)
                    {
                        scroll.AutoScrollPosition = new Point(0, scroll.AutoScrollMinSize.Height); diagram.PerformLayout(); References(diagram, device);
                        Save(diagram, Path.Combine(output, device + "-scrolled.png"));
                    }
                    scroll.AutoScrollPosition = Point.Empty;
                }
                string[] extras = device == "keyboard" ? new[] { "NumPadZero", "NumPadOne", "NumPadTwo", "NumPadThree", "NumPadFour", "NumPadFive", "NumPadSix", "NumPadSeven", "NumPadEight", "NumPadNine", "Add", "Subtract", "Multiply", "Divide", "Decimal", "NumLock" }
                    : new[] { "Gamepad_LeftX", "Gamepad_LeftY", "Gamepad_RightX", "Gamepad_RightY" };
                string countedKey = device == "keyboard" ? "B" : "Gamepad_RightTrigger";
                foreach (int count in new[] { 12, 359 })
                {
                    var countedFactory = StateFactory(state, countedKey, count, count == 12);
                    var previous = Field<Dictionary<string, RectangleF>>(diagram, "badges")[countedKey];
                    Call(diagram, "UpdateBindings", device != "keyboard", device == "playstation" ? "PS" : "Xbox", countedFactory, countedKey, new string[0]);
                    var badge = Field<Dictionary<string, RectangleF>>(diagram, "badges")[countedKey];
                    Check(badge.Width > previous.Width, "Badge did not grow when the binding count changed: " + countedKey);
                    using (var font = (Font)Call(diagram, "BadgeFont", countedKey, badge.Height))
                    {
                        string number = count.ToString() + (count == 12 ? "" : "+");
                        Size text = TextRenderer.MeasureText(number, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                        Check(text.Width <= badge.Width, "Binding count is clipped: " + number);
                    }
                    Save(diagram, Path.Combine(output, device + "-count-" + count + ".png"));
                }
                Call(diagram, "UpdateBindings", device != "keyboard", device == "playstation" ? "PS" : "Xbox", factory, null, extras);
                // Preview capture must include the complete controller and wrapped keys, independent of screen DPI.
                host.Controls.Remove(diagram);
                using (diagram)
                foreach (int width in new[] { 1000, 600 })
                using (var snapshot = (Bitmap)Call(diagram, "CapturePreview", width))
                {
                    var scroll = (ScrollableControl)diagram;
                    Check(!scroll.VerticalScroll.Visible && !scroll.HorizontalScroll.Visible, "Preview has a scrollbar: " + device + " " + width);
                    foreach (var target in Field<Dictionary<string, RectangleF>>(diagram, "targets"))
                        Check(diagram.ClientRectangle.Contains(Rectangle.Ceiling(target.Value)), "Preview clips " + device + " " + target.Key);
                    foreach (var area in Field<Dictionary<string, RectangleF>>(diagram, "imageBounds").Values)
                        Check(diagram.ClientRectangle.Contains(Rectangle.Ceiling(area)), "Preview clips artwork: " + device);
                    References(diagram, device);
                    snapshot.Save(Path.Combine(output, device + "-preview-" + width + ".png"));
                }
            }
            Console.WriteLine("DeviceAlignmentTests PASS: " + checks + " checks; artifacts " + output); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
