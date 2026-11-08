using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class KeyEnhancementFlameOverlay : System.ComponentModel.Component, IMessageFilter
    {
        readonly Form host;
        readonly Timer animation = new Timer { Interval = 40 };
        readonly Stopwatch elapsed = Stopwatch.StartNew();
        readonly object renderGate = new object();
        readonly FlameMotion motion = new FlameMotion();
        readonly int sessionSeed = new Random().Next(1, 1048576);
        Bitmap frame;
        FireScene scene;
        Task<Bitmap> pendingFrame;
        volatile int renderVersion;
        int pendingVersion, pendingHeight, frameHeight = 30;
        bool flameEnabled;
        bool animationPaused;
        bool applicationActive = true;
        int interactionUntil = Environment.TickCount;
        bool filteringMessages;
        internal bool ApplicationActive
        {
            get { return applicationActive; }
            set { if (applicationActive == value) return; applicationActive = value; Synchronize(this, EventArgs.Empty); }
        }
        internal bool AnimationPaused
        {
            get { return animationPaused; }
            set { if (animationPaused == value) return; animationPaused = value; Synchronize(this, EventArgs.Empty); }
        }
        int tick, pulse;
        float frameStartedAt = -1;
        float simulationTime, simulationUpdatedAt;
        FlameAppearance appearance = new FlameAppearance();
        internal FlameAppearance Appearance
        {
            get { return appearance.Clone(); }
            set
            {
                AdvanceSimulation((float)elapsed.Elapsed.TotalSeconds);
                appearance = value.Clone(); appearance.Clamp();
                frameStartedAt = -1;
                Synchronize(this, EventArgs.Empty);
            }
        }

        void AdvanceSimulation(float time)
        {
            simulationTime += Math.Max(0, time - simulationUpdatedAt) * appearance.Speed / 100f;
            simulationUpdatedAt = time;
        }

        internal PointF FlameDisplacement { get { return motion.Displacement; } }
        internal Size Size { get { return host.ClientSize; } }
        internal int PaintTop { get { return Size.Height * (100 - frameHeight) / 100; } }
        internal bool IsActive { get; private set; }
        internal bool IsDisposed { get; private set; }
        internal bool PaintSuspended { get; set; }
        internal bool InteractionPaused
        { get { return flameEnabled && !IsDisposed && unchecked(Environment.TickCount - interactionUntil) < 0; } }
        internal event EventHandler FrameChanged;

        public bool FlameEnabled
        {
            get { return flameEnabled; }
            set { if (flameEnabled == value) return; flameEnabled = value; Synchronize(this, EventArgs.Empty); }
        }

        public KeyEnhancementFlameOverlay(Form owner)
        {
            host = owner;
            host.LocationChanged += Synchronize;
            host.SizeChanged += Synchronize;
            host.VisibleChanged += Synchronize;
            host.Activated += Synchronize;
            host.EnabledChanged += Synchronize;
            host.Disposed += HostDisposed;
            Application.AddMessageFilter(this); filteringMessages = true;
            animation.Tick += delegate {
                tick++; if (pulse > 0) pulse--; RenderFrame();
            };
        }

        public bool PreFilterMessage(ref Message message)
        {
            int id = message.Msg;
            if (id >= 0x0100 && id <= 0x0109 || id >= 0x0200 && id <= 0x020E || id == 0x0114 || id == 0x0115)
                interactionUntil = Environment.TickCount + 220;
            return false;
        }

        void HostDisposed(object sender, EventArgs e) { Dispose(); }

        void Synchronize(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            motion.Sample(host.Location, (float)elapsed.Elapsed.TotalSeconds);
            if (!flameEnabled || !host.Visible || host.WindowState == FormWindowState.Minimized)
            { animation.Stop(); RetirePendingFrame(); IsActive = false; NotifyFrameChanged(); return; }
            IsActive = true;
            if (animationPaused || !host.Enabled || !applicationActive)
            { animation.Stop(); RetirePendingFrame(); return; }
            RenderFrame();
            animation.Start();
        }

        public void Celebrate() { pulse = 20; }

        void PresentFrame()
        {
            if (pendingFrame == null || !pendingFrame.IsCompleted) return;
            var completed = pendingFrame.GetAwaiter().GetResult(); pendingFrame = null;
            if (completed == null) return;
            if (pendingVersion == renderVersion && completed.Size == Size)
            {
                if (frame != null) frame.Dispose();
                frameHeight = pendingHeight;
                frame = completed; NotifyFrameChanged();
            }
            else completed.Dispose();
        }

        void RenderFrame()
        {
            if (Size.Width < 1 || Size.Height < 1) return;
            float time = (float)elapsed.Elapsed.TotalSeconds;
            AdvanceSimulation(time);
            motion.AdvanceTo(time);
            PresentFrame();
            if (pendingFrame != null) return;
            Size dimensions = Size;
            float strength = pulse / 20f;
            if (time - frameStartedAt < .035f) return;
            frameStartedAt = time;
            PointF displacement = motion.Displacement;
            float sceneTime = simulationTime;
            var settings = appearance.Clone();
            int version = renderVersion;
            pendingVersion = version; pendingHeight = settings.Height;
            pendingFrame = Task.Run(delegate
            {
                if (version != renderVersion) return null;
                var bitmap = new Bitmap(dimensions.Width, dimensions.Height, PixelFormat.Format32bppPArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    lock (renderGate)
                    {
                        if (version != renderVersion) { bitmap.Dispose(); return null; }
                        if (scene == null || scene.Size != dimensions || scene.HeightPercent != settings.Height)
                            scene = new FireScene(dimensions, sceneTime, sessionSeed, settings.Height);
                        scene.AdvanceTo(sceneTime, displacement);
                        scene.Paint(graphics, strength);
                    }
                }
                if (settings.Brightness == 100 && settings.Transparency == 0 && settings.Hue == 0) return bitmap;
                var adjusted = new Bitmap(dimensions.Width, dimensions.Height, PixelFormat.Format32bppPArgb);
                using (bitmap)
                using (var graphics = Graphics.FromImage(adjusted))
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(AppearanceMatrix(settings));
                    int bottomTop = dimensions.Height * (100 - settings.Height) / 100;
                    int rim = Math.Min(50, bottomTop);
                    AdjustRegion(graphics, bitmap, attributes, new Rectangle(0, bottomTop, dimensions.Width, dimensions.Height - bottomTop));
                    AdjustRegion(graphics, bitmap, attributes, new Rectangle(0, 0, dimensions.Width, rim));
                    int middleHeight = bottomTop - rim;
                    int side = Math.Min(50, dimensions.Width / 2);
                    AdjustRegion(graphics, bitmap, attributes, new Rectangle(0, rim, side, middleHeight));
                    AdjustRegion(graphics, bitmap, attributes, new Rectangle(dimensions.Width - side, rim, side, middleHeight));
                }
                return adjusted;
            });
        }

        static void AdjustRegion(Graphics graphics, Bitmap source, ImageAttributes attributes, Rectangle bounds)
        {
            if (bounds.Width > 0 && bounds.Height > 0)
                graphics.DrawImage(source, bounds, bounds.X, bounds.Y, bounds.Width, bounds.Height, GraphicsUnit.Pixel, attributes);
        }

        static ColorMatrix AppearanceMatrix(FlameAppearance settings)
        {
            float c = (float)Math.Cos(settings.Hue * Math.PI / 180), s = (float)Math.Sin(settings.Hue * Math.PI / 180);
            float b = settings.Brightness / 100f;
            return new ColorMatrix(new[] {
                new[] { (.213f + c * .787f - s * .213f) * b, (.213f - c * .213f + s * .143f) * b, (.213f - c * .213f - s * .787f) * b, 0f, 0f },
                new[] { (.715f - c * .715f - s * .715f) * b, (.715f + c * .285f + s * .140f) * b, (.715f - c * .715f + s * .715f) * b, 0f, 0f },
                new[] { (.072f - c * .072f + s * .928f) * b, (.072f - c * .072f - s * .283f) * b, (.072f + c * .928f + s * .072f) * b, 0f, 0f },
                new[] { 0f, 0f, 0f, 1 - settings.Transparency / 100f, 0f }, new[] { 0f, 0f, 0f, 0f, 1f }
            });
        }

        void RetirePendingFrame()
        {
            renderVersion++;
            var retired = pendingFrame; pendingFrame = null;
            if (retired != null) retired.ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion && task.Result != null) task.Result.Dispose();
            }, TaskScheduler.Default);
        }

        internal static void PaintFlames(Graphics graphics, Size size, float time, float pulse)
        {
            var snapshot = new FireScene(size, time, 7);
            snapshot.Paint(graphics, pulse);
        }

        internal static void PaintFlamesWithMotion(Graphics graphics, Size size, float time, float pulse,
            PointF displacement, int seed)
        {
            var snapshot = new FireScene(size, time, seed);
            snapshot.AdvanceTo(time, displacement);
            snapshot.Paint(graphics, pulse);
        }

        internal static void PaintBorder(Graphics graphics, Size size, float time)
        {
            var snapshot = new FireScene(size, time, 7);
            snapshot.PaintEdges(graphics, 0);
        }

        sealed class FlameMotion
        {
            Point previous;
            float sampledAt, updatedAt, velocityX, velocityY, leanX, leanY, speedX, speedY, targetX, targetY;
            bool initialized;
            public PointF Displacement { get { return new PointF(leanX, leanY); } }

            public void Sample(Point location, float time)
            {
                if (!initialized)
                { previous = location; sampledAt = updatedAt = time; initialized = true; return; }
                if (location == previous) return;
                float dt = Math.Max(.012f, Math.Min(.15f, time - sampledAt));
                float vx = (location.X - previous.X) / dt, vy = (location.Y - previous.Y) / dt;
                float ax = (vx - velocityX) / dt, ay = (vy - velocityY) / dt;
                targetX = Limit(-vx * .15f - ax * .0016f, 260);
                targetY = Limit(-vy * .09f - ay * .0010f, 150);
                velocityX = vx; velocityY = vy; previous = location; sampledAt = time;
            }

            public void AdvanceTo(float time)
            {
                if (!initialized) return;
                float remaining = Math.Min(.2f, Math.Max(0, time - updatedAt));
                while (remaining > 0)
                {
                    float dt = Math.Min(.016f, remaining);
                    speedX += ((targetX - leanX) * 260 - speedX * 18) * dt;
                    speedY += ((targetY - leanY) * 260 - speedY * 18) * dt;
                    leanX += speedX * dt; leanY += speedY * dt;
                    float decay = (float)Math.Exp(-dt * 5.5f);
                    targetX *= decay; targetY *= decay;
                    remaining -= dt;
                }
                updatedAt = time;
            }

            static float Limit(float value, float maximum) { return Math.Max(-maximum, Math.Min(maximum, value)); }
        }

        // Fuel clusters feed a transported reaction field. Flame silhouettes, folds
        // and transparent pockets arise from that field rather than rooted blades.
        sealed class FlameField
        {
            static readonly ParallelOptions workers = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)) };
            static readonly float[] lattice = MakeLattice();
            readonly int width, height, seed;
            readonly float length, depth, scaleDepth, pixel;
            readonly int[] composite, pixels;
            readonly float[] fuelHeight, supply, localFlow, streamCenter;
            readonly System.Collections.Generic.Dictionary<int, FluxDynamics> currents
                = new System.Collections.Generic.Dictionary<int, FluxDynamics>();
            readonly PointF[] windHistory = new PointF[96];
            readonly float[] windTimes = new float[96];
            int windCount, windHead;

            public FlameField(int length, int depth, int seed)
                : this(length, depth, seed, depth) { }

            public FlameField(int length, int depth, int seed, int scaleDepth)
            {
                this.length = length; this.depth = depth; this.seed = seed;
                this.scaleDepth = scaleDepth;
                // Sample the decorative field at a bounded resolution, then interpolate
                // it to the window so DPI and maximization do not multiply its work.
                float sampling = Math.Min(.8f, Math.Min(640f / length, 240f / depth));
                width = Math.Max(2, (int)Math.Ceiling(length * sampling));
                height = Math.Max(2, (int)Math.Ceiling(depth * sampling));
                pixel = length / (float)width;
                composite = new int[width * height];
                pixels = new int[width * height];
                fuelHeight = new float[width];
                supply = new float[width];
                localFlow = new float[width];
                streamCenter = new float[width];
            }

            public Bitmap Image(float time, float opacity, float windX, float windY)
            {
                RememberWind(time, new PointF(windX, windY));
                Array.Clear(composite, 0, composite.Length);
                for (int layer = 0; layer < 3; layer++)
                {
                    int[] pixels = RenderLayer(time, opacity, windX, windY, layer);
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        int source = pixels[index], previous = composite[index];
                        if (source == 0) continue;
                        int inverse = 255 - (int)((uint)source >> 24);
                        int alpha = (int)((uint)source >> 24) + (int)((uint)previous >> 24) * inverse / 255;
                        int red = ((source >> 16) & 255) + ((previous >> 16) & 255) * inverse / 255;
                        int green = ((source >> 8) & 255) + ((previous >> 8) & 255) * inverse / 255;
                        int blue = (source & 255) + (previous & 255) * inverse / 255;
                        composite[index] = (alpha << 24) | (red << 16) | (green << 8) | blue;
                    }
                }
                var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try { Marshal.Copy(composite, 0, data.Scan0, composite.Length); }
                finally { bitmap.UnlockBits(data); }
                return bitmap;
            }

            int[] RenderLayer(float time, float opacity, float windX, float windY, int layer)
            {
                int layerSeed = seed + layer * 9341;
                float speed = layer == 0 ? .78f : layer == 1 ? 1 : 1.19f;
                float heightFactor = layer == 0 ? 1.12f : layer == 1 ? 1 : .72f;
                float widthFactor = layer == 0 ? 1.35f : layer == 1 ? 1 : .75f;
                float opacityFactor = layer == 0 ? .34f : layer == 1 ? .84f : .62f;
                Array.Clear(localFlow, 0, localFlow.Length);
                for (int x = 0; x < width; x++)
                {
                    float baseFuel = Field(x * pixel / Math.Max(18, scaleDepth) * 3 + 19 + layer * 17,
                        time * .24f * speed + 37);
                    fuelHeight[x] = scaleDepth * (.16f + baseFuel * .13f);
                    supply[x] = .28f + baseFuel * .12f;
                    streamCenter[x] = x * supply[x];
                }
                float spacing = Math.Max(24, scaleDepth * (.47f + layer * .07f));
                int clusters = (int)(length / spacing) + 3;
                for (int index = -1; index < clusters; index++)
                {
                    int lane = index + layerSeed;
                    float phase = time * (.10f + Hash(lane, 11) * .08f) * speed + Hash(lane, 23);
                    int generation = (int)Math.Floor(phase);
                    float age = phase - generation;
                    float life = Smooth(0, .17f, age) * (1 - Smooth(.66f, 1, age));
                    float power = Hash(lane + generation * 193, 41);
                    float center = (index + .15f + Hash(lane, 53) * .70f) * spacing;
                    float reach = scaleDepth * (.35f + power * .70f) * (.72f + life * .28f) * heightFactor;
                    float spread = scaleDepth * (.20f + power * .30f) * (.70f + life * .30f) * widthFactor;
                    Feed(fuelHeight, supply, localFlow, streamCenter, center, reach, spread,
                        (.22f + power * .80f) * life, layer * 10000 + index + 1, time, windX);
                    float eventPhase = time * (.045f + Hash(lane, 151) * .040f) + Hash(lane, 157);
                    int eventGeneration = (int)Math.Floor(eventPhase);
                    float eventAge = eventPhase - eventGeneration;
                    float eventPower = Hash(layerSeed + index * 29 + eventGeneration * 179, 163);
                    float burst = Smooth(.78f, .98f, eventPower) * Smooth(.34f, .44f, eventAge)
                        * (1 - Smooth(.50f, .62f, eventAge));
                    if (burst > 0)
                        Feed(fuelHeight, supply, localFlow, streamCenter, center, scaleDepth * .65f
                            + burst * Math.Max(0, depth * .91f - scaleDepth * .65f),
                            scaleDepth * (.42f + eventPower * .22f), burst * 1.4f,
                            layer * 10000 + clusters + index + 3, time, windX);
                }
                Array.Clear(pixels, 0, pixels.Length);
                float maximumReach = 0;
                for (int x = 0; x < width; x++)
                {
                    maximumReach = Math.Max(maximumReach, fuelHeight[x]);
                    streamCenter[x] /= supply[x];
                }
                float xSeed = (layerSeed & 127) + 128.23f, ySeed = ((layerSeed >> 7) & 127) + 128.71f;
                Parallel.For(0, height, workers, row =>
                {
                    float rise = depth * (1 - row / (float)(height - 1));
                    float level = rise / Math.Max(16, scaleDepth);
                    PointF transportedWind = WindAt(time - level * (.15f + layer * .065f));
                    float entrainment = 1 - (float)Math.Exp(-level * 1.5f);
                    float drag = transportedWind.X * entrainment;
                    float transportedRise = Math.Max(0, rise + transportedWind.Y * entrainment * .45f);
                    if (transportedRise > maximumReach * 1.3f + scaleDepth * .12f || rise >= depth - 7) return;
                    for (int x = 0; x < width; x++)
                    {
                        float position = x * pixel;
                        float profileX = x - drag * .42f / pixel;
                        float crownHeight = Profile(fuelHeight, profileX);
                        if (transportedRise > crownHeight * 1.3f + scaleDepth * .12f) continue;
                        float upperFlow = Smooth(.70f, 1.18f, transportedRise / Math.Max(1, crownHeight));
                        float materialX = profileX, jacobian = 1;
                        // Reverse advection narrows the boundary and internal material together at the tip.
                        if (upperFlow > 0)
                            for (int step = 0; step < 4; step++)
                            {
                                float center = Profile(streamCenter, materialX);
                                float midpoint = materialX + (materialX - center) * upperFlow * .14f;
                                float slope = Profile(streamCenter, midpoint + .5f) - Profile(streamCenter, midpoint - .5f);
                                materialX += (midpoint - Profile(streamCenter, midpoint)) * upperFlow * .28f;
                                jacobian *= Math.Max(.1f, 1 + (1 - slope) * upperFlow * .28f);
                            }
                        float materialRise = transportedRise - crownHeight * upperFlow * .12f;
                        position += (materialX - profileX) * pixel;
                        profileX = materialX;
                        float reach = Profile(fuelHeight, profileX);
                        if (transportedRise > reach * 1.3f + scaleDepth * .12f || rise >= depth - 7) continue;
                        float fuel = Profile(supply, profileX);
                        float feed = Math.Min(1.8f, fuel);
                        float flow = Profile(localFlow, profileX) / Math.Max(.30f, fuel);
                        float windOffset = drag * .30f + flow * entrainment * .35f;
                        float px = (position - windOffset) / Math.Max(14, scaleDepth) * 5.1f + xSeed;
                        float py = materialRise / Math.Max(16, scaleDepth) * 1.8f - time * .72f * speed + ySeed;
                        float slow = Field(px * .39f + 17, py * .61f + 31);
                        float cross = Field(px * .73f + 53, py * .84f + 11);
                        float warpX = px + (slow - .5f) * 2.4f + (cross - .5f) * .80f;
                        float warpY = py + (slow - .5f) * .65f;
                        float large = Field(warpX, warpY);
                        float fine = Field(warpX * 2.09f + 41, warpY * 1.71f + 67);
                        float detail = Field(warpX * 3.77f + warpY * .38f + 71,
                            warpY * 2.63f - warpX * .31f + 29);
                        float structure = large * .58f + fine * .27f + detail * .15f;
                        float holes = Field(warpX * 1.11f + 83, warpY * .91f + 97);
                        float normalizedHeight = transportedRise / Math.Max(1, reach);
                        float cooling = 1 - Smooth(.78f + slow * .11f, 1.22f + large * .10f, normalizedHeight);
                        float reaction = structure;
                        float threshold = .515f;
                        float front = 1 - Smooth(.013f, .077f, Math.Abs(reaction - threshold));
                        front *= .45f + Smooth(threshold - .025f, threshold + .04f, reaction) * .55f;
                        float volume = Smooth(threshold - .22f, threshold + .13f, reaction);
                        float opening = Smooth(.22f, .49f, holes);
                        float tearing = 1 - Smooth(.78f, 1.20f, normalizedHeight + (holes - .5f) * .5f);
                        float peak = Smooth(.39f, .68f, structure)
                            * (1 - Smooth(.92f, 1.23f, normalizedHeight));
                        float sheet = front * opening * cooling * (.48f + peak * .52f);
                        float body = volume * opening * cooling * tearing;
                        float interior = (1 - Smooth(.015f, .085f, Math.Abs(fine - .51f))) * body;
                        float filamentField = Field(warpX * 6.71f + fine * 1.2f + 19,
                            warpY * 3.23f + large * .6f + 43);
                        float footprint = Math.Max(1, jacobian * pixel / Math.Max(14, scaleDepth) * 5.1f * 6.71f / .65f);
                        float microFront = (1 - Smooth(.007f * footprint, .038f * footprint,
                            Math.Abs(filamentField - .51f))) / footprint;
                        float micro = microFront * body * (.40f + fine * .60f);
                        float rearField = Field(px * .79f + slow * .8f + 73, py * .83f + 103);
                        float rear = Smooth(.35f, .65f, rearField)
                            * (1 - Smooth(.63f, 1.25f, normalizedHeight)) * opening;
                        float rooted = 1 - Smooth(.04f, .19f, level);
                        float basal = rooted * (.11f + slow * .09f);
                        float hotField = Field(warpX * .69f + 107, warpY * .79f + 13);
                        float heat = Smooth(.47f, .76f, hotField) * sheet
                            * (1 - Smooth(.15f, .95f, normalizedHeight));
                        float violet = body * .27f + sheet * .23f + interior * .12f + micro * .075f;
                        float emission = (violet * (.50f + feed * .72f) + basal + heat * (layer == 2 ? 1.65f : 1.15f))
                            * opacity * opacityFactor;
                        emission += rear * .075f * opacity * opacityFactor;
                        float upperBlank = 1 - Smooth(depth - 14, depth - 7, rise);
                        int alpha = (int)(Math.Min(.85f, emission * upperBlank) * 255);
                        if (alpha == 0) continue;
                        float light = Math.Min(1, sheet * .38f + interior * .24f + heat * .80f + micro * .28f)
                            * (layer == 0 ? .35f : 1);
                        int red = (int)(154 + light * 100), green = (int)(45 + light * 185);
                        pixels[row * width + x] = (alpha << 24) | (red * alpha / 255 << 16)
                            | (green * alpha / 255 << 8) | alpha;
                    }
                });
                return pixels;
            }

            void Feed(float[] reach, float[] supply, float[] flow, float[] streamCenter, float center, float height,
                float spread, float strength, int slot, float time, float wind)
            {
                FluxDynamics current;
                if (!currents.TryGetValue(slot, out current))
                {
                    current = new FluxDynamics(wind, time, seed + slot * 71);
                    currents.Add(slot, current);
                }
                current.Advance(wind, time, height / Math.Max(1, scaleDepth));
                float tipCenter = (center + (Noise(slot + seed, time * .17f) - .5f) * scaleDepth * .12f) / pixel;
                int from = Math.Max(0, (int)((center - spread * 2) / pixel));
                int to = Math.Min(width - 1, (int)((center + spread * 2) / pixel) + 1);
                for (int x = from; x <= to; x++)
                {
                    float distance = (x * pixel - center) / Math.Max(1, spread);
                    float weight = (float)Math.Exp(-distance * distance * 1.65f);
                    float peakHeight = height * (.65f + strength * .35f);
                    float candidate = peakHeight * (float)Math.Sqrt(weight);
                    reach[x] = Math.Max(reach[x], candidate);
                    streamCenter[x] += weight * strength * tipCenter;
                    supply[x] += weight * strength;
                    flow[x] += weight * strength * current.Lean;
                }
            }

            void RememberWind(float time, PointF wind)
            {
                if (windCount > 0 && time <= windTimes[(windHead + 95) % 96]) return;
                windTimes[windHead] = time; windHistory[windHead] = wind;
                windHead = (windHead + 1) % 96; windCount = Math.Min(96, windCount + 1);
            }

            PointF WindAt(float time)
            {
                if (windCount == 0) return PointF.Empty;
                int latest = (windHead + 95) % 96;
                for (int count = 1; count < windCount; count++)
                {
                    int older = (latest + 95) % 96;
                    if (windTimes[older] <= time)
                    {
                        float mix = Math.Max(0, Math.Min(1, (time - windTimes[older])
                            / Math.Max(.001f, windTimes[latest] - windTimes[older])));
                        return new PointF(windHistory[older].X + (windHistory[latest].X - windHistory[older].X) * mix,
                            windHistory[older].Y + (windHistory[latest].Y - windHistory[older].Y) * mix);
                    }
                    latest = older;
                }
                return windHistory[latest];
            }

            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            static float Profile(float[] values, float x)
            {
                x = Math.Max(0, Math.Min(values.Length - 1, x));
                int left = (int)x, right = Math.Min(values.Length - 1, left + 1);
                return values[left] + (values[right] - values[left]) * (x - left);
            }

            static float[] MakeLattice()
            {
                var values = new float[128 * 128 * 2];
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        double angle = Hash(x, y) * Math.PI * 2;
                        int offset = (y * 128 + x) * 2;
                        values[offset] = (float)Math.Cos(angle);
                        values[offset + 1] = (float)Math.Sin(angle);
                    }
                return values;
            }

            static float Field(float x, float y)
            {
                int left = Floor(x), top = Floor(y);
                float dx = x - left, dy = y - top;
                float blendX = dx * dx * dx * (dx * (dx * 6 - 15) + 10);
                float blendY = dy * dy * dy * (dy * (dy * 6 - 15) + 10);
                int right = (left + 1) & 127, bottom = (top + 1) & 127;
                left &= 127; top &= 127;
                int a = (top * 128 + left) * 2, b = (top * 128 + right) * 2;
                int c = (bottom * 128 + left) * 2, d = (bottom * 128 + right) * 2;
                float va = lattice[a] * dx + lattice[a + 1] * dy;
                float vb = lattice[b] * (dx - 1) + lattice[b + 1] * dy;
                float vc = lattice[c] * dx + lattice[c + 1] * (dy - 1);
                float vd = lattice[d] * (dx - 1) + lattice[d + 1] * (dy - 1);
                return .5f + (va + (vb - va) * blendX + ((vc - va)
                    + (va - vb - vc + vd) * blendX) * blendY) * .85f;
            }

            sealed class FluxDynamics
            {
                public float Lean;
                readonly float stiffness, damping, response;
                float velocity, clock;
                public FluxDynamics(float wind, float time, int seed)
                {
                    stiffness = 75 + Hash(seed, 211) * 170;
                    damping = 9 + Hash(seed, 223) * 10;
                    response = .65f + Hash(seed, 227) * .80f;
                    Lean = wind * response; clock = time;
                }
                public void Advance(float wind, float time, float height)
                {
                    float remaining = Math.Min(.18f, Math.Max(0, time - clock));
                    while (remaining > 0)
                    {
                        float dt = Math.Min(.012f, remaining);
                        velocity += ((wind * response - Lean) * stiffness / (1 + height * .2f) - velocity * damping) * dt;
                        Lean += velocity * dt; remaining -= dt;
                    }
                    clock = time;
                }
            }
        }

        sealed class FireScene
        {
            public readonly Size Size;
            public readonly int HeightPercent;
            readonly FlameField bottom, left, right, top;
            readonly int flameHeight, edgeDepth, sparkSeed;
            float time;
            PointF displacement;

            public FireScene(Size size, float time, int seed) : this(size, time, seed, 30) { }

            public FireScene(Size size, float time, int seed, int heightPercent)
            {
                Size = size;
                HeightPercent = heightPercent;
                this.time = time;
                flameHeight = Math.Max(1, size.Height * heightPercent / 100);
                edgeDepth = Math.Max(24, Math.Min(48, (int)(size.Height * .045f)));
                int distribution = seed ^ (size.Width * 397) ^ (size.Height * 733);
                sparkSeed = distribution;
                bottom = new FlameField(size.Width, flameHeight, distribution, Math.Max(1, Math.Min(size.Height / 5, flameHeight / 2)));
                left = new FlameField(size.Height, edgeDepth, distribution + 71);
                right = new FlameField(size.Height, edgeDepth, distribution + 131);
                top = new FlameField(size.Width, edgeDepth, distribution + 197);
            }

            public void AdvanceTo(float time, PointF displacement)
            {
                this.time = time;
                this.displacement = displacement;
            }

            public void Paint(Graphics graphics, float pulse)
            {
                var state = graphics.Save();
                graphics.SetClip(new Rectangle(Point.Empty, Size), CombineMode.Intersect);
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                using (var image = bottom.Image(time, .92f + pulse * .12f, displacement.X, displacement.Y))
                    graphics.DrawImage(image, new Rectangle(0, Size.Height - flameHeight, Size.Width, flameHeight));
                PaintSparks(graphics, pulse);
                PaintEdges(graphics, pulse);
                graphics.Restore(state);
            }

            public void PaintEdges(Graphics graphics, float pulse)
            {
                var state = graphics.Save();
                graphics.SetClip(new Rectangle(Point.Empty, Size), CombineMode.Intersect);
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                float scale = edgeDepth / (float)flameHeight;
                using (var image = left.Image(time, .94f + pulse * .1f, displacement.Y * scale, -displacement.X * scale))
                    graphics.DrawImage(image, new PointF[] { new PointF(edgeDepth, 0), new PointF(edgeDepth, Size.Height), new PointF(0, 0) });
                using (var image = right.Image(time, .94f + pulse * .1f, -displacement.Y * scale, displacement.X * scale))
                    graphics.DrawImage(image, new PointF[] { new PointF(Size.Width - edgeDepth, Size.Height), new PointF(Size.Width - edgeDepth, 0), new PointF(Size.Width, Size.Height) });
                using (var image = top.Image(time, .94f + pulse * .1f, -displacement.X * scale, -displacement.Y * scale))
                    graphics.DrawImage(image, new PointF[] { new PointF(Size.Width, edgeDepth * .8f), new PointF(0, edgeDepth * .8f), new PointF(Size.Width, 0) });
                graphics.Restore(state);
            }

            void PaintSparks(Graphics graphics, float pulse)
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                for (int index = 0; index < Size.Width / 31; index++)
                {
                    int seed = index + sparkSeed;
                    float phase = time * (.14f + Hash(seed, 3) * .12f) + Hash(seed, 7);
                    float age = phase - (float)Math.Floor(phase);
                    float x = Hash(seed, 13) * Size.Width + (Noise(seed, time * .5f) - .5f) * 24
                        + displacement.X * age * age;
                    float y = Size.Height - age * flameHeight * .91f;
                    float visibility = Smooth(0, .1f, age) * (1 - Smooth(.45f, .94f, age));
                    int alpha = (int)(visibility * (95 + pulse * 25));
                    float radius = .65f + Hash(seed, 29) * .6f;
                    using (var glow = new SolidBrush(Color.FromArgb(alpha / 7, 177, 99, 255)))
                        graphics.FillEllipse(glow, x - radius * 3, y - radius * 3, radius * 6, radius * 6);
                    using (var spark = new SolidBrush(Color.FromArgb(alpha, 235, 213, 255)))
                        graphics.FillEllipse(spark, x - radius * .5f, y - radius, radius, radius * 2);
                }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        static float Smooth(float from, float to, float value)
        {
            value = (value - from) / (to - from);
            if (value <= 0) return 0;
            if (value >= 1) return 1;
            return value * value * (3 - 2 * value);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        static int Floor(float value)
        {
            int integer = (int)value;
            return value < integer ? integer - 1 : integer;
        }

        static float Noise(float x, float y)
        {
            int left = (int)Math.Floor(x), top = (int)Math.Floor(y);
            float dx = x - left, dy = y - top;
            dx = dx * dx * (3 - 2 * dx); dy = dy * dy * (3 - 2 * dy);
            float a = Hash(left, top), b = Hash(left + 1, top);
            float c = Hash(left, top + 1), d = Hash(left + 1, top + 1);
            return a + (b - a) * dx + ((c - a) + (a - b - c + d) * dx) * dy;
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                uint value = (uint)x * 374761393u + (uint)y * 668265263u + 1442695041u;
                value = (value ^ (value >> 13)) * 1274126177u;
                return ((value ^ (value >> 16)) & 0x00ffffff) / 16777215f;
            }
        }

        void NotifyFrameChanged()
        {
            if (FrameChanged != null) FrameChanged(this, EventArgs.Empty);
        }

        internal void PaintBackground(Control surface, Graphics graphics)
        {
            if (!IsActive || frame == null || frame.Size != Size || !surface.IsHandleCreated) return;
            Point offset = host.PointToClient(surface.PointToScreen(Point.Empty));
            var visible = Rectangle.Intersect(surface.ClientRectangle, Rectangle.Ceiling(graphics.VisibleClipBounds));
            visible.Intersect(new Rectangle(-offset.X, -offset.Y, frame.Width, frame.Height));
            if (visible.Width == 0 || visible.Height == 0) return;
            int bottomTop = PaintTop, rim = Math.Min(50, bottomTop), side = Math.Min(50, frame.Width / 2);
            PaintRegion(graphics, offset, visible, new Rectangle(0, bottomTop, frame.Width, frame.Height - bottomTop));
            PaintRegion(graphics, offset, visible, new Rectangle(0, 0, frame.Width, rim));
            PaintRegion(graphics, offset, visible, new Rectangle(0, rim, side, bottomTop - rim));
            PaintRegion(graphics, offset, visible, new Rectangle(frame.Width - side, rim, side, bottomTop - rim));
        }

        void PaintRegion(Graphics graphics, Point offset, Rectangle visible, Rectangle band)
        {
            band.Offset(-offset.X, -offset.Y);
            var destination = Rectangle.Intersect(visible, band);
            if (destination.Width > 0 && destination.Height > 0)
                graphics.DrawImage(frame, destination, destination.X + offset.X, destination.Y + offset.Y,
                    destination.Width, destination.Height, GraphicsUnit.Pixel);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true; IsActive = false;
                animation.Dispose();
                RetirePendingFrame();
                host.LocationChanged -= Synchronize;
                host.SizeChanged -= Synchronize;
                host.VisibleChanged -= Synchronize;
                host.Activated -= Synchronize;
                host.EnabledChanged -= Synchronize;
                host.Disposed -= HostDisposed;
                if (filteringMessages) { Application.RemoveMessageFilter(this); filteringMessages = false; }
                if (frame != null) { frame.Dispose(); frame = null; }
            }
            base.Dispose(disposing);
        }

    }
}
