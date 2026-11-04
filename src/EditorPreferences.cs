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

        string InputPathFile { get { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "last-input-path.txt"); } }

        public string LoadInputPath()
        {
            try { return File.Exists(InputPathFile) ? File.ReadAllText(InputPathFile).Trim() : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public void SaveInputPath(string inputPath)
        {
            try
            {
                string file = InputPathFile;
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, Path.GetFullPath(inputPath));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
