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
        string CompanionHotkeyFile { get { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "companion-hotkey.txt"); } }
        public bool LoadCompanionHotkey()
        {
            try { return !File.Exists(CompanionHotkeyFile) || File.ReadAllText(CompanionHotkeyFile).Trim() != "off"; }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }
        public void SaveCompanionHotkey(bool enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CompanionHotkeyFile));
            File.WriteAllText(CompanionHotkeyFile, enabled ? "on" : "off");
        }
        string FlameEffectsFile { get { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "flame-effects.txt"); } }
        string FlameAppearanceFile { get { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "flame-appearance.txt"); } }

        public FlameAppearance LoadFlameAppearance()
        {
            var settings = new FlameAppearance { Enabled = LoadFlameEffects() };
            try
            {
                if (File.Exists(FlameAppearanceFile))
                    foreach (string line in File.ReadAllLines(FlameAppearanceFile))
                    {
                        string[] pair = line.Split('='); int value;
                        if (pair.Length != 2 || !int.TryParse(pair[1], out value)) continue;
                        switch (pair[0])
                        {
                            case "speed": settings.Speed = value; break;
                            case "height": settings.Height = value; break;
                            case "brightness": settings.Brightness = value; break;
                            case "transparency": settings.Transparency = value; break;
                            case "hue": settings.Hue = value; break;
                        }
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            settings.Clamp();
            return settings;
        }

        public void SaveFlameAppearance(FlameAppearance settings)
        {
            var value = settings.Clone(); value.Clamp();
            Directory.CreateDirectory(Path.GetDirectoryName(FlameAppearanceFile));
            File.WriteAllLines(FlameAppearanceFile, new[] { "speed=" + value.Speed, "height=" + value.Height,
                "brightness=" + value.Brightness, "transparency=" + value.Transparency, "hue=" + value.Hue });
            SaveFlameEffects(value.Enabled);
        }

        public bool LoadFlameEffects()
        {
            try { return !File.Exists(FlameEffectsFile) || File.ReadAllText(FlameEffectsFile).Trim() != "off"; }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        public void SaveFlameEffects(bool enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FlameEffectsFile));
            File.WriteAllText(FlameEffectsFile, enabled ? "on" : "off");
        }

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
