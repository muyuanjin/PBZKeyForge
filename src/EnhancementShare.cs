using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    // Field order and symbol numbers are part of the share format. Append new entries.
    internal static class EnhancementShare
    {
        static readonly Dictionary<Type, string[]> Fields = new Dictionary<Type, string[]> {
            { typeof(KeyEnhancementConfiguration), new[] { "Enabled", "RuntimeHotkeys", "NativeBindings", "Rules", "CustomFunctions", "Suppressions", "Hooks", "NativeAbilitySuppressions", "CustomLua" } },
            { typeof(NativeBindingOverride), new[] { "FunctionId", "Device", "Keys" } },
            { typeof(KeyEnhancementRule), new[] { "Id", "Keys", "Outputs", "Trigger", "Enabled", "Seconds", "InputGroup", "Consume", "ConsumeKeys", "Suppressions", "WhenLua", "Axis", "AxisDirection", "Threshold", "Interval", "Count", "Steps", "Watch", "EvaluateLua", "Priority", "BlockOriginalWhenDisabled", "DefaultTrigger", "BypassResources", "DamageMultiplier", "StartupSpeed", "RecoverySpeed" } },
            { typeof(KeyEnhancementOutput), new[] { "Kind", "Path", "Tag", "MappingName", "MappingActions", "Device", "Value", "Id", "Name", "FunctionId", "Receiver", "ValueType", "Property", "Code", "InputAction", "Clamp", "Modifiers", "Triggers", "Payload", "Args", "Force", "Priority", "NativePipeline" } },
            { typeof(KeyEnhancementSuppression), new[] { "ActionPath", "Key", "FunctionId" } }
        };
        static readonly Dictionary<Type, FieldInfo[]> Schema = Fields.ToDictionary(pair => pair.Key, pair => {
            var fields = pair.Key.GetFields(BindingFlags.Instance | BindingFlags.Public).ToDictionary(field => field.Name);
            return pair.Value.Select(name => fields[name]).ToArray();
        });
        // Wire defaults are independent of editor defaults.
        static readonly Dictionary<Type, object> Defaults = new Dictionary<Type, object> {
            { typeof(KeyEnhancementConfiguration), new KeyEnhancementConfiguration { Enabled = false, RuntimeHotkeys = false, CustomLua = null } },
            { typeof(NativeBindingOverride), new NativeBindingOverride { FunctionId = null, Device = null } },
            { typeof(KeyEnhancementRule), new KeyEnhancementRule { Id = null, InputGroup = null, AxisDirection = "both", Axis = null, Trigger = "press",
                WhenLua = null, EvaluateLua = null, Enabled = true, Consume = false, BlockOriginalWhenDisabled = false,
                Seconds = .25, Interval = .3, Threshold = .5, Count = 2, Priority = 0 } },
            { typeof(KeyEnhancementOutput), new KeyEnhancementOutput { Kind = "action", Path = null, Tag = null, Receiver = "pawn", ValueType = null,
                Property = null, Code = null, InputAction = null, MappingName = null, Device = null, Id = null, Name = null, FunctionId = null,
                MappingActions = null, Clamp = null, Value = null, Payload = null, Args = null, Force = false, Priority = -5 } },
            { typeof(KeyEnhancementSuppression), new KeyEnhancementSuppression { ActionPath = null, Key = null, FunctionId = null } }
        };
        static readonly string[] Symbols = EnhancementShareSymbols.Values;

        internal static byte[] Encode(KeyEnhancementConfiguration value)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                var codec = new Codec(writer, null); codec.Write(value, typeof(KeyEnhancementConfiguration)); return stream.ToArray();
            }
        }

        internal static KeyEnhancementConfiguration Decode(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                var result = (KeyEnhancementConfiguration)new Codec(null, reader).Read(typeof(KeyEnhancementConfiguration));
                if (stream.Position != stream.Length) throw new InvalidDataException(I18n.Str("preset.shareupgrade"));
                result.CompileLua(); return result;
            }
        }

        sealed class Codec
        {
            readonly BinaryWriter writer;
            readonly BinaryReader reader;
            readonly List<string> strings = Symbols.ToList();
            readonly Dictionary<string, int> indices = Symbols.Select((value, index) => new { value, index }).ToDictionary(item => item.value, item => item.index);
            readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16777216 };
            internal Codec(BinaryWriter writer, BinaryReader reader) { this.writer = writer; this.reader = reader; }

            void Number(ulong value)
            { while (value >= 128) { writer.Write((byte)(value | 128)); value >>= 7; } writer.Write((byte)value); }
            ulong Number()
            {
                ulong result = 0;
                for (int shift = 0; shift < 64; shift += 7)
                { byte value = reader.ReadByte(); result |= (ulong)(value & 127) << shift; if (value < 128) return result; }
                throw new InvalidDataException(I18n.Str("preset.invalidshare"));
            }
            void Signed(long value) { Number((ulong)((value << 1) ^ (value >> 63))); }
            long Signed() { ulong value = Number(); return (long)(value >> 1) ^ -((long)value & 1); }

            void Text(string value)
            {
                if (value == null) { Number(0); return; }
                int index;
                if (indices.TryGetValue(value, out index)) { Number((ulong)index + 4); return; }
                Guid guid;
                if (Guid.TryParseExact(value, "N", out guid) && guid.ToString("N") == value) { Number(2); writer.Write(guid.ToByteArray()); }
                else if (Guid.TryParseExact(value, "D", out guid) && guid.ToString("D") == value) { Number(3); writer.Write(guid.ToByteArray()); }
                else { Number(1); writer.Write(value); }
                indices.Add(value, strings.Count); strings.Add(value);
            }
            string Text()
            {
                ulong token = Number(); if (token == 0) return null;
                if (token >= 4)
                {
                    if (token - 4 >= (ulong)strings.Count) throw new InvalidDataException(I18n.Str("preset.shareupgrade"));
                    return strings[(int)(token - 4)];
                }
                string value = token == 1 ? reader.ReadString() : new Guid(reader.ReadBytes(16)).ToString(token == 2 ? "N" : "D");
                strings.Add(value); return value;
            }

            bool Equal(object value, object original)
            {
                if (value == null || original == null) return value == original;
                var items = value as IList; var defaults = original as IList;
                if (items != null && defaults != null) return items.Count == 0 && defaults.Count == 0;
                return value.Equals(original);
            }
            internal void Write(object value, Type type)
            {
                FieldInfo[] fields;
                if (Schema.TryGetValue(type, out fields))
                {
                    ulong mask = 0;
                    for (int i = 0; i < fields.Length; i++) if (!Equal(fields[i].GetValue(value), fields[i].GetValue(Defaults[type]))) mask |= 1UL << i;
                    Number(mask);
                    for (int i = 0; i < fields.Length; i++) if ((mask & (1UL << i)) != 0 && fields[i].FieldType != typeof(bool)) Write(fields[i].GetValue(value), fields[i].FieldType);
                }
                else if (type == typeof(string)) Text((string)value);
                else if (type == typeof(int)) Signed((int)value);
                else if (type == typeof(double) || type == typeof(double?)) Dynamic(value);
                else if (type.IsArray || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var list = value as IList; Number(list == null ? 0UL : (ulong)list.Count + 1);
                    if (list != null) foreach (object item in list) Write(item, type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0]);
                }
                else Dynamic(value);
            }
            internal object Read(Type type)
            {
                FieldInfo[] fields;
                if (Schema.TryGetValue(type, out fields))
                {
                    ulong mask = Number();
                    if ((mask >> fields.Length) != 0) throw new InvalidDataException(I18n.Str("preset.shareupgrade"));
                    var value = Activator.CreateInstance(type);
                    foreach (var field in fields)
                    {
                        object original = field.GetValue(Defaults[type]);
                        field.SetValue(value, original is IList ? Activator.CreateInstance(field.FieldType) : original);
                    }
                    for (int i = 0; i < fields.Length; i++) if ((mask & (1UL << i)) != 0)
                        fields[i].SetValue(value, fields[i].FieldType == typeof(bool) ? !(bool)fields[i].GetValue(Defaults[type]) : Read(fields[i].FieldType));
                    return value;
                }
                if (type == typeof(string)) return Text();
                if (type == typeof(int)) return checked((int)Signed());
                if (type == typeof(double) || type == typeof(double?)) { var value = Dynamic(); return value == null ? null : (object)Convert.ToDouble(value, CultureInfo.InvariantCulture); }
                if (type.IsArray || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    int count = checked((int)Number()) - 1; if (count < 0) return null;
                    Type element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element));
                    for (int i = 0; i < count; i++) list.Add(Read(element));
                    if (!type.IsArray) return list;
                    var array = Array.CreateInstance(element, count); list.CopyTo(array, 0); return array;
                }
                return Dynamic();
            }

            // Dynamic values preserve arbitrary function arguments, hook data and property values.
            void Dynamic(object value)
            {
                if (value == null) { writer.Write((byte)0); return; }
                if (value is bool) { writer.Write((byte)((bool)value ? 2 : 1)); return; }
                if (value is string) { writer.Write((byte)3); Text((string)value); return; }
                var dictionary = value as IDictionary;
                if (dictionary != null)
                {
                    writer.Write((byte)4); Number((ulong)dictionary.Count);
                    foreach (DictionaryEntry entry in dictionary) { Text((string)entry.Key); Dynamic(entry.Value); } return;
                }
                var sequence = value as IEnumerable;
                if (sequence != null)
                {
                    var items = sequence.Cast<object>().ToArray(); writer.Write((byte)5); Number((ulong)items.Length);
                    foreach (object item in items) Dynamic(item); return;
                }
                if (value is byte || value is short || value is int || value is long || value is uint)
                { writer.Write((byte)6); Signed(Convert.ToInt64(value)); return; }
                if (value is double || value is float)
                {
                    double number = Convert.ToDouble(value);
                    for (int scale = 0; scale <= 6; scale++)
                    {
                        double factor = Math.Pow(10, scale), scaled = Math.Round(number * factor);
                        if (Math.Abs(scaled) < 1000000000 && scaled / factor == number)
                        { writer.Write((byte)(8 + scale)); Signed((long)scaled); return; }
                    }
                    writer.Write((byte)7); writer.Write(number); return;
                }
                if (value is decimal) { writer.Write((byte)15); writer.Write((decimal)value); return; }
                Dynamic(json.DeserializeObject(json.Serialize(value)));
            }
            object Dynamic()
            {
                byte tag = reader.ReadByte();
                switch (tag)
                {
                    case 0: return null;
                    case 1: return false;
                    case 2: return true;
                    case 3: return Text();
                    case 4:
                        var map = new Dictionary<string, object>(); int size = checked((int)Number());
                        for (int i = 0; i < size; i++) map.Add(Text(), Dynamic()); return map;
                    case 5:
                        var array = new object[checked((int)Number())]; for (int i = 0; i < array.Length; i++) array[i] = Dynamic(); return array;
                    case 6: return Signed();
                    case 7: return reader.ReadDouble();
                    case 15: return reader.ReadDecimal();
                    default:
                        if (tag >= 8 && tag <= 14) return Signed() / Math.Pow(10, tag - 8);
                        throw new InvalidDataException(I18n.Str("preset.shareupgrade"));
                }
            }
        }
    }
}
