using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using ProtoBuf;

namespace PBZKeyForge
{
    internal static class PresetDependencies
    {
        static PresetDependencies()
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args) {
                if (new AssemblyName(args.Name).Name != "protobuf-net") return null;
                using (var stream = typeof(PresetDependencies).Assembly.GetManifestResourceStream("PBZKeyForge.protobuf-net"))
                using (var output = new MemoryStream())
                { stream.CopyTo(output); return Assembly.Load(output.ToArray()); }
            };
        }
        public static void Initialize() { }
    }

    [ProtoContract]
    internal sealed class ShareDocument
    {
        [ProtoMember(1)] public uint Baseline;
        [ProtoMember(2)] public List<ShareScope> Scopes = new List<ShareScope>();
        [ProtoMember(3)] public List<string> ExtraKeys = new List<string>();
        [ProtoMember(4)] public List<string> ExtraFamilies = new List<string>();
    }

    [ProtoContract]
    internal sealed class ShareScope
    {
        [ProtoMember(1)] public uint Device;
        [ProtoMember(2)] public List<ShareChange> Changes = new List<ShareChange>();
    }

    [ProtoContract]
    internal sealed class ShareChange
    {
        [ProtoMember(1)] public uint Action;
        [ProtoMember(2, IsPacked = true)] public uint[] Added;
        [ProtoMember(3, IsPacked = true)] public uint[] Removed;
    }

    internal static class PresetShare
    {
        static PresetShare() { PresetDependencies.Initialize(); }

        const uint LiteralStart = 16384;
        // Permanent one-based IDs; independent of KeyNames and display ordering.
        static readonly string[] Keys = {
            "LeftMouseButton", "RightMouseButton", "MiddleMouseButton", "ThumbMouseButton", "ThumbMouseButton2",
            "LeftShift", "RightShift", "LeftControl", "RightControl", "LeftAlt", "RightAlt", "SpaceBar", "Tab", "CapsLock",
            "Escape", "Enter", "BackSpace", "LeftBracket", "RightBracket", "Semicolon", "Quote", "Comma", "Period", "Slash", "Backslash", "Tilde",
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
            "Up", "Down", "Left", "Right", "Home", "End", "PageUp", "PageDown", "Insert", "Delete",
            "NumPadZero", "NumPadOne", "NumPadTwo", "NumPadThree", "NumPadFour", "NumPadFive", "NumPadSix", "NumPadSeven", "NumPadEight", "NumPadNine",
            "Gamepad_FaceButton_Bottom", "Gamepad_FaceButton_Right", "Gamepad_FaceButton_Left", "Gamepad_FaceButton_Top",
            "Gamepad_LeftShoulder", "Gamepad_RightShoulder", "Gamepad_LeftTrigger", "Gamepad_RightTrigger",
            "Gamepad_LeftTriggerAxis", "Gamepad_RightTriggerAxis", "Gamepad_LeftThumbstick", "Gamepad_RightThumbstick",
            "Gamepad_DPad_Up", "Gamepad_DPad_Down", "Gamepad_DPad_Left", "Gamepad_DPad_Right", "Gamepad_Special_Left", "Gamepad_Special_Right",
            "Multiply", "Add", "Subtract", "Decimal", "Divide", "NumLock", "ScrollLock", "Pause", "PrintScreen", "LeftWindows", "RightWindows"
        };
        static readonly string[] Families = { "Xbox", "PS" };

        static uint Id(string value, string[] known, List<string> extra)
        {
            int index = Array.IndexOf(known, value);
            if (index >= 0) return (uint)index + 1;
            index = extra.IndexOf(value);
            if (index < 0) { index = extra.Count; extra.Add(value); }
            return LiteralStart + (uint)index;
        }

        static string Value(uint id, string[] known, List<string> extra)
        {
            if (id > 0 && id <= known.Length) return known[id - 1];
            if (id >= LiteralStart && id - LiteralStart < extra.Count) return extra[(int)(id - LiteralStart)];
            throw new InvalidDataException(I18n.Str("preset.shareupgrade"));
        }

        static uint[] KeyIds(string[] keys, List<string> extra)
        {
            if (keys.Length == 0) return null;
            return keys.Distinct().OrderBy(key => key, StringComparer.Ordinal).Select(key => Id(key, Keys, extra)).OrderBy(id => id).ToArray();
        }

        public static string Encode(Preset preset)
        { return WithName(preset.Name, EncodePayload(preset)); }

        internal static string WithName(string name, string payload)
        {
            UserPresetStore.ValidateName(name);
            return "PBZ2:" + name.Replace('\r', ' ').Replace('\n', ' ').Trim() + ":" + payload;
        }

        internal static string EncodePayload(Preset preset)
        {
            var document = new ShareDocument { Baseline = preset.BaselineId };
            foreach (var scope in preset.Scopes.OrderBy(scope => scope.Family, StringComparer.Ordinal))
            {
                var group = new ShareScope { Device = scope.Family == null ? 0 : Id(scope.Family, Families, document.ExtraFamilies) };
                foreach (var mapping in preset.UserMappings.Where(mapping => mapping.Family == scope.Family)
                    .OrderBy(mapping => Array.IndexOf(PresetBaseline.Actions, mapping.Action)))
                {
                    if (mapping.Added.Length == 0 && mapping.Removed.Length == 0) continue;
                    group.Changes.Add(new ShareChange { Action = (uint)Array.IndexOf(PresetBaseline.Actions, mapping.Action) + 1,
                        Added = KeyIds(mapping.Added, document.ExtraKeys), Removed = KeyIds(mapping.Removed, document.ExtraKeys) });
                }
                document.Scopes.Add(group);
            }
            byte[] raw;
            using (var output = new MemoryStream()) { Serializer.Serialize(output, document); raw = output.ToArray(); }
            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true)) deflate.Write(raw, 0, raw.Length);
                compressed = output.ToArray();
            }
            bool compress = compressed.Length < raw.Length;
            byte[] payload = compress ? compressed : raw;
            var envelope = new byte[payload.Length + 1]; envelope[0] = compress ? (byte)1 : (byte)0;
            Buffer.BlockCopy(payload, 0, envelope, 1, payload.Length);
            return Convert.ToBase64String(envelope).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static Preset Decode(string name, byte[] envelope)
        {
            if (envelope.Length == 0) throw new FormatException();
            if (envelope[0] > 1) throw new InvalidDataException(I18n.Str("preset.shareupgrade"));
            ShareDocument document;
            using (var input = new MemoryStream(envelope, 1, envelope.Length - 1))
            {
                if (envelope[0] == 0) document = Serializer.Deserialize<ShareDocument>(input);
                else using (var deflate = new DeflateStream(input, CompressionMode.Decompress)) document = Serializer.Deserialize<ShareDocument>(deflate);
            }
            PresetBaseline.Require(document.Baseline);
            var preset = new Preset { UserName = name, BaselineId = document.Baseline, Scopes = new List<PresetScope>(), UserMappings = new List<PresetMapping>() };
            foreach (var scope in document.Scopes)
            {
                string family = scope.Device == 0 ? null : Value(scope.Device, Families, document.ExtraFamilies);
                preset.Scopes.Add(new PresetScope { Family = family });
                foreach (var change in scope.Changes)
                {
                    if (change.Action == 0 || change.Action > PresetBaseline.Actions.Length)
                        throw new InvalidDataException(I18n.Str("preset.shareupgrade"));
                    preset.UserMappings.Add(new PresetMapping { Family = family, Action = PresetBaseline.Actions[change.Action - 1],
                        Added = (change.Added ?? new uint[0]).Select(id => Value(id, Keys, document.ExtraKeys)).ToArray(),
                        Removed = (change.Removed ?? new uint[0]).Select(id => Value(id, Keys, document.ExtraKeys)).ToArray() });
                }
            }
            return preset;
        }
    }
}
