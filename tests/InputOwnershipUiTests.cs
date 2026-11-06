using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class InputOwnershipUiTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static string Ui(string method, params object[] arguments)
    { return (string)typeof(MainForm).Assembly.GetType("PBZKeyForge.UnifiedBindingUi").GetMethod(method, Hidden).Invoke(null, arguments); }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static IEnumerable<Control> All(Control owner)
    { foreach (Control child in owner.Controls) { yield return child; foreach (Control descendant in All(child)) yield return descendant; } }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); I18n.Current = "zh-Hans";
            string output = args.Length == 0 ? "dist/ownership-ui-check" : args[0]; Directory.CreateDirectory(output);
            var saved = new BindingEntry { IsNative = true, Active = true, Keys = new[] { "J" }, ExecutionOwner = "scheduler", OriginalInputBlocked = true };
            Check(Ui("EntryExecution", saved).Contains("存档绑定") && Ui("EntryExecution", saved).Contains("统一调度"), "Saved source confused with native execution");
            Check(Ui("EntryExecution", saved).Contains("执行规划"), "Planning state claims a loaded runtime");
            Check(Ui("EntryState", saved).Contains("统一调度"), "Compact table state omits unified ownership");
            Check(Ui("EntryPolicy", saved).Contains("拦截原生") && Ui("EntryPolicy", saved).Contains("共存"), "Native replacement and custom-binding coexistence are not distinct");
            var rule = new BindingEntry { ExecutionOwner = "scheduler", Rule = new KeyEnhancementRule { ConsumeKeys = new List<string> { "Gamepad_FaceButton_Bottom" } } };
            Check(Ui("EntryExecution", rule).Contains("自定义规则"), "Custom rule source missing");
            Check(Ui("EntryPolicy", rule).Contains("仅消耗") && Ui("EntryPolicy", rule).Contains("A / ×") && !Ui("EntryPolicy", rule).Contains("LB"), "Partial consume explanation broadens blocked input");
            rule.Rule.ConsumeKeys.Clear(); rule.Rule.Consume = true; Check(Ui("EntryPolicy", rule).Contains("全部输入键"), "Full consume explanation missing");
            rule.ExecutionOwner = "inactive"; Check(Ui("EntryExecution", rule).Contains("停用") && Ui("EntryPolicy", rule) == "", "Inactive custom binding is presented as active");
            Check(Ui("EntryState", rule).Contains("停用"), "Compact table hides disabled complex binding state");
            saved.ExecutionOwner = "game"; saved.OriginalInputBlocked = false;
            Check(Ui("EntryExecution", saved).Contains("游戏原生") && Ui("EntryPolicy", saved).Contains("原有触发"), "Native mode ownership missing");
            saved.ExecutionOwner = "uncovered"; Check(Ui("EntryExecution", saved).Contains("未覆盖"), "Uncovered input presented as owned");

            var config = new KeyEnhancementConfiguration(); var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);
            Check(Ui("CoverageSummary", workspace).Contains("复杂绑定停用"), "Turning key enhancement off does not explain complex bindings");
            config.Enabled = true;
            Check(Ui("CoverageSummary", workspace).Contains("未覆盖输入") && workspace.UncoveredInputs.All(input => Ui("CoverageSummary", workspace).Contains(input)), "Coverage gaps are hidden behind a mapping count");
            Check(Ui("CoverageSummary", workspace).Contains("菜单") && Ui("CoverageSummary", workspace).Contains("游戏状态"), "Gameplay scope does not separate UI and eligibility");
            foreach (string key in new[] { "MouseScrollUp", "MouseScrollDown", "Mouse2D", "PBZ_Touchpad_SwipeUp" })
            {
                var unsupportedSession = EditorSession.CreateOffline(); unsupportedSession.SetKeyboard("IA_Jump", 0, key);
                var unsupported = new BindingWorkspace(unsupportedSession, config, config.Clone(), false, -1).Entries("IA_Jump").Single(entry => entry.IsNative && entry.Keys.Contains(key));
                Check(unsupported.ExecutionOwner == "uncovered" && !unsupported.OriginalInputBlocked && Ui("EntryExecution", unsupported).Contains("未覆盖"), "Unsupported key is represented as scheduler-owned: " + key);
            }
            using (var dialog = new UnifiedActionBindingsDialog(workspace, "IA_Jump", null, null))
            {
                dialog.Show(); Application.DoEvents();
                var explanations = All(dialog).OfType<Label>().Where(label => label.Name == "bindingExecution").ToArray();
                Console.WriteLine("Action explanation widths: " + string.Join(", ", explanations.Select(label => label.Width)));
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "binding-ownership.png")); }
                var tip = Field<ToolTip>(dialog, "tip");
                Check(explanations.Length > 0 && explanations.All(label => tip.GetToolTip(label).Contains("执行规划")), "Action details omit per-binding execution planning from tooltips");
                Check(explanations.All(label => tip.GetToolTip(label).Contains("来源") && label.Text.Contains("当前方案")), "Action details omit binding scope or source");
                Check(explanations.All(label => label.Width >= 300), "Action ownership explanation collapsed to a narrow column");
            }
            var dictionaries = (Dictionary<string, Dictionary<string, string>>)typeof(I18n).GetField("T", Hidden).GetValue(null);
            string[] names = dictionaries["zh-Hans"].Keys.Where(key => key.StartsWith("ownership.", StringComparison.Ordinal)).ToArray();
            Check(names.Length == 25 && dictionaries.Count == 15, "Ownership localization key set incomplete");
            foreach (var language in dictionaries) Check(names.All(key => language.Value.ContainsKey(key) && !string.IsNullOrWhiteSpace(language.Value[key])), "Missing ownership translation: " + language.Key);
            Console.WriteLine("InputOwnershipUiTests PASS " + checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
