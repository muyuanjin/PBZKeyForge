using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using PBZKeyForge;

// A direct dictionary check catches missing translations even when Lookup would
// silently return English. Compile with all three I18n partial source files.
public static class I18nTranslationTests
{
    static int assertions;
    static readonly Regex Placeholder = new Regex(@"\{(\d+)(?:[^{}]*)\}");

    static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    static string Slots(string value)
    {
        return string.Join(",", Placeholder.Matches(value).Cast<Match>().Select(match => match.Groups[1].Value).Distinct().OrderBy(item => item));
    }

    static void Detect(string culture, string expected)
    {
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
        Assert(I18n.Detect() == expected, "Culture " + culture + " must select " + expected);
    }

    public static int Main()
    {
        string previous = I18n.Current;
        CultureInfo previousCulture = Thread.CurrentThread.CurrentUICulture;
        try
        {
            string[] official = { "en", "zh-Hans", "fr", "de", "es-ES", "ru", "ja", "zh-Hant", "pt-BR", "ko", "it", "th", "vi", "pt-PT", "es-419" };
            Assert(I18n.Languages.Length >= official.Length, "At least all fifteen officially listed interface languages must be available");
            Assert(I18n.Languages.Distinct().Count() == I18n.Languages.Length, "No duplicate language codes");
            Assert(official.All(I18n.Languages.Contains), "Every official interface language, including regional variants, is selectable");
            var table = (Dictionary<string, Dictionary<string, string>>)typeof(I18n).GetField("T", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var english = table["en"];
            object[] sample = { "{SCHEME}", 2, 3, "{DRAFT}", "{GAME}", 6, 7, 8 };
            foreach (string language in I18n.Languages)
            {
                Assert(table.ContainsKey(language), "Missing language dictionary: " + language);
                Assert(!string.IsNullOrWhiteSpace(I18n.DisplayName(language)), "Missing native language name: " + language);
                if (language != "en") Assert(I18n.DisplayName(language) != "English", "Language name incorrectly falls back to English: " + language);
                I18n.Current = language;
                foreach (var pair in english)
                {
                    string value;
                    Assert(table[language].TryGetValue(pair.Key, out value) && !string.IsNullOrWhiteSpace(value), "English fallback would hide missing translation: " + language + " / " + pair.Key);
                    Assert(Slots(value) == Slots(pair.Value), "Changed format placeholders: " + language + " / " + pair.Key);
                    string formatted = I18n.Str(pair.Key, sample);
                    Assert(formatted != pair.Key && formatted.IndexOf('\ufffd') < 0, "Invalid localized result: " + language + " / " + pair.Key);
                }
                Assert(table[language]["preset.onekey.desc"].Contains("LB") && table[language]["preset.onekey.desc"].Contains("X"), "Additive guard description must mention both original LB and new X: " + language);
            }
            Detect("zh-TW", "zh-Hant"); Detect("zh-HK", "zh-Hant"); Detect("zh-MO", "zh-Hant"); Detect("zh-Hant", "zh-Hant");
            Detect("zh-CN", "zh-Hans"); Detect("zh-SG", "zh-Hans");
            Detect("fr-CA", "fr"); Detect("de-AT", "de"); Detect("it-IT", "it");
            Detect("es-ES", "es-ES"); Detect("es-MX", "es-419"); Detect("es-AR", "es-419");
            Detect("pt-PT", "pt-PT"); Detect("pt-BR", "pt-BR"); Detect("th-TH", "th"); Detect("vi-VN", "vi");
            Detect("ja-JP", "ja"); Detect("ko-KR", "ko"); Detect("ru-RU", "ru"); Detect("en-US", "en");
            Assert(table["zh-Hans"]["act.IA_Sense_Default"] == "澄心洞照", "Simplified Chinese action name must match the game's controls screen");
            Assert(table["zh-Hant"]["act.IA_Sense_Default"] == "澄心洞照", "Traditional Chinese must preserve the official action term");
            Assert(table["en"]["act.IA_Guard"] == "Block", "Existing official English action name changed");
            Assert(!table["pt-PT"]["editor.keyboard"].Contains("mouse") && table["pt-BR"]["editor.keyboard"].Contains("mouse"), "Portuguese regional computer terminology must remain distinct");
            Assert(!table["es-419"]["editor.keyboard"].Contains("ratón") && table["es-ES"]["editor.keyboard"].Contains("ratón"), "Spanish regional computer terminology must remain distinct");
            Console.WriteLine("I18nTranslationTests PASS: " + assertions + " assertions; " + I18n.Languages.Length + " languages; " + english.Count + " keys per language, no missing-key fallback.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { I18n.Current = previous; Thread.CurrentThread.CurrentUICulture = previousCulture; }
    }
}
