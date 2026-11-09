using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PBZKeyForge;

class SearchOptimizationTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static string[] Ids(IEnumerable<BindingFunction> functions) { return functions.Select(function => function.Id).ToArray(); }

    static int Main()
    {
        try
        {
            I18n.Current = "zh-Hans";
            var config = new KeyEnhancementConfiguration { Enabled = true };
            var workspace = new BindingWorkspace(EditorSession.CreateOffline(), config, config.Clone(), false, -1);

            var catalogTimer = Stopwatch.StartNew();
            int catalogCount = workspace.Functions.Length;
            catalogTimer.Stop();
            Check(catalogCount > 1000, "The performance fixture did not build the complete action catalog.");

            var bronze = workspace.FindFunctions("青铜剑 释放");
            var reverseBronze = workspace.FindFunctions("释放   青铜剑");
            Check(Ids(bronze).OrderBy(id => id).SequenceEqual(Ids(reverseBronze).OrderBy(id => id)),
                "Whitespace-separated search terms must be order independent.");
            Check(bronze.Any(function => function.Id.EndsWith(".Release1", StringComparison.Ordinal))
                && bronze.Any(function => function.Id.EndsWith(".Release2", StringComparison.Ordinal)),
                "Multiple terms split across labels and technical fields did not find both release stages.");
            Check(reverseBronze[0].Id.EndsWith(".Release2", StringComparison.Ordinal),
                "The player-facing release label was not ranked ahead of unrelated technical matches.");

            var movement = workspace.FindFunctions("WASD 向后");
            Check(movement.Length > 0 && movement[0].Id == "movement:backward",
                "Multiple player aliases did not rank movement:backward first.");

            var snake = workspace.FindFunctions("SnakeDual DB");
            Check(snake.Length > 0 && snake[0].Id == "move:Weapon.Weapon_Hun_SnakeDual.GA_Hun_SnakeDual_DB",
                "The snake-dual DB action was not ranked first.");

            var pipa = workspace.FindFunctions("催心 琵琶");
            Check(pipa.Length > 0 && pipa[0].Label.IndexOf("催心琵琶", StringComparison.Ordinal) >= 0,
                "The localized shadow-weapon name did not lead its related actions.");

            var mixedCase = workspace.FindFunctions("ia_JUMP");
            Check(mixedCase.Length > 0 && mixedCase[0].Id == "IA_Jump",
                "Search did not ignore case for an exact action identity.");

            var wasd = workspace.FindFunctions("WASD");
            Check(wasd.Length >= 5 && wasd.Take(4).Select(function => function.Id).SequenceEqual(
                new[] { "movement:forward", "movement:backward", "movement:left", "movement:right" }),
                "Player aliases and exact names must rank ahead of technical keyboard fields.");
            Check(wasd.Any(function => function.Id.IndexOf("KeyboardMoveInput", StringComparison.Ordinal) >= 0),
                "The raw keyboard mapping disappeared from the WASD results.");

            Check(workspace.FindFunctions("青铜剑 不存在词").Length == 0,
                "A query missing one of its required terms returned unrelated actions.");

            var cached = workspace.FindFunctions("SnakeDual DB");
            var cachedAgain = workspace.FindFunctions("  snakedual   dB  ");
            Check(object.ReferenceEquals(cached, cachedAgain), "An equivalent consecutive query was not served from the cache.");

            config.CustomFunctions.Add(new KeyEnhancementOutput
            {
                Id = "search-optimization-probe",
                Kind = "property",
                Name = "缓存探针动作",
                Property = "SearchOptimizationProbe",
                Value = true
            });
            Check(workspace.FindFunctions("缓存探针动作").Any(function => function.Label == "缓存探针动作"),
                "Adding an action did not invalidate the previous search result.");
            config.CustomFunctions.Clear();
            Check(workspace.FindFunctions("缓存探针动作").Length == 0,
                "Removing an action retained it in the search cache.");

            string[] queries =
            {
                "a", "青铜剑", "SnakeDual", "释放", "WASD", "琵琶", "action", "attack",
                "防御", "瞬身", "heavy", "sword", "移动", "skill", "Ability", "camera",
                "菜单", "拳", "影之武装", "Dodge"
            };
            var timings = new List<double>();
            foreach (string query in queries)
            {
                var timer = Stopwatch.StartNew();
                workspace.FindFunctions(query);
                timer.Stop();
                timings.Add(timer.Elapsed.TotalMilliseconds);
            }
            workspace.FindFunctions("SnakeDual DB");
            var cachedTimer = Stopwatch.StartNew();
            workspace.FindFunctions("SnakeDual DB");
            cachedTimer.Stop();

            Console.WriteLine("Catalog: {0} functions in {1:0.0} ms", catalogCount, catalogTimer.Elapsed.TotalMilliseconds);
            Console.WriteLine("Uncached search: average {0:0.00} ms, median {1:0.00} ms, max {2:0.00} ms",
                timings.Average(), timings.OrderBy(value => value).ElementAt(timings.Count / 2), timings.Max());
            Console.WriteLine("Cached search: {0:0.000} ms", cachedTimer.Elapsed.TotalMilliseconds);
            Console.WriteLine("SearchOptimizationTests PASS " + checks);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
