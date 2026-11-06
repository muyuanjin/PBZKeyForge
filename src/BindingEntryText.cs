using System;
using System.Linq;

namespace PBZKeyForge
{
    internal static class BindingEntryText
    {
        static string Text(string key) { return I18n.Str("unified." + key); }
        internal static string BindingText(BindingEntry entry)
        {
            string input = entry.Trigger == "sequence" && entry.Rule != null && entry.Rule.Steps.Count > 0
                ? string.Join(" → ", entry.Rule.Steps.Select(step => string.Join(" + ", step.Select(BindingButton.FriendlyKey))))
                : !string.IsNullOrEmpty(entry.Axis) ? BindingButton.FriendlyKey(entry.Axis) : string.Join(" + ", entry.Keys.Select(BindingButton.FriendlyKey));
            if (entry.Trigger == "default" || string.IsNullOrEmpty(entry.Trigger)) return input;
            string name = Text("trigger." + entry.Trigger);
            if (entry.Trigger == "tap" || entry.Trigger == "hold") name += " " + entry.Seconds.ToString("0.###") + " s";
            if (entry.Trigger == "multi_tap") name += " ×" + entry.Count;
            if (entry.Trigger == "pulse" || entry.Trigger == "sequence" || entry.Trigger == "multi_tap") name += " " + entry.Interval.ToString("0.###") + " s";
            return input + " · " + name;
        }
        internal static string EntryDescription(BindingEntry entry)
        {
            string result = BindingText(entry) + (entry.Suppressed ? " · " + Text("removed") : !entry.Enabled || !entry.Active ? " · " + Text("inactive") : "");
            if (entry.Priority != 0) result += " · " + Text("priority") + " " + entry.Priority;
            if (entry.Rule != null && (entry.Rule.Consume || entry.Rule.ConsumeKeys.Count > 0)) result += " · " + Text("consume") + " " + string.Join("+", entry.Rule.ConsumeKeys.Select(BindingButton.FriendlyKey));
            return result;
        }
    }
}
