using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PBZKeyForge
{
    internal sealed class GamepadCapture : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct XState
        { public uint Packet; public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LeftX, LeftY, RightX, RightY; }
        [StructLayout(LayoutKind.Sequential)] struct RawDevice
        { public ushort Page, Usage; public uint Flags; public IntPtr Window; }
        [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint index, out XState state);
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
        [DllImport("hid.dll")] static extern int HidP_GetUsages(int reportType, ushort page, ushort link, [Out] ushort[] usages, ref uint length, byte[] preparsed, byte[] report, uint reportLength);
        [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int reportType, ushort page, ushort link, ushort usage, out uint value, byte[] preparsed, byte[] report, uint reportLength);
        readonly Timer timer = new Timer { Interval = 20 };
        readonly Dictionary<string, Dictionary<string, double>> previous = new Dictionary<string, Dictionary<string, double>>();
        readonly Dictionary<IntPtr, byte[]> sonyDevices = new Dictionary<IntPtr, byte[]>();
        public event Action<string> Pressed, Released;
        public event Action<string, double> Axis;
        bool running;
        public GamepadCapture() { timer.Tick += delegate { Poll(); }; }
        public void Start(IntPtr window)
        {
            previous.Clear(); sonyDevices.Clear(); running = true;
            RegisterRawInputDevices(new[] { new RawDevice { Page = 1, Usage = 4, Flags = 0x100, Window = window },
                new RawDevice { Page = 1, Usage = 5, Flags = 0x100, Window = window } }, 2, (uint)Marshal.SizeOf(typeof(RawDevice)));
            Poll(); timer.Start();
        }
        public void Stop()
        {
            if (!running) return;
            timer.Stop(); running = false;
            RegisterRawInputDevices(new[] { new RawDevice { Page = 1, Usage = 4, Flags = 1 }, new RawDevice { Page = 1, Usage = 5, Flags = 1 } }, 2, (uint)Marshal.SizeOf(typeof(RawDevice)));
            previous.Clear(); sonyDevices.Clear();
        }
        void Poll()
        {
            for (uint index = 0; index < 4; index++)
            {
                XState state;
                string id = "x" + index;
                if (XInputGetState(index, out state) != 0) { Feed(id, new Dictionary<string, double>()); continue; }
                var sample = new Dictionary<string, double>();
                AddButtons(sample, state.Buttons);
                AddAxes(sample, state.LeftX / 32768d, state.LeftY / 32768d, state.RightX / 32768d, state.RightY / 32768d,
                    state.LeftTrigger / 255d, state.RightTrigger / 255d);
                Feed(id, sample);
            }
        }
        static void AddButtons(Dictionary<string, double> sample, int buttons)
        {
            int[] masks = { 1, 2, 4, 8, 0x10, 0x20, 0x40, 0x80, 0x100, 0x200, 0x1000, 0x2000, 0x4000, 0x8000 };
            string[] keys = { "DPad_Up", "DPad_Down", "DPad_Left", "DPad_Right", "Special_Right", "Special_Left", "LeftThumbstick", "RightThumbstick",
                "LeftShoulder", "RightShoulder", "FaceButton_Bottom", "FaceButton_Right", "FaceButton_Left", "FaceButton_Top" };
            for (int i = 0; i < keys.Length; i++) sample["Gamepad_" + keys[i]] = (buttons & masks[i]) != 0 ? 1 : 0;
        }
        static void AddAxes(Dictionary<string, double> sample, double lx, double ly, double rx, double ry, double lt, double rt)
        {
            sample["Gamepad_LeftX"] = lx; sample["Gamepad_LeftY"] = ly; sample["Gamepad_RightX"] = rx; sample["Gamepad_RightY"] = ry;
            sample["Gamepad_LeftTriggerAxis"] = lt; sample["Gamepad_RightTriggerAxis"] = rt;
            sample["Gamepad_LeftTrigger"] = lt > .55 ? 1 : 0; sample["Gamepad_RightTrigger"] = rt > .55 ? 1 : 0;
            foreach (string stick in new[] { "Left", "Right" })
            {
                double x = stick == "Left" ? lx : rx, y = stick == "Left" ? ly : ry;
                sample["Gamepad_" + stick + "Stick_Left"] = x < -.55 ? 1 : 0; sample["Gamepad_" + stick + "Stick_Right"] = x > .55 ? 1 : 0;
                sample["Gamepad_" + stick + "Stick_Up"] = y > .55 ? 1 : 0; sample["Gamepad_" + stick + "Stick_Down"] = y < -.55 ? 1 : 0;
            }
        }
        internal void Feed(string device, Dictionary<string, double> sample)
        {
            Dictionary<string, double> old;
            if (!previous.TryGetValue(device, out old)) { previous[device] = sample; return; }
            foreach (var pair in sample)
            {
                double before; old.TryGetValue(pair.Key, out before);
                bool axis = pair.Key.EndsWith("Axis", StringComparison.Ordinal) || pair.Key.EndsWith("X", StringComparison.Ordinal) || pair.Key.EndsWith("Y", StringComparison.Ordinal);
                if (axis)
                {
                    if (Math.Abs(pair.Value) > .55 && (Math.Abs(before) <= .55 || Math.Sign(pair.Value) != Math.Sign(before)) && Axis != null) Axis(pair.Key, pair.Value);
                }
                else if (pair.Value > .5 && before <= .5 && Pressed != null) Pressed(pair.Key);
            }
            foreach (var pair in old)
            {
                double now; sample.TryGetValue(pair.Key, out now);
                if (pair.Value > .5 && now <= .5 && Released != null) Released(pair.Key);
            }
            previous[device] = sample;
        }
        public void ReadRawInput(IntPtr input)
        {
            if (!running) return;
            uint header = (uint)(IntPtr.Size * 2 + 8), size = 0;
            if (GetRawInputData(input, 0x10000003, IntPtr.Zero, ref size, header) != 0 || size < header + 8) return;
            IntPtr memory = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(input, 0x10000003, memory, ref size, header) != size || Marshal.ReadInt32(memory) != 2) return;
                IntPtr device = Marshal.ReadIntPtr(memory, 8);
                byte[] preparsed;
                if (!sonyDevices.TryGetValue(device, out preparsed))
                {
                    uint infoSize = 32; IntPtr info = Marshal.AllocHGlobal(32);
                    try
                    {
                        Marshal.WriteInt32(info, 32);
                        if (GetRawInputDeviceInfo(device, 0x2000000b, info, ref infoSize) == uint.MaxValue || Marshal.ReadInt32(info, 8) != 0x054c)
                        { sonyDevices[device] = null; return; }
                    }
                    finally { Marshal.FreeHGlobal(info); }
                    uint length = 0; GetRawInputDeviceInfo(device, 0x20000005, IntPtr.Zero, ref length);
                    IntPtr data = Marshal.AllocHGlobal((int)length);
                    try
                    {
                        if (GetRawInputDeviceInfo(device, 0x20000005, data, ref length) == uint.MaxValue) { sonyDevices[device] = null; return; }
                        preparsed = new byte[length]; Marshal.Copy(data, preparsed, 0, (int)length); sonyDevices[device] = preparsed;
                    }
                    finally { Marshal.FreeHGlobal(data); }
                }
                if (preparsed == null) return;
                int reportSize = Marshal.ReadInt32(memory, (int)header), count = Marshal.ReadInt32(memory, (int)header + 4);
                for (int index = 0; index < count && header + 8 + (index + 1L) * reportSize <= size; index++)
                {
                    byte[] report = new byte[reportSize]; Marshal.Copy(IntPtr.Add(memory, (int)header + 8 + index * reportSize), report, 0, reportSize);
                    ReadSony(device, preparsed, report);
                }
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        void ReadSony(IntPtr device, byte[] preparsed, byte[] report)
        {
            var sample = new Dictionary<string, double>();
            uint count = 32; var buttons = new ushort[32];
            if (HidP_GetUsages(0, 9, 0, buttons, ref count, preparsed, report, (uint)report.Length) != 0x110000) return;
            string[] keys = { "FaceButton_Left", "FaceButton_Bottom", "FaceButton_Right", "FaceButton_Top", "LeftShoulder", "RightShoulder",
                "LeftTrigger", "RightTrigger", "Special_Left", "Special_Right", "LeftThumbstick", "RightThumbstick" };
            foreach (string key in keys) sample["Gamepad_" + key] = 0;
            for (int i = 0; i < count; i++) if (buttons[i] > 0 && buttons[i] <= keys.Length) sample["Gamepad_" + keys[buttons[i] - 1]] = 1;
            uint x, y, rx, ry, lt, rt, hat;
            if (Value(0x30, preparsed, report, out x) && Value(0x31, preparsed, report, out y)
                && Value(0x32, preparsed, report, out rx) && Value(0x35, preparsed, report, out ry)
                && Value(0x33, preparsed, report, out lt) && Value(0x34, preparsed, report, out rt))
                AddAxes(sample, (x - 127.5) / 127.5, (127.5 - y) / 127.5, (rx - 127.5) / 127.5, (127.5 - ry) / 127.5, lt / 255d, rt / 255d);
            if (Value(0x39, preparsed, report, out hat))
            {
                sample["Gamepad_DPad_Up"] = hat == 0 || hat == 1 || hat == 7 ? 1 : 0;
                sample["Gamepad_DPad_Right"] = hat >= 1 && hat <= 3 ? 1 : 0;
                sample["Gamepad_DPad_Down"] = hat >= 3 && hat <= 5 ? 1 : 0;
                sample["Gamepad_DPad_Left"] = hat >= 5 && hat <= 7 ? 1 : 0;
            }
            Feed("hid" + device, sample);
        }
        static bool Value(ushort usage, byte[] preparsed, byte[] report, out uint value)
        { return HidP_GetUsageValue(0, 1, 0, usage, out value, preparsed, report, (uint)report.Length) == 0x110000; }
        public void Dispose() { Stop(); timer.Dispose(); }
    }
}
