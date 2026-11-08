using System.Linq;

namespace PBZKeyForge
{
    internal static class BindingRestrictionKinds
    {
        internal static readonly string[] All = { "equipment", "training", "space", "stance", "predecessor", "cooldown" };
        internal static string Label(string kind) { return I18n.Str("binding.restrictions." + kind); }
        internal static string Describe(string[] kinds) { return string.Join(" / ", kinds.Select(Label)); }
    }
}
