using System.Diagnostics;

namespace PBZKeyForge
{
    public static class ProjectLinks
    {
        public const string Project = "https://github.com/muyuanjin/PBZKeyForge";
        public const string Releases = Project + "/releases/latest";
        public const string Nexus = "https://www.nexusmods.com/phantombladezero/mods/402";
        public const string Ue4ss = "https://github.com/KintaroModding/ue4ss";
        public const string Ue4ssUpstream = "https://github.com/UE4SS-RE/RE-UE4SS";
        public static void Open(string url) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    }
}
