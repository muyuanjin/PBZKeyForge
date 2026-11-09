using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PBZKeyForge;

class SearchDialogPerformanceTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, Hidden).GetValue(owner); }
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    [STAThread]
    static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            I18n.Current = "zh-Hans";
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);

            var catalogTimer = Stopwatch.StartNew();
            int catalogCount = workspace.FunctionChoices.Length;
            catalogTimer.Stop();

            var constructorTimer = Stopwatch.StartNew();
            using (var dialog = new UnifiedActionChoiceDialog(workspace, "K", null))
            {
                constructorTimer.Stop();
                dialog.Show();
                Application.DoEvents();
                var search = Field<TextBox>(dialog, "search");
                var functions = Field<ListBox>(dialog, "functions");
                Check(functions.Items.Count == catalogCount, "The enabled picker did not initially show the complete catalog.");

                string[] phoenix = { "S", "Sn", "Sna", "Snak", "Snake", "SnakeD", "SnakeDu", "SnakeDua", "SnakeDual", "SnakeDual ", "SnakeDual D", "SnakeDual DB" };
                string[] bronze = { "青", "青铜", "青铜剑", "青铜剑 ", "青铜剑 释", "青铜剑 释放" };
                double[] snakeTiming = Measure(search, functions, phoenix);
                string snakeFirst = functions.Items.Count == 0 ? null : ((BindingFunction)functions.Items[0]).Id;
                double[] bronzeTiming = Measure(search, functions, bronze);
                bool bronzeMatch = functions.Items.Cast<BindingFunction>().Any(function => function.Id.EndsWith(".Release2", StringComparison.Ordinal));
                double[] components = MeasureComponents(workspace, functions, phoenix);
                Check(snakeFirst == "move:Weapon.Weapon_Hun_SnakeDual.GA_Hun_SnakeDual_DB",
                    "Typing the snake-dual DB query did not leave the best match selected first.");
                Check(bronzeMatch,
                    "Typing the bronze-sword release query lost the intended release action.");

                Console.WriteLine("Catalog: {0} functions in {1:0.0} ms", catalogCount, catalogTimer.Elapsed.TotalMilliseconds);
                Console.WriteLine("Dialog construction: {0:0.0} ms", constructorTimer.Elapsed.TotalMilliseconds);
                Console.WriteLine("Typing burst: SnakeDual key event {0:0.0} ms, final refresh {1:0.0} ms; BronzeSword key event {2:0.0} ms, final refresh {3:0.0} ms",
                    snakeTiming[0], snakeTiming[1], bronzeTiming[0], bronzeTiming[1]);
                Console.WriteLine("Typing components: match {0:0.0} ms, list replacement {1:0.0} ms", components[0], components[1]);
            }
            Console.WriteLine("SearchDialogPerformanceTests PASS " + checks);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    static double[] Measure(TextBox search, ListBox functions, string[] values)
    {
        double keyTotal = 0;
        foreach (string value in values)
        {
            var timer = Stopwatch.StartNew();
            search.Text = value;
            timer.Stop();
            keyTotal += timer.Elapsed.TotalMilliseconds;
        }
        System.Threading.Thread.Sleep(130);
        var refresh = Stopwatch.StartNew();
        Application.DoEvents();
        refresh.Stop();
        if (!functions.Enabled) throw new Exception("The debounced search did not refresh the action list.");
        return new[] { keyTotal / values.Length, refresh.Elapsed.TotalMilliseconds };
    }

    static double[] MeasureComponents(BindingWorkspace workspace, ListBox functions, string[] values)
    {
        double matchTotal = 0, listTotal = 0;
        foreach (string value in values)
        {
            var timer = Stopwatch.StartNew();
            BindingFunction[] matches = workspace.FindFunctionChoices(value);
            timer.Stop();
            matchTotal += timer.Elapsed.TotalMilliseconds;

            object[] items = matches.Cast<object>().ToArray();
            timer.Restart();
            functions.BeginUpdate();
            functions.Items.Clear();
            functions.Items.AddRange(items);
            functions.EndUpdate();
            timer.Stop();
            listTotal += timer.Elapsed.TotalMilliseconds;
        }
        return new[] { matchTotal / values.Length, listTotal / values.Length };
    }
}
