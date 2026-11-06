using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using PBZKeyForge;

public static class KeyEnhancementFlameOverlayTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;


    static object Field(object owner, string name)
    { return owner.GetType().GetField(name, Hidden).GetValue(owner); }

    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }

    static void Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
    }

    static void PaintFlames(object overlay, Graphics graphics, Size size, float time)
    {
        overlay.GetType().GetMethod("PaintFlames", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { graphics, size, time, 0f });
    }

    static void Shot(MainForm form, object overlay, string directory, string name)
    {
        form.Refresh(); Application.DoEvents();
        using (var bitmap = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppPArgb))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(directory, name + ".png"));
        }
    }

    static void CheckFrame(object overlay)
    {
        Size canvas = ((Form)Field(overlay, "host")).ClientSize;
        using (var first = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppPArgb))
        using (var second = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppPArgb))
        {
            using (var graphics = Graphics.FromImage(first)) PaintFlames(overlay, graphics, first.Size, 1.7f);
            using (var graphics = Graphics.FromImage(second)) PaintFlames(overlay, graphics, second.Size, 2.4f);
            int top = (int)Math.Ceiling(first.Height * .40f), upper = 0, bottom = 0, changes = 0, maxAlpha = 0;
            bool clear = true, purple = true;
            int naturalTip = 0, colored = 0, translucent = 0, violet = 0, brightCores = 0;
            for (int y = 0; y < first.Height; y += 3)
                for (int x = 0; x < first.Width; x += 7)
                {
                    var pixel = first.GetPixel(x, y);
                    if (y >= 55 && y < top && x >= 55 && x < first.Width - 55) clear &= pixel.A == 0;
                    else if (y >= top)
                    {
                        if (x < 55 || x >= first.Width - 55) continue;
                        if (y < top + 5) naturalTip = Math.Max(naturalTip, pixel.A);
                        maxAlpha = Math.Max(maxAlpha, pixel.A);
                        if (pixel.A > 8)
                        {
                            colored++;
                            if (pixel.A < 150) translucent++;
                            if (pixel.A >= 236) brightCores++;
                            if (pixel.B - pixel.G > 15 && pixel.R - pixel.G > 10) violet++;
                        }
                        if (pixel.A > 20) purple &= pixel.B >= pixel.G && pixel.R >= pixel.G;
                        if (y < first.Height * .90 && pixel.A > 8) upper++;
                        if (pixel != second.GetPixel(x, y)) changes++;
                    }
                }
            for (int x = 0; x < first.Width; x += 7)
                if (first.GetPixel(x, first.Height - 3).A > 15) bottom++;
            int darkRun = 0, longestDarkRun = 0;
            for (int x = 55; x < first.Width - 55; x++)
            {
                bool burning = false;
                for (int y = first.Height - Math.Max(6, first.Height / 40); y < first.Height; y += 2)
                    burning |= first.GetPixel(x, y).A > 8;
                darkRun = burning ? 0 : darkRun + 1;
                longestDarkRun = Math.Max(longestDarkRun, darkRun);
            }
            first.Save(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "flame-field.png"));
            Check(clear, "Flames cover the central editing surface.");
            Check(naturalTip == 0, "Hot flames reach the top of the simulation band and are clipped along a straight line.");
            Check(upper > 10, "Flames remain confined to a narrow strip instead of rising.");
            Check(bottom > first.Width / 20, "The flame base must remain visible across the window.");
            Check(longestDarkRun <= 8, "The continuous flame base contains an empty band: " + longestDarkRun + " pixels.");
            Check(changes > 100, "Animation ticks do not change the flame field.");
            Check(maxAlpha > 35 && brightCores < colored * .02f && translucent > colored * .75f,
                "The flame body must stay translucent while allowing small bright cores.");
            Check(purple && violet > colored * .60f, "Key enhancement flames must use a purple palette with pale hot cores.");
        }
    }

    static Bitmap MarkShot(Control bar)
    {
        using (var graphics = bar.CreateGraphics())
        using (var bitmap = new Bitmap(bar.Width, bar.Height, PixelFormat.Format32bppPArgb))
        {
            var effect = Field(bar.FindForm(), "keyEnhancementFlame");
            Color previousBackground = bar.BackColor;
            bool enabled = (bool)effect.GetType().GetProperty("FlameEnabled", Hidden).GetValue(effect, null);
            EnableEffect(effect, false);
            bar.BackColor = Color.FromArgb(29, 29, 29);
            try { bar.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); }
            finally { bar.BackColor = previousBackground; EnableEffect(effect, enabled); }
            float scale = graphics.DpiX / 96f;
            int markSize = Math.Min(bar.Height - (int)(4 * scale), (int)(64 * scale));
            return bitmap.Clone(new Rectangle(0, (bar.Height - markSize) / 2, markSize, markSize), PixelFormat.Format32bppPArgb);
        }
    }

    static void CheckMark(Control bar, Bitmap disabledMark)
    {
        int markHeight = disabledMark.Height;
        using (disabledMark)
        using (var enabledMark = MarkShot(bar))
        {
            int changed = 0;
            for (int y = 0; y < enabledMark.Height; y++)
                for (int x = 0; x < enabledMark.Width; x++)
                    if (disabledMark.GetPixel(x, y) != enabledMark.GetPixel(x, y)) changed++;
            int gray = 0, tinted = 0;
            for (int y = 0; y < disabledMark.Height; y++)
                for (int x = 0; x < disabledMark.Width; x++)
                {
                    var off = disabledMark.GetPixel(x, y);
                    if (Math.Abs(off.R - off.G) <= 2 && Math.Abs(off.G - off.B) <= 2) gray++;
                    var on = enabledMark.GetPixel(x, y);
                    if (Math.Max(on.R, Math.Max(on.G, on.B)) - Math.Min(on.R, Math.Min(on.G, on.B)) > 15) tinted++;
                }
            Check(changed > 100 && gray > disabledMark.Width * disabledMark.Height * .90f && tinted > 100,
                "The disabled puppet mark must be grayscale and the enabled mark must retain its colors.");
        }
        var artwork = typeof(MainForm).Assembly.GetType("PBZKeyForge.EditorArtwork");
        var mark = (Image)artwork.GetField("KeyEnhancementMark", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        Check(mark != null && mark.Width == mark.Height, "The square puppet artwork is missing.");
        var toggle = (Control)Field(bar, "Toggle");
        using (var graphics = bar.CreateGraphics())
            Check(markHeight == (int)(64 * graphics.DpiX / 96f) && toggle.Left >= markHeight,
                "The puppet mark must use 64 logical pixels without overlapping the toggle.");
    }

    static void CheckFooter(MainForm form)
    {
        var bar = (Control)Field(form, "keyEnhancementBar");
        var mod = (Control)Field(form, "chkPerfectDeflectNoRapidClickPenalty");
        var activate = (Control)Field(form, "chkActivate");
        var apply = (Control)Field(form, "btnApply");
        using (var graphics = form.CreateGraphics())
            Check(bar.Parent.Height <= 88 * graphics.DpiX / 96f, "The compact footer exceeds 88 logical pixels.");
        Check(bar.Parent == mod.Parent && mod.Parent == activate.Parent, "Footer options must share one bar.");
        Check(bar.Right < mod.Left && mod.Right < activate.Left, "Footer options are not ordered key enhancement, mod, activation.");
        Check(Math.Abs(bar.Top - mod.Top) <= 1 && Math.Abs(activate.Top - bar.Top) <= 8,
            "Footer options do not share one row.");
        Check(bar.Left >= 0 && activate.Right <= activate.Parent.ClientSize.Width && apply.Bottom <= apply.Parent.Height,
            "Footer options or Apply are clipped.");
        var scope = (Control)Field(form, "lblScope");
        var status = (Control)Field(form, "lblStatus");
        Check(scope.Left == activate.Left && status.Left == activate.Left && scope.Top >= activate.Bottom - 3
            && status.Top >= scope.Bottom && status.Right < apply.Left, "Scheme status must sit beneath activation, beside Apply.");
        Check(Math.Abs(apply.Top + apply.Height / 2 - apply.Parent.Height / 2) <= 1, "Apply must be vertically centered in the footer.");
        using (var graphics = form.CreateGraphics())
            Check(apply.Height >= 44 * graphics.DpiX / 96f - 1, "The primary Apply button is too short.");
        using (var graphics = bar.CreateGraphics())
        using (var title = new Font(bar.Font.FontFamily, 10f, FontStyle.Bold))
        using (var badge = new Font(bar.Font.FontFamily, 8f))
        {
            float scale = graphics.DpiX / 96f;
            int required = TextRenderer.MeasureText(graphics, I18n.Str("enhancement.title"), title).Width
                + TextRenderer.MeasureText(graphics, I18n.Str("mod.badge"), badge).Width + bar.Height - (int)(4 * scale) + (int)(44 * scale);
            Check(bar.Width >= required, "The key enhancement title or Mod badge is clipped.");
        }
        var help = (Control)Field(form, "btnModHelp");
        Check(help.Visible && help.Right <= mod.Right && help.Top >= mod.Top && help.Bottom <= mod.Bottom,
            "The resource Mod help control is clipped.");
        Check(help.Parent.GetChildAtPoint(new Point(help.Left + help.Width / 2, help.Top + help.Height / 2)) == help,
            "The resource Mod help control is covered.");
    }

    static bool Active(object effect)
    { return (bool)effect.GetType().GetProperty("IsActive", Hidden).GetValue(effect, null); }

    static void EnableEffect(object effect, bool value)
    { effect.GetType().GetProperty("FlameEnabled", Hidden).SetValue(effect, value, null); }

    static void CheckOverlayBounds(MainForm form, object overlay)
    {
        Check(!(overlay is Control) && ((Form)Field(overlay, "host")) == form,
            "Flames must be painted in the owner's background without a foreground window.");
    }

    static void CheckPresentedFrame(object overlay)
    {
        var watch = Stopwatch.StartNew();
        Bitmap frame;
        do
        {
            Pump(20); frame = (Bitmap)Field(overlay, "frame");
        }
        while ((frame == null || frame.Size != ((Form)Field(overlay, "host")).ClientSize) && watch.ElapsedMilliseconds < 2500);
        Check(frame != null && frame.Size == ((Form)Field(overlay, "host")).ClientSize, "The background renderer did not present a frame at the current size.");
        int visible = 0;
        for (int y = (int)(frame.Height * .8); y < frame.Height; y += 4)
            for (int x = 0; x < frame.Width; x += 11)
                if (frame.GetPixel(x, y).A > 15) visible++;
        Check(visible > 100, "The presented overlay frame is transparent or has an incorrect crop.");
    }

    static void EditAppearance(MainForm form, Action<AppearanceDialog> edit, bool accept)
    {
        ((Button)Field(form, "btnKeyEnhancementMore")).PerformClick(); Pump(30);
        var menu = (ContextMenuStrip)Field(form, "keyEnhancementMenu");
        var item = (ToolStripMenuItem)menu.Items["enhancement.flames"];
        Check(item != null && item.DropDownItems.Count == 0, "Appearance settings must open a dialog directly.");
        Exception failure = null;
        form.BeginInvoke((Action)delegate {
            AppearanceDialog dialog = null;
            foreach (Form window in Application.OpenForms) if (window is AppearanceDialog) dialog = (AppearanceDialog)window;
            try
            {
                Check(dialog != null && dialog.Visible, "The appearance dialog did not open.");
                edit(dialog);
                dialog.DialogResult = accept ? DialogResult.OK : DialogResult.Cancel;
            }
            catch (Exception ex) { failure = ex; if (dialog != null) dialog.DialogResult = DialogResult.Cancel; }
        });
        item.PerformClick();
        Pump(160);
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    static void CheckBorder(object overlay)
    {
        Size size = ((Form)Field(overlay, "host")).ClientSize;
        using (var first = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb))
        using (var second = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb))
        {
            var paint = overlay.GetType().GetMethod("PaintBorder", BindingFlags.Static | BindingFlags.NonPublic);
            using (var graphics = Graphics.FromImage(first)) paint.Invoke(null, new object[] { graphics, size, 1.7f });
            using (var graphics = Graphics.FromImage(second)) paint.Invoke(null, new object[] { graphics, size, 2.4f });
            int left = 0, right = 0, top = 0, changes = 0; bool clear = true;
            for (int y = 30; y < size.Height - 30; y += 7)
            {
                if (first.GetPixel(2, y).A > 10) left++;
                if (first.GetPixel(size.Width - 3, y).A > 10) right++;
                if (first.GetPixel(4, y) != second.GetPixel(4, y)) changes++;
                if (y >= 55) for (int x = 55; x < size.Width - 55; x += 17) clear &= first.GetPixel(x, y).A == 0;
            }
            for (int x = 30; x < size.Width - 30; x += 7)
                if (first.GetPixel(x, 2).A > 10) top++;
            Check(left > 20 && right > 20 && top > 20, "The border does not carry fire along all three edges.");
            Check(clear, "The border effect covers the editing surface.");
            Check(changes > 10, "Border flames do not animate.");
        }
    }

    static Bitmap ResponsiveFrame(object overlay, Size size, float time, PointF displacement, int seed)
    {
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            overlay.GetType().GetMethod("PaintFlamesWithMotion", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { graphics, size, time, 0f, displacement, seed });
        return bitmap;
    }

    static int MatchingShift(Bitmap reference, Bitmap candidate)
    {
        long bestError = long.MaxValue;
        int bestShift = 0;
        for (int shift = -36; shift <= 36; shift++)
        {
            long error = 0;
            for (int y = (int)(reference.Height * .84); y < reference.Height * .95; y += 3)
                for (int x = 140; x < reference.Width - 140; x += 3)
                {
                    int difference = reference.GetPixel(x, y).A - candidate.GetPixel(x + shift, y).A;
                    error += difference * difference;
                }
            if (error < bestError) { bestError = error; bestShift = shift; }
        }
        return bestShift;
    }

    static void CheckResponsiveFrames(object overlay, string output)
    {
        var size = new Size(1280, 800);
        using (var neutral = ResponsiveFrame(overlay, size, 2.1f, PointF.Empty, 193))
        using (var left = ResponsiveFrame(overlay, size, 2.1f, new PointF(-70, 0), 193))
        using (var right = ResponsiveFrame(overlay, size, 2.1f, new PointF(70, 0), 193))
        using (var up = ResponsiveFrame(overlay, size, 2.1f, new PointF(0, -60), 193))
        using (var down = ResponsiveFrame(overlay, size, 2.1f, new PointF(0, 60), 193))
        using (var anotherSeed = ResponsiveFrame(overlay, size, 2.1f, PointF.Empty, 719))
        using (var wider = ResponsiveFrame(overlay, new Size(1600, 800), 2.1f, PointF.Empty, 193))
        {
            left.Save(Path.Combine(output, "motion-left.png"));
            right.Save(Path.Combine(output, "motion-right.png"));
            up.Save(Path.Combine(output, "motion-up.png"));
            down.Save(Path.Combine(output, "motion-down.png"));
            int leftShift = MatchingShift(neutral, left), rightShift = MatchingShift(neutral, right);
            Check(leftShift < 0 && rightShift > 0,
                "With time and random seed fixed, flame tongues must lean with the motion displacement: " + leftShift + ", " + rightShift);
            int seedChanges = 0, sizeChanges = 0, upChanges = 0, downChanges = 0;
            for (int y = 650; y < 790; y += 3)
                for (int x = 100; x < size.Width - 100; x += 5)
                {
                    if (neutral.GetPixel(x, y) != anotherSeed.GetPixel(x, y)) seedChanges++;
                    if (neutral.GetPixel(x, y) != wider.GetPixel(x, y)) sizeChanges++;
                    if (neutral.GetPixel(x, y) != up.GetPixel(x, y)) upChanges++;
                    if (neutral.GetPixel(x, y) != down.GetPixel(x, y)) downChanges++;
                }
            Check(upChanges > 100 && downChanges > 100,
                "Vertical dragging must affect the bottom flame body, not only the side borders: " + upChanges + "/" + downChanges);
            Check(seedChanges > 100, "Session seeds do not change the flame distribution.");
            Check(sizeChanges > 100, "Resizing only stretches/crops the same flame distribution.");
        }
    }

    static void CheckHeightVariance(object effect)
    {
        var size = new Size(800, 500);
        int lowest = size.Height, highest = 0;
        for (int second = 0; second <= 120; second += 5)
            using (var bitmap = ResponsiveFrame(effect, size, second, PointF.Empty, 193))
            {
                int reach = 0;
                for (int y = size.Height * 2 / 5; y < size.Height; y += 2)
                {
                    bool burning = false;
                    for (int x = 60; x < size.Width - 60; x += 4) burning |= bitmap.GetPixel(x, y).A > 8;
                    if (burning) { reach = size.Height - y; break; }
                }
                lowest = Math.Min(lowest, reach); highest = Math.Max(highest, reach);
            }
        Check(highest >= size.Height * .25 && highest <= size.Height * .30,
            "Default high flames must reach 25-30 percent while remaining inside their envelope: " + highest);
        Check(lowest < size.Height * .20 && highest - lowest > size.Height * .10,
            "Flame heights remain uniformly tall instead of varying between low bodies and occasional peaks: " + lowest + "/" + highest);
    }

    static PointF Displacement(object overlay)
    { return (PointF)overlay.GetType().GetProperty("FlameDisplacement", Hidden).GetValue(overlay, null); }

    static void CheckWindowMotion(MainForm form, object overlay)
    {
        Pump(1000);
        for (int index = 0; index < 8; index++)
        {
            form.Location = new Point(form.Left + 15, form.Top);
            Pump(30);
        }
        var moving = Displacement(overlay);
        Check(moving.X < -1, "Dragging the window right must bend its flames left: " + moving);
        Pump(100);
        var released = Displacement(overlay);
        Check(Math.Abs(released.X) > .1, "Releasing a drag instantly reset the flame instead of preserving inertia.");
        Pump(1800);
        var settled = Displacement(overlay);
        Check(Math.Abs(settled.X) < Math.Abs(moving.X) * .15f + .1f,
            "Flame inertia does not settle after dragging stops: " + settled);
        CheckOverlayBounds(form, overlay);
    }

    static Bitmap ControlShot(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppPArgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        return bitmap;
    }

    static void CheckForegroundLayers(MainForm form, object effect, string output)
    {
        var timer = (System.Windows.Forms.Timer)Field(effect, "animation");
        bool running = timer.Enabled;
        timer.Stop();
        var frameField = effect.GetType().GetField("frame", Hidden);
        var original = (Bitmap)frameField.GetValue(effect);
        var active = effect.GetType().GetProperty("IsActive", Hidden).GetSetMethod(true);
        var bar = (Control)Field(form, "keyEnhancementBar");
        var label = (Control)Field(form, "lblScope");
        var apply = (Control)Field(form, "btnApply");
        using (var fixture = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppPArgb))
        {
            using (var graphics = Graphics.FromImage(fixture)) graphics.Clear(Color.FromArgb(160, 208, 62, 255));
            frameField.SetValue(effect, fixture);
            try
            {
                active.Invoke(effect, new object[] { false });
                using (var plainBar = ControlShot(bar))
                using (var plainLabel = ControlShot(label))
                using (var plainApply = ControlShot(apply))
                {
                    active.Invoke(effect, new object[] { true });
                    using (var flameBar = ControlShot(bar))
                    using (var flameLabel = ControlShot(label))
                    using (var flameApply = ControlShot(apply))
                    {
                        int glyphs = 0, alteredGlyphs = 0, changedBackground = 0, changedButton = 0, opaqueIcon = 0;
                        for (int y = 0; y < label.Height; y++)
                            for (int x = 0; x < label.Width; x++)
                            {
                                var pixel = plainLabel.GetPixel(x, y);
                                if (pixel.ToArgb() == label.ForeColor.ToArgb())
                                { glyphs++; if (pixel != flameLabel.GetPixel(x, y)) alteredGlyphs++; }
                                else if (pixel != flameLabel.GetPixel(x, y)) changedBackground++;
                            }
                        for (int y = 6; y < apply.Height - 6; y++)
                            for (int x = 6; x < apply.Width - 6; x++)
                                if (plainApply.GetPixel(x, y) != flameApply.GetPixel(x, y)) changedButton++;
                        var artwork = typeof(MainForm).Assembly.GetType("PBZKeyForge.EditorArtwork");
                        var icon = (Bitmap)artwork.GetField("KeyEnhancementMark", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                        int mark = Math.Min(bar.Height - (int)(4 * form.DeviceDpi / 96f), (int)(64 * form.DeviceDpi / 96f));
                        int markTop = (bar.Height - mark) / 2;
                        for (int y = 4; y < mark - 4; y++)
                            for (int x = 4; x < mark - 4; x++)
                                if (icon.GetPixel(x * icon.Width / mark, y * icon.Height / mark).A > 250
                                    && plainBar.GetPixel(x, y + markTop) == flameBar.GetPixel(x, y + markTop)) opaqueIcon++;
                        Check(glyphs > 10 && alteredGlyphs == 0 && changedBackground > 100,
                            "Flames must change the label background without tinting its glyphs: " + glyphs + "/" + alteredGlyphs + "/" + changedBackground);
                        Check(changedButton == 0, "The background flame is painted over the Apply button.");
                        Check(opaqueIcon > 100, "The background flame is painted over the puppet artwork.");
                        flameBar.Save(Path.Combine(output, "layer-order-mark.png"));
                        flameLabel.Save(Path.Combine(output, "layer-order-text.png"));
                    }
                }
            }
            finally { frameField.SetValue(effect, original); active.Invoke(effect, new object[] { true }); timer.Enabled = running; }
        }
    }

    static void CaptureFlowFrames(MainForm form, object effect, string output)
    {
        var timer = (System.Windows.Forms.Timer)Field(effect, "animation");
        bool running = timer.Enabled;
        timer.Stop();
        var frameField = effect.GetType().GetField("frame", Hidden);
        var original = (Bitmap)frameField.GetValue(effect);
        try
        {
            foreach (int wind in new[] { 0, -100, 100 })
                using (var frame = ResponsiveFrame(effect, form.ClientSize, 3.4f, new PointF(wind, 0), 193))
                {
                    frameField.SetValue(effect, frame);
                    using (var screenshot = ControlShot(form))
                    {
                        string name = wind == 0 ? "flow-idle" : wind < 0 ? "flow-left" : "flow-right";
                        screenshot.Save(Path.Combine(output, name + ".png"));
                        int cropTop = screenshot.Height * 3 / 4;
                        using (var crop = screenshot.Clone(new Rectangle(0, cropTop, screenshot.Width,
                            screenshot.Height - cropTop), PixelFormat.Format32bppPArgb))
                            crop.Save(Path.Combine(output, name + "-footer.png"));
                    }
                }
        }
        finally { frameField.SetValue(effect, original); timer.Enabled = running; }
    }

    static void CheckActionLayers(MainForm form, object effect, string output)
    {
        var timer = (System.Windows.Forms.Timer)Field(effect, "animation");
        bool running = timer.Enabled; timer.Stop();
        effect.GetType().GetMethod("RetirePendingFrame", Hidden).Invoke(effect, null);
        var frameField = effect.GetType().GetField("frame", Hidden);
        var original = (Bitmap)frameField.GetValue(effect);
        form.ShowActionView(true); form.Refresh();
        var heightField = effect.GetType().GetField("frameHeight", Hidden);
        int originalHeight = (int)heightField.GetValue(effect);
        heightField.SetValue(effect, 100);
        var view = (Control)Field(form, "unifiedActions");
        var grid = (DataGridView)Field(view, "actions");
        var tree = (TreeView)Field(view, "groups");
        using (var fixture = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppPArgb))
        {
            for (int y = 0; y < fixture.Height; y++)
                for (int x = 0; x < fixture.Width; x++) fixture.SetPixel(x, y, Color.FromArgb(50 + x % 160, 30 + y % 180, 190));
            frameField.SetValue(effect, fixture);
            try
            {
                foreach (int firstRow in new[] { 0, 5 })
                {
                    grid.FirstDisplayedScrollingRowIndex = firstRow;
                    using (var screenshot = ControlShot(form))
                    {
                        screenshot.Save(Path.Combine(output, "action-layer-coordinates-" + firstRow + ".png"));
                        foreach (var target in new[] { view, (Control)grid, (Control)tree })
                        {
                            Point local = target == view ? new Point(target.Width / 2, target.Height - 3)
                                : target == tree ? new Point(target.Width / 2, target.Height - 8)
                                : new Point(grid.GetCellDisplayRectangle(0, grid.FirstDisplayedScrollingRowIndex + 1, false).Right - 12,
                                    grid.GetCellDisplayRectangle(0, grid.FirstDisplayedScrollingRowIndex + 1, false).Bottom - 5);
                            Point expected = form.PointToClient(target.PointToScreen(local));
                            Check(screenshot.GetPixel(expected.X, expected.Y) == fixture.GetPixel(expected.X, expected.Y),
                                "Action flame coordinates differ at " + target.GetType().Name + ": " + expected + " after scroll " + firstRow);
                        }
                    }
                }
            }
            finally { frameField.SetValue(effect, original); heightField.SetValue(effect, originalHeight); form.ShowActionView(false); timer.Enabled = running; }
        }
    }

    static Bitmap RenderAppearance(object effect, FlameAppearance settings)
    {
        effect.GetType().GetProperty("Appearance", Hidden).SetValue(effect, settings, null);
        var watch = Stopwatch.StartNew();
        while (Field(effect, "pendingFrame") != null && watch.ElapsedMilliseconds < 3000) Pump(20);
        Check(Field(effect, "pendingFrame") == null, "A flame appearance change did not finish rendering.");
        return (Bitmap)((Bitmap)Field(effect, "frame")).Clone();
    }

    static void CheckPreviewCoalescing()
    {
        int previews = 0; FlameAppearance last = null;
        using (var dialog = new AppearanceDialog(new FlameAppearance(), delegate(FlameAppearance value) { previews++; last = value; }))
        {
            dialog.Show(); Pump(30);
            var slider = (TrackBar)Field(dialog, "speed");
            var watch = Stopwatch.StartNew();
            for (int value = slider.Minimum; value <= slider.Maximum; value++) slider.Value = value;
            watch.Stop();
            Check(previews == 0, "Rapid slider movement starts a render for every intermediate value.");
            Pump(120);
            Check(previews == 1 && last.Speed == 300, "The preview must coalesce intermediate values into the latest setting.");
            Console.WriteLine("Appearance slider: {0} updates in {1} ms; preview callbacks={2}", slider.Maximum - slider.Minimum + 1, watch.ElapsedMilliseconds, previews);
            dialog.Close();
        }
    }

    static void CheckRendererSettings(object effect)
    {
        var clock = (Stopwatch)Field(effect, "elapsed");
        var property = effect.GetType().GetProperty("Appearance", Hidden);
        var original = (FlameAppearance)property.GetValue(effect, null);
        clock.Stop();
        try
        {
            using (var normal = RenderAppearance(effect, new FlameAppearance { Brightness = 100, Transparency = 0 }))
            using (var dark = RenderAppearance(effect, new FlameAppearance { Brightness = 40, Transparency = 0 }))
            using (var faint = RenderAppearance(effect, new FlameAppearance { Brightness = 100, Transparency = 90 }))
            using (var low = RenderAppearance(effect, new FlameAppearance { Height = 10, Transparency = 0 }))
            using (var shifted = RenderAppearance(effect, new FlameAppearance { Hue = 120, Transparency = 0 }))
            {
                long normalLight = 0, darkLight = 0; int opaqueDifference = 0, faintMax = 0, lowTop = low.Height;
                for (int y = 55; y < normal.Height; y += 3)
                    for (int x = 55; x < normal.Width - 55; x += 5)
                    {
                        var a = normal.GetPixel(x, y); var b = dark.GetPixel(x, y); var c = faint.GetPixel(x, y);
                        normalLight += (a.R + a.G + a.B) * a.A; darkLight += (b.R + b.G + b.B) * b.A;
                        opaqueDifference = Math.Max(opaqueDifference, Math.Abs(a.A - b.A)); faintMax = Math.Max(faintMax, c.A);
                        if (low.GetPixel(x, y).A > 8) lowTop = Math.Min(lowTop, y);
                    }
                Check(darkLight < normalLight * .6 && opaqueDifference <= 1,
                    "Brightness must change flame luminance while preserving its transparency.");
                Check(faintMax > 0 && faintMax <= 26, "The transparency control does not affect the rendered flame alpha.");
                Check(lowTop >= low.Height * .90 && lowTop < low.Height,
                    "The rendered bottom flame exceeds the selected 10 percent maximum height.");
                int hueChanges = 0;
                for (int y = normal.Height * 4 / 5; y < normal.Height; y += 4)
                    for (int x = 55; x < normal.Width - 55; x += 7)
                    {
                        var a = normal.GetPixel(x, y); var b = shifted.GetPixel(x, y);
                        if (a.A > 40 && a.A == b.A && Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) > 30) hueChanges++;
                    }
                Check(hueChanges > 50, "Hue must change flame color independently from opacity.");
            }
        }
        finally { property.SetValue(effect, original, null); clock.Start(); }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Pass a screenshot output directory.");
            string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            var preferences = new EditorPreferences(Path.Combine(output, "preferences.txt"));
            Check(preferences.LoadFlameEffects(), "Flame effects should default on.");
            Application.EnableVisualStyles();
            CheckPreviewCoalescing();
            object overlay = null;
            using (var form = new MainForm(preferences))
            {
                typeof(MainForm).GetProperty("Session", Hidden).GetSetMethod(true)
                    .Invoke(form, new object[] { EditorSession.CreateOffline() });
                I18n.Current = "zh-Hans";
                form.RenderSession();
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(80, 70);
                Exception failure = null;
                form.Shown += delegate { form.BeginInvoke((Action)delegate {
                    try
                    {
                        Pump(200); form.Activate(); Pump(100);
                        var bar = (Control)Field(form, "keyEnhancementBar");
                        var toggle = (CheckBox)Field(bar, "Toggle");
                        overlay = Field(form, "keyEnhancementFlame");
                        Check(!Active(overlay), "Disabled key enhancement shows an overlay.");
                        Check(toggle.Enabled && form.Session.IsOffline && Field(form, "keyEnhancementInstallation") == null,
                            "The effect fixture must not have an installation target.");
                        var activeBefore = Form.ActiveForm;
                        var disabledMark = MarkShot(bar);
                        toggle.Checked = true; Pump(200);
                        CheckMark(bar, disabledMark);
                        Check(Active(overlay), "The draft switch did not show flames before installation.");
                        Check(!(overlay is Control) && Form.ActiveForm == activeBefore,
                            "Background flames must not create a window or change focus.");
                        CheckOverlayBounds(form, overlay);
                        CheckPresentedFrame(overlay);
                        CheckFooter(form);
                        CheckForegroundLayers(form, overlay, output);
                        CaptureFlowFrames(form, overlay, output);
                        CheckActionLayers(form, overlay, output);
                        Shot(form, overlay, output, "draft-on");
                        CheckFrame(overlay); CheckBorder(overlay); CheckResponsiveFrames(overlay, output);
                        CheckHeightVariance(overlay);
                        CheckRendererSettings(overlay);
                        CheckWindowMotion(form, overlay);
                        string configuration = ((KeyEnhancementConfiguration)Field(form, "keyEnhancement")).ToJson();
                        EditAppearance(form, delegate(AppearanceDialog dialog) {
                            Check(dialog.Value.Enabled && dialog.Value.Speed == 75 && dialog.Value.Height == 30,
                                "The flame defaults must be slower and capped at 30 percent.");
                            ((CheckBox)Field(dialog, "enabled")).Checked = false;
                            Check(!((TrackBar)Field(dialog, "height")).Enabled, "Disabled effects leave their sliders active.");
                        }, true);
                        Check(!Active(overlay) && toggle.Checked, "Disabling effects must leave key enhancement enabled.");
                        Check(!new EditorPreferences(Path.Combine(output, "preferences.txt")).LoadFlameEffects(), "The disabled effect preference was not saved.");
                        Check(configuration == ((KeyEnhancementConfiguration)Field(form, "keyEnhancement")).ToJson() && !form.Session.HasChanges,
                            "An appearance preference changed bindings or Mod configuration.");
                        Shot(form, overlay, output, "effects-off");
                        EditAppearance(form, delegate(AppearanceDialog dialog) {
                            Check(!dialog.Value.Enabled, "Reopening settings lost the disabled state.");
                            ((CheckBox)Field(dialog, "enabled")).Checked = true;
                            ((TrackBar)Field(dialog, "height")).Value = 100;
                            int progress = 0;
                            EventHandler onFrame = delegate { progress++; };
                            var frameEvent = overlay.GetType().GetEvent("FrameChanged", Hidden);
                            frameEvent.GetAddMethod(true).Invoke(overlay, new object[] { onFrame });
                            var dragging = Stopwatch.StartNew();
                            try
                            {
                                for (int step = 0; step < 24; step++)
                                {
                                    ((TrackBar)Field(dialog, "hue")).Value = -180 + step * 15;
                                    Pump(25);
                                }
                            }
                            finally { frameEvent.GetRemoveMethod(true).Invoke(overlay, new object[] { onFrame }); }
                            Check(progress > 1, "Continuous preview changes starve completed flame frames.");
                            Console.WriteLine("Continuous preview: {0} presented frames during {1} ms of slider movement", progress, dragging.ElapsedMilliseconds);
                            ((TrackBar)Field(dialog, "speed")).Value = 50;
                            ((TrackBar)Field(dialog, "height")).Value = 20;
                            ((TrackBar)Field(dialog, "brightness")).Value = 140;
                            ((TrackBar)Field(dialog, "transparency")).Value = 45;
                            ((TrackBar)Field(dialog, "hue")).Value = 60;
                            Pump(100);
                            Check(Active(overlay) && ((FlameAppearance)overlay.GetType().GetProperty("Appearance", Hidden).GetValue(overlay, null)).Height == 20,
                                "Dialog changes do not preview in the flame renderer.");
                            using (var shot = ControlShot(dialog)) shot.Save(Path.Combine(output, "appearance-dialog.png"));
                        }, true);
                        var saved = preferences.LoadFlameAppearance();
                        Check(saved.Speed == 50 && saved.Height == 20 && saved.Brightness == 140 && saved.Transparency == 45 && saved.Hue == 60,
                            "Appearance controls did not persist all four adjustments.");
                        EditAppearance(form, delegate(AppearanceDialog dialog) {
                            var sweep = Stopwatch.StartNew();
                            for (int speed = 5; speed <= 300; speed++) ((TrackBar)Field(dialog, "speed")).Value = speed;
                            sweep.Stop(); Console.WriteLine("Live appearance slider sweep: {0} ms", sweep.ElapsedMilliseconds);
                            ((TrackBar)Field(dialog, "height")).Value = 60;
                            ((TrackBar)Field(dialog, "hue")).Value = 0;
                            ((TrackBar)Field(dialog, "speed")).Value = 75;
                            ((TrackBar)Field(dialog, "brightness")).Value = 100;
                            ((TrackBar)Field(dialog, "transparency")).Value = 15;
                            ((TrackBar)Field(dialog, "speed")).Value = 150;
                        }, false);
                        Check(preferences.LoadFlameAppearance().Height == 20
                            && ((FlameAppearance)overlay.GetType().GetProperty("Appearance", Hidden).GetValue(overlay, null)).Height == 20,
                            "Cancel must restore the preview without changing saved preferences.");
                        EditAppearance(form, delegate(AppearanceDialog dialog) {
                            ((TrackBar)Field(dialog, "height")).Value = 60;
                            using (var shot = ControlShot(dialog)) shot.Save(Path.Combine(output, "appearance-expanded.png"));
                        }, true);
                        Check(Active(overlay) && preferences.LoadFlameEffects(), "Re-enabling effects did not resume flames after closing the dialog.");
                        CheckPresentedFrame(overlay);
                        form.ShowActionView(true); Pump(300);
                        Shot(form, overlay, output, "actions-flames");
                        var liveGrid = (DataGridView)Field(Field(form, "unifiedActions"), "actions");
                        liveGrid.FirstDisplayedScrollingRowIndex = 5;
                        Shot(form, overlay, output, "actions-flames-scrolled");
                        form.ShowActionView(false);
                        int previousTick = (int)Field(overlay, "tick"); Pump(150);
                        Check((int)Field(overlay, "tick") > previousTick, "The animation timer is stopped.");
                        form.Location = new Point(110, 90); form.Size = new Size(1000, 740); Pump(100);
                        CheckOverlayBounds(form, overlay);
                        CheckPresentedFrame(overlay);
                        CheckFooter(form);
                        Shot(form, overlay, output, "minimum-on");
                        toggle.Checked = false;
                        Check(!Active(overlay), "Disabling the draft did not hide flames immediately.");
                        Check(Field(overlay, "pendingFrame") == null, "Disabling flames retained a pending frame.");
                        previousTick = (int)Field(overlay, "tick"); Pump(100);
                        Check((int)Field(overlay, "tick") == previousTick, "Disabled flames still animate.");
                        Shot(form, overlay, output, "draft-off");
                        toggle.Checked = true; Pump(100);
                        toggle.Checked = false; toggle.Checked = true; Pump(100);
                        Check(Active(overlay), "Rapid effect toggles lost the visible simulation.");
                        form.WindowState = FormWindowState.Minimized; Pump(100);
                        Check(!Active(overlay), "Minimizing the host left flames visible.");
                        form.WindowState = FormWindowState.Normal; form.Activate(); Pump(100);
                        Check(Active(overlay), "Restoring the host did not restore flames.");
                        using (var other = new Form { ShowInTaskbar = false, Size = new Size(100, 100),
                            Location = new Point(20, 20), StartPosition = FormStartPosition.Manual })
                        {
                            other.Show(); other.Activate(); Pump(100);
                            Check(Active(overlay) && !(overlay is Control), "Unfocused flames must stay within their background painter.");
                            previousTick = (int)Field(overlay, "tick"); Pump(80);
                            Check((int)Field(overlay, "tick") > previousTick, "Unfocused flames stopped animating.");
                            Check(Form.ActiveForm == other, "Continuing the effect stole focus from the other window.");
                            Shot(form, overlay, output, "unfocused-on");
                            other.Close();
                        }
                        form.Activate(); Pump(100);
                        Check(Active(overlay), "Reactivating the host did not restore flames.");
                    }
                    catch (Exception ex) { failure = ex; }
                    finally
                    {
                        if (overlay != null) EnableEffect(overlay, false);
                        form.Hide(); Application.ExitThread();
                    }
                }); };
                Application.Run(form);
                if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            Check(overlay != null && (bool)overlay.GetType().GetProperty("IsDisposed", Hidden).GetValue(overlay, null), "Disposing the host did not dispose its overlay.");
            preferences.SavePresetMode(PresetApplyMode.Replace); preferences.SaveFlameEffects(false);
            using (var reopened = new MainForm(new EditorPreferences(Path.Combine(output, "preferences.txt"))))
            {
                Check(!(bool)Field(reopened, "flameEffectsEnabled"), "A new window ignored the disabled effect preference.");
                Check(preferences.LoadPresetMode() == PresetApplyMode.Replace, "Saving the visual preference changed the preset mode.");
            }
            Console.WriteLine("KeyEnhancementFlameOverlayTests PASS: " + checks + " assertions; screenshots=" + output);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
