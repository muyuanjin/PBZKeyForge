using System;
using System.IO;

namespace PBZKeyForge
{
    public sealed class EditorPreferences
    {
        readonly string path;

        public EditorPreferences(string path) { this.path = path; }

        public static EditorPreferences Default()
        {
            return new EditorPreferences(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PBZKeyForge", "preset-mode.txt"));
        }

        public PresetApplyMode LoadPresetMode()
        {
            try
            {
                PresetApplyMode mode;
                if (File.Exists(path) && Enum.TryParse(File.ReadAllText(path).Trim(), out mode)
                    && Enum.IsDefined(typeof(PresetApplyMode), mode)) return mode;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return PresetApplyMode.OnlyPreset;
        }

        public void SavePresetMode(PresetApplyMode mode)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, mode.ToString());
        }
    }
}
