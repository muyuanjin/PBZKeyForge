using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PBZKeyForge
{
    internal static class SaveDiscovery
    {
        const string SaveName = @"ProjectPBZ\Saved\EnhancedInputUserSettings.sav";

        public static EditorSession Open(EditorPreferences preferences, bool useLastPath)
        {
            return OpenCandidates(preferences, Candidates(preferences, useLastPath));
        }

        static IEnumerable<string> Candidates(EditorPreferences preferences, bool useLastPath)
        {
            if (useLastPath) yield return preferences.LoadInputPath();
            foreach (string path in InstalledSaves(SteamRoot(), FallbackLibraries())) yield return path;
        }

        static EditorSession OpenCandidates(EditorPreferences preferences, IEnumerable<string> candidates)
        {
            Exception failure = null;
            foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) continue;
                EditorSession session;
                try { session = EditorSession.Open(path); }
                catch (Exception ex) { failure = ex; continue; }
                preferences.SaveInputPath(session.InputPath);
                return session;
            }
            if (failure != null) throw failure;
            return null;
        }

        static IEnumerable<string> InstalledSaves(string steamRoot, IEnumerable<string> fallbackLibraries)
        {
            var libraries = new List<string>();
            if (!string.IsNullOrEmpty(steamRoot))
            {
                libraries.Add(steamRoot);
                string folders = ReadText(Path.Combine(steamRoot, @"steamapps\libraryfolders.vdf"));
                foreach (Match match in Regex.Matches(folders, "\"path\"\\s*\"([^\"]+)\""))
                    libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
            }
            // Steam manifests take precedence over leftover directories from older installations.
            foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (string path in ManifestSave(library)) yield return path;
            foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
                yield return Path.Combine(library, @"steamapps\common\PBZ", SaveName);
            foreach (string library in fallbackLibraries)
            {
                foreach (string path in ManifestSave(library)) yield return path;
                yield return Path.Combine(library, @"steamapps\common\PBZ", SaveName);
            }
        }

        static IEnumerable<string> ManifestSave(string library)
        {
            string manifest = ReadText(Path.Combine(library, @"steamapps\appmanifest_4115450.acf"));
            var match = Regex.Match(manifest, "\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (match.Success) yield return Path.Combine(library, @"steamapps\common", match.Groups[1].Value, SaveName);
        }

        static string ReadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
        }

        static string SteamRoot()
        {
            string root = RegistryValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            if (!string.IsNullOrEmpty(root)) return root;
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                return RegistryValue(machine, @"SOFTWARE\Valve\Steam", "InstallPath");
        }

        static string RegistryValue(RegistryKey hive, string subkey, string name)
        {
            try
            {
                using (var key = hive.OpenSubKey(subkey)) return key == null ? null : key.GetValue(name) as string;
            }
            catch (SecurityException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        static IEnumerable<string> FallbackLibraries()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                string library = null;
                try { if (drive.DriveType == DriveType.Fixed) library = Path.Combine(drive.Name, "SteamLibrary"); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (library != null) yield return library;
            }
        }
    }
}
