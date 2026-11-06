using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    public sealed class UserPresetDocument
    {
        public string Name;
        public uint BaselineId;
        public List<PresetScope> Scopes;
        public List<PresetMapping> Mappings;
        public KeyEnhancementConfiguration KeyEnhancement;
    }

    public sealed class UserPresetStore
    {
        readonly string directory;
        public UserPresetStore(string directory) { this.directory = directory; }
        public static UserPresetStore Default()
        { return new UserPresetStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBZKeyForge", "Presets")); }

        public List<Preset> Load()
        {
            var paths = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json") : new string[0];
            return paths.Length == 0 ? new List<Preset>() : LoadFiles(paths);
        }

        static List<Preset> LoadFiles(string[] paths)
        {
            var json = new JavaScriptSerializer();
            return paths.Select(path => {
                var document = json.Deserialize<UserPresetDocument>(File.ReadAllText(path, Encoding.UTF8));
                var preset = new Preset { Id = Path.GetFileNameWithoutExtension(path), UserName = document.Name,
                    BaselineId = document.BaselineId, Scopes = document.Scopes, UserMappings = document.Mappings, KeyEnhancement = document.KeyEnhancement };
                Validate(preset);
                return preset;
            }).OrderBy(preset => preset.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public Preset Save(Preset preset, string name)
        { return Save(preset, name, Load()); }

        internal Preset Save(Preset preset, string name, List<Preset> library)
        {
            name = NormalizeName(name);
            var result = new Preset { Id = "user_" + Guid.NewGuid().ToString("N"), UserName = name,
                BaselineId = preset.BaselineId, Scopes = preset.Scopes, UserMappings = preset.UserMappings, KeyEnhancement = preset.KeyEnhancement == null ? null : preset.KeyEnhancement.Clone() };
            Validate(result);
            if (library.Any(item => string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                throw new InvalidOperationException(I18n.Str("preset.duplicatename"));
            var document = new UserPresetDocument { Name = name, BaselineId = result.BaselineId, Scopes = result.Scopes, Mappings = result.UserMappings, KeyEnhancement = result.KeyEnhancement };
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, result.Id + ".tmp");
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(document), new UTF8Encoding(false));
                File.Move(temporary, Path.Combine(directory, result.Id + ".json"));
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            library.Add(result);
            library.Sort((first, second) => StringComparer.CurrentCultureIgnoreCase.Compare(first.Name, second.Name));
            return result;
        }

        public void Delete(IEnumerable<Preset> presets)
        { Delete(presets, Load()); }

        internal void Delete(IEnumerable<Preset> presets, List<Preset> library)
        {
            var selected = presets.ToArray();
            var owned = library.ToDictionary(preset => preset.Id);
            foreach (var preset in selected)
            {
                if (!preset.IsUser || !owned.ContainsKey(preset.Id)) throw new InvalidOperationException(I18n.Str("preset.builtinreadonly"));
            }
            foreach (var preset in selected) File.Delete(Path.Combine(directory, preset.Id + ".json"));
            library.RemoveAll(preset => selected.Any(item => item.Id == preset.Id));
        }

        public static Preset Capture(EditorSession session, string name)
        { return Capture(session, name, null); }

        public static Preset Capture(EditorSession session, string name, KeyEnhancementConfiguration enhancement)
        {
            session.Refresh();
            if (session.Conflicts.Length > 0) throw new InvalidOperationException(I18n.Str("editor.conflictinfo"));
            foreach (bool pad in new[] { false, true })
                foreach (int family in pad ? session.ControllerFamilies.Select(item => item.Index) : new[] { -1 })
                    if (session.BindingActions(pad, family).Any(action => {
                        var relation = session.Relations(action, pad, family); return relation.DefaultKeys == null && relation.Changed;
                    })) throw new InvalidOperationException(I18n.Str("preset.unknowndefault"));
            var mappings = session.SchemeDifferences(true).Select(change => new PresetMapping {
                Action = change.Action, Family = change.Gamepad ? change.Family : null,
                Added = change.AddedKeys, Removed = change.RemovedKeys }).ToList();
            var preset = new Preset { UserName = NormalizeName(name), BaselineId = PresetBaseline.CurrentId,
                KeyEnhancement = enhancement == null ? null : enhancement.Clone(),
                Scopes = new[] { new PresetScope() }.Concat(session.ControllerFamilies.Select(family => new PresetScope { Family = family.Name })).ToList(),
                UserMappings = mappings };
            Validate(preset);
            return preset;
        }

        public static string Share(Preset preset)
        {
            Validate(preset);
            return PresetShare.Encode(preset);
        }

        public static Preset SelectScopes(Preset preset, IEnumerable<PresetScope> scopes)
        {
            Validate(preset);
            var selected = scopes.ToList();
            if (selected.Any(scope => !preset.Scopes.Any(original => original.Family == scope.Family)))
                throw new InvalidDataException(I18n.Str("preset.invalidshare"));
            var result = new Preset { UserName = preset.Name, BaselineId = preset.BaselineId, Scopes = selected,
                KeyEnhancement = preset.KeyEnhancement == null ? null : EnhancementEditing.SelectDevices(preset.KeyEnhancement,
                    selected.Any(scope => scope.Family == null), selected.Any(scope => scope.Family != null)),
                UserMappings = preset.UserMappings.Where(mapping => selected.Any(scope => scope.Family == mapping.Family)).ToList() };
            Validate(result);
            return result;
        }

        public static string ScopeText(Preset preset)
        { return string.Join(" / ", preset.Scopes.Select(scope => scope.Family ?? I18n.Str("editor.keyboard"))); }

        public static Preset ParseShare(string text)
        {
            try
            {
                text = text.Trim(); int separator = text.LastIndexOf(':');
                if (!text.StartsWith("PBZ2:", StringComparison.Ordinal) || separator <= 5) throw new FormatException();
                string name = NormalizeName(text.Substring(5, separator - 5));
                string encoded = text.Substring(separator + 1);
                if (encoded.Length == 0 || encoded.Any(c => !(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_')))
                    throw new FormatException();
                encoded = encoded.Replace('-', '+').Replace('_', '/');
                encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
                var preset = PresetShare.Decode(name, Convert.FromBase64String(encoded));
                Validate(preset);
                return preset;
            }
            catch (Exception ex)
            {
                if (ex is InvalidDataException && ex.Message == I18n.Str("preset.shareupgrade")) throw;
                if (!(ex is FormatException || ex is IOException || ex is ArgumentException || ex.GetType().FullName == "ProtoBuf.ProtoException")) throw;
                throw new InvalidDataException(I18n.Str("preset.invalidshare"), ex);
            }
        }

        static string NormalizeName(string name)
        { return (name ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim(); }

        internal static void ValidateName(string name)
        { if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new InvalidDataException(I18n.Str("preset.namerequired")); }

        public static void Validate(Preset preset)
        {
            ValidateName(preset.Name);
            PresetBaseline.Require(preset.BaselineId);
            var mappings = preset.UserMappings;
            if (preset.Scopes == null || preset.Scopes.Count == 0 || preset.Scopes.Any(scope => scope == null
                || scope.Family != null && string.IsNullOrWhiteSpace(scope.Family))
                || preset.Scopes.GroupBy(scope => scope.Family).Any(group => group.Count() > 1))
                throw new InvalidDataException(I18n.Str("preset.invalidshare"));
            if (mappings == null) throw new InvalidDataException(I18n.Str("preset.invalidshare"));
            if (preset.KeyEnhancement != null) preset.KeyEnhancement.CompileLua();
            foreach (var mapping in mappings)
            {
                if (mapping == null || !PresetBaseline.Actions.Contains(mapping.Action) || mapping.Added == null || mapping.Removed == null
                    || !preset.Scopes.Any(scope => scope.Family == mapping.Family)
                    || mapping.Added.Concat(mapping.Removed).Any(key => string.IsNullOrWhiteSpace(key) || key == "None")
                    || mapping.Added.Intersect(mapping.Removed).Any()
                    || mapping.Removed.Except(PresetBaseline.Keys(preset.BaselineId, mapping.Action, mapping.Family != null)).Any()
                    || mapping.Added.Intersect(PresetBaseline.Keys(preset.BaselineId, mapping.Action, mapping.Family != null)).Any()
                    || mapping.Removed.Intersect(InputDefaults.FixedForAction(mapping.Action, mapping.Family != null)).Any())
                    throw new InvalidDataException(I18n.Str("preset.invalidshare"));
            }
            if (mappings.GroupBy(mapping => mapping.Action + "\n" + mapping.Family).Any(group => group.Count() > 1))
                throw new InvalidDataException(I18n.Str("preset.invalidshare"));
        }
    }
}
