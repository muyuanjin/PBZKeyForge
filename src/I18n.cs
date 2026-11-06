using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PBZKeyForge
{
    /// <summary>Minimal string table with culture detection. Falls back to English, then to the key itself.</summary>
    public static partial class I18n
    {
        public static readonly string[] Languages = new[] { "zh-Hans", "en", "ja", "ko", "ru" }.Concat(ExtendedLanguages()).ToArray();

        public static string Current = "en";

        static readonly Dictionary<string, Dictionary<string, string>> T =
            new Dictionary<string, Dictionary<string, string>>();

        static void Add(string lang, string key, string value)
        {
            Dictionary<string, string> d;
            if (!T.TryGetValue(lang, out d)) { d = new Dictionary<string, string>(); T[lang] = d; }
            d[key] = value;
        }

        public static string Str(string key, params object[] args)
        {
            if (args == null || args.Length == 0) return Lookup(key);
            return string.Format(Lookup(key), args);
        }

        internal static IEnumerable<string> Translations(string key)
        { return T.Values.Where(language => language.ContainsKey(key)).Select(language => language[key]).Distinct(); }

        static string Lookup(string key)
        {
            Dictionary<string, string> d;
            if (T.TryGetValue(Current, out d) && d.ContainsKey(key)) return d[key];
            if (T.TryGetValue("en", out d) && d.ContainsKey(key)) return d[key];
            return key;
        }

        /// <summary>Pick a language from the OS UI culture.</summary>
        public static string Detect()
        {
            var c = CultureInfo.CurrentUICulture;
            string extended = DetectExtendedCulture(c);
            if (extended != null) return extended;
            string n = c.TwoLetterISOLanguageName.ToLowerInvariant();
            if (n == "zh") return "zh-Hans";
            if (n == "ja") return "ja";
            if (n == "ko") return "ko";
            if (n == "ru") return "ru";
            return "en";
        }

        public static string DisplayName(string lang)
        {
            string extended = ExtendedDisplayName(lang);
            if (extended != null) return extended;
            switch (lang)
            {
                case "zh-Hans": return "中文（简体）";
                case "ja": return "日本語";
                case "ko": return "한국어";
                case "ru": return "Русский";
                default: return "English";
            }
        }

        static I18n()
        {
            // ---------------- 中文 ----------------
            Add("zh-Hans", "app.title", "影之刃零 键位工具");
            Add("zh-Hans", "lang.label", "语言");
            Add("zh-Hans", "file.label", "存档");
            Add("zh-Hans", "btn.browse", "浏览…");
            Add("zh-Hans", "btn.autofind", "自动查找");
            Add("zh-Hans", "btn.load", "载入");
            Add("zh-Hans", "tab.kb", "键鼠键位");
            Add("zh-Hans", "tab.pad", "手柄键位");
            Add("zh-Hans", "col.action", "动作");
            Add("zh-Hans", "preset.label", "预设");
            Add("zh-Hans", "btn.apply", "套用预设");
            Add("zh-Hans", "btn.save", "保存到存档");
            Add("zh-Hans", "status.hint", "先载入存档。游戏必须完全退出；保存前自动备份。");
            Add("zh-Hans", "status.loaded", "已载入。当前方案 {0}，键鼠覆盖 {1} 条，手柄 {2} 组。");
            Add("zh-Hans", "status.loadfail", "载入失败：{0}");
            Add("zh-Hans", "status.needfile", "先载入存档。");
            Add("zh-Hans", "status.running", "游戏正在运行，请完全退出后再保存。");
            Add("zh-Hans", "status.saved", "已保存。备份 {0}；回读自检通过。");
            Add("zh-Hans", "status.savefail", "保存失败：{0}");
            Add("zh-Hans", "status.applied", "已套用预设，检查网格后点保存。");
            Add("zh-Hans", "status.cleared", "已清空编辑区，保存后这些动作恢复游戏默认。");
            Add("zh-Hans", "status.notfound", "没找到存档，请手动浏览到 ProjectPBZ\\Saved\\EnhancedInputUserSettings.sav。");
            Add("zh-Hans", "warn.editor", "⚠ 改完不要在游戏里打开“键位”界面点应用或恢复默认：游戏只在自己保存时校验，它的界面会整份覆盖本工具写入的键位。");
            Add("zh-Hans", "about.title", "关于");
            Add("zh-Hans", "about.text", "直接编辑 ProjectPBZ\\Saved\\EnhancedInputUserSettings.sav 里的键位覆盖记录，绕过游戏内的冲突校验。\n\n· 留空 = 继承游戏默认\n· 同一个键可以出现在多个动作上（这就是“一键攻防”）\n· 卸载：恢复备份文件即可\n\nMIT License");

            Add("zh-Hans", "act.IA_LightAttack", "轻攻击");
            Add("zh-Hans", "act.IA_HeavyAttack", "重攻击");
            Add("zh-Hans", "act.IA_Guard", "架势/格挡");
            Add("zh-Hans", "act.IA_Dodge", "闪避");
            Add("zh-Hans", "act.IA_Jump", "跳跃/特殊移动");
            Add("zh-Hans", "act.IA_Crouch", "下蹲");
            Add("zh-Hans", "act.IA_MoveFast", "高速移动");
            Add("zh-Hans", "act.IA_LockTarget", "锁定敌人");
            Add("zh-Hans", "act.IA_Interact", "对话/交互");
            Add("zh-Hans", "act.IA_Execute", "处决敌人");
            Add("zh-Hans", "act.IA_ShadowArm", "影之武装");
            Add("zh-Hans", "act.IA_ShadowArm_LongPress", "影之武装（长按）");
            Add("zh-Hans", "act.IA_SwitchWeapon", "更换武器");
            Add("zh-Hans", "act.IA_SwitchShadowArm", "更换影之武装");
            Add("zh-Hans", "act.IA_UltimateSkill", "绝技");
            Add("zh-Hans", "act.IA_Sense_Default", "澄心洞照");
            Add("zh-Hans", "act.IA_OpenCharacterMenu", "打开角色菜单");
            Add("zh-Hans", "act.IA_OpenMapMenu", "打开地图菜单");
            Add("zh-Hans", "act.IA_UseTopItem", "使用上方药");
            Add("zh-Hans", "act.IA_UseBottomItem", "使用下方药");

            Add("zh-Hans", "preset.onekey.name", "一键攻防：攻击键同时防御");
            Add("zh-Hans", "preset.onekey.desc", "左键/X 同时触发攻击与防御，其余键位不动。");
            Add("zh-Hans", "preset.onekey_dodge.name", "一键攻防 + 防御键改闪避冲刺");
            Add("zh-Hans", "preset.onekey_dodge.desc", "在上一项基础上，把腾出来的防御键改成闪避与高速移动。");
            Add("zh-Hans", "preset.souls.name", "魂系手柄布局");
            Add("zh-Hans", "preset.souls.desc", "轻攻击 RB、重攻击 RT、防御 LB、闪避与冲刺 B、交互与处决 Y。");
            Add("zh-Hans", "preset.space_sprint.name", "空格 = 高速移动");
            Add("zh-Hans", "preset.space_sprint.desc", "键鼠额外加一个空格键给高速移动，Shift 保留。");
            Add("zh-Hans", "preset.space_dodge.name", "空格 = 闪避");
            Add("zh-Hans", "preset.space_dodge.desc", "键鼠额外加一个空格键给闪避，Shift 保留。");
            Add("zh-Hans", "preset.vanilla.name", "清除全部覆盖（恢复默认）");
            Add("zh-Hans", "preset.vanilla.desc", "清掉本工具写的键位，回到游戏默认。");
            Add("zh-Hans", "preset.vanilla_current.name", "恢复原厂");
            Add("zh-Hans", "preset.vanilla_current.desc", "仅恢复当前方案的键鼠和手柄原厂键位，其他方案不变。");

            // ---------------- English ----------------
            Add("en", "app.title", "Phantom Blade Zero Key Tool");
            Add("en", "lang.label", "Language");
            Add("en", "file.label", "Save file");
            Add("en", "btn.browse", "Browse…");
            Add("en", "btn.autofind", "Auto-detect");
            Add("en", "btn.load", "Load");
            Add("en", "tab.kb", "Keyboard & mouse");
            Add("en", "tab.pad", "Gamepad");
            Add("en", "col.action", "Action");
            Add("en", "preset.label", "Preset");
            Add("en", "btn.apply", "Apply preset");
            Add("en", "btn.save", "Save to file");
            Add("en", "status.hint", "Load a save first. The game must be closed; a backup is written before saving.");
            Add("en", "status.loaded", "Loaded. Active scheme {0}, {1} keyboard overrides, {2} gamepad profiles.");
            Add("en", "status.loadfail", "Load failed: {0}");
            Add("en", "status.needfile", "Load a save first.");
            Add("en", "status.running", "Phantom Blade Zero is running. Close it completely before saving.");
            Add("en", "status.saved", "Saved. Backup {0}; re-read check passed.");
            Add("en", "status.savefail", "Save failed: {0}");
            Add("en", "status.applied", "Preset applied. Review the grid, then save.");
            Add("en", "status.cleared", "Grid cleared. Saving now restores the game defaults for these actions.");
            Add("en", "status.notfound", "Save file not found. Browse to ProjectPBZ\\Saved\\EnhancedInputUserSettings.sav manually.");
            Add("en", "warn.editor", "⚠ After editing, do not open the in-game keybind screen and apply or reset: the game only validates on its own save, and that screen overwrites everything written here.");
            Add("en", "about.title", "About");
            Add("en", "about.text", "Edits the key override records inside ProjectPBZ\\Saved\\EnhancedInputUserSettings.sav, bypassing the in-game conflict check.\n\n· Empty = use the game default\n· One key can drive several actions (that is the whole point)\n· Uninstall: restore your backup\n\nMIT License");

            Add("en", "act.IA_LightAttack", "Light Attack");
            Add("en", "act.IA_HeavyAttack", "Heavy Attack");
            Add("en", "act.IA_Guard", "Block");
            Add("en", "act.IA_Dodge", "Dodge");
            Add("en", "act.IA_Jump", "Jump / Special Movement");
            Add("en", "act.IA_Crouch", "Crouch");
            Add("en", "act.IA_MoveFast", "Sprint");
            Add("en", "act.IA_LockTarget", "Lock On");
            Add("en", "act.IA_Interact", "Interact / Talk");
            Add("en", "act.IA_Execute", "Execute");
            Add("en", "act.IA_ShadowArm", "Phantom Edges");
            Add("en", "act.IA_ShadowArm_LongPress", "Phantom Edges (hold)");
            Add("en", "act.IA_SwitchWeapon", "Switch Weapon");
            Add("en", "act.IA_SwitchShadowArm", "Switch Phantom Edge");
            Add("en", "act.IA_UltimateSkill", "Power Surge");
            Add("en", "act.IA_Sense_Default", "Mind's Eye");
            Add("en", "act.IA_OpenCharacterMenu", "Open Character Menu");
            Add("en", "act.IA_OpenMapMenu", "Open Map");
            Add("en", "act.IA_UseTopItem", "Use Upper Medicine");
            Add("en", "act.IA_UseBottomItem", "Use Lower Medicine");

            Add("en", "preset.onekey.name", "One-key attack & guard");
            Add("en", "preset.onekey.desc", "Left mouse / X triggers attack and guard together. Everything else stays untouched.");
            Add("en", "preset.onekey_dodge.name", "One-key attack & guard + guard key becomes dodge");
            Add("en", "preset.onekey_dodge.desc", "On top of the previous preset, the freed guard key also dodges and sprints.");
            Add("en", "preset.souls.name", "Souls-style gamepad layout");
            Add("en", "preset.souls.desc", "Light RB, heavy RT, guard LB, dodge and sprint B, interact and execute Y.");
            Add("en", "preset.space_sprint.name", "Space = sprint");
            Add("en", "preset.space_sprint.desc", "Adds Space as an extra keyboard sprint key; Shift stays.");
            Add("en", "preset.space_dodge.name", "Space = dodge");
            Add("en", "preset.space_dodge.desc", "Adds Space as an extra keyboard dodge key; Shift stays.");
            Add("en", "preset.vanilla.name", "Clear all overrides (game defaults)");
            Add("en", "preset.vanilla.desc", "Removes the bindings written by this tool and returns to the shipped defaults.");
            Add("en", "preset.vanilla_current.name", "Restore factory defaults");
            Add("en", "preset.vanilla_current.desc", "Restores factory keyboard and controller bindings for this scheme only.");

            // ---------------- 日本語 ----------------
            Add("ja", "app.title", "影之刃零 キー設定ツール");
            Add("ja", "lang.label", "言語");
            Add("ja", "file.label", "セーブファイル");
            Add("ja", "btn.browse", "参照…");
            Add("ja", "btn.autofind", "自動検索");
            Add("ja", "btn.load", "読み込み");
            Add("ja", "tab.kb", "キーボード／マウス");
            Add("ja", "tab.pad", "ゲームパッド");
            Add("ja", "col.action", "アクション");
            Add("ja", "preset.label", "プリセット");
            Add("ja", "btn.apply", "プリセットを適用");
            Add("ja", "btn.save", "セーブに保存");
            Add("ja", "status.hint", "先にセーブを読み込んでください。ゲームは完全に終了し、保存前にバックアップを作成します。");
            Add("ja", "status.loaded", "読み込み完了。使用中のプリセット {0}、キーボード上書き {1} 件、ゲームパッド {2} 組。");
            Add("ja", "status.loadfail", "読み込み失敗：{0}");
            Add("ja", "status.needfile", "先にセーブを読み込んでください。");
            Add("ja", "status.running", "ゲームが起動中です。完全に終了してから保存してください。");
            Add("ja", "status.saved", "保存しました。バックアップ {0}、再読み込み検査も通過。");
            Add("ja", "status.savefail", "保存失敗：{0}");
            Add("ja", "status.applied", "プリセットを適用しました。表を確認して保存してください。");
            Add("ja", "status.cleared", "編集欄を空にしました。保存するとこれらの動作は既定に戻ります。");
            Add("ja", "status.notfound", "セーブが見つかりません。ProjectPBZ\\Saved\\EnhancedInputUserSettings.sav を手動で選択してください。");
            Add("ja", "warn.editor", "⚠ 編集後、ゲーム内のキー設定画面で適用や初期化を行うと、ここで書いた内容は上書きされます。");
            Add("ja", "about.title", "このツールについて");
            Add("ja", "about.text", "ProjectPBZ\\Saved\\EnhancedInputUserSettings.sav のキー上書き記録を直接編集し、ゲーム内の競合チェックを回避します。\n\n· 空欄 = ゲーム既定を使用\n· 1 つのキーを複数の動作に割り当て可能\n· アンインストール：バックアップを戻すだけ\n\nMIT License");

            Add("ja", "act.IA_LightAttack", "軽攻撃");
            Add("ja", "act.IA_HeavyAttack", "重攻撃");
            Add("ja", "act.IA_Guard", "構え/ガード");
            Add("ja", "act.IA_Dodge", "回避");
            Add("ja", "act.IA_Jump", "ジャンプ/特殊移動");
            Add("ja", "act.IA_Crouch", "しゃがむ");
            Add("ja", "act.IA_MoveFast", "高速移動");
            Add("ja", "act.IA_LockTarget", "ロックオン");
            Add("ja", "act.IA_Interact", "会話/インタラクト");
            Add("ja", "act.IA_Execute", "処刑");
            Add("ja", "act.IA_ShadowArm", "影の武装");
            Add("ja", "act.IA_ShadowArm_LongPress", "影の武装（長押し）");
            Add("ja", "act.IA_SwitchWeapon", "武器切替");
            Add("ja", "act.IA_SwitchShadowArm", "影の武装切替");
            Add("ja", "act.IA_UltimateSkill", "絶技");
            Add("ja", "act.IA_Sense_Default", "心眼");
            Add("ja", "act.IA_OpenCharacterMenu", "キャラクターメニュー");
            Add("ja", "act.IA_OpenMapMenu", "マップ");
            Add("ja", "act.IA_UseTopItem", "上の薬を使用");
            Add("ja", "act.IA_UseBottomItem", "下の薬を使用");

            Add("ja", "preset.onekey.name", "ワンキー攻防：攻撃キーで同時にガード");
            Add("ja", "preset.onekey.desc", "左クリック／X で攻撃とガードを同時に発生。他のキーは変更しません。");
            Add("ja", "preset.onekey_dodge.name", "ワンキー攻防＋ガードキーを回避に変更");
            Add("ja", "preset.onekey_dodge.desc", "上の内容に加え、空いたガードキーを回避とダッシュに割り当てます。");
            Add("ja", "preset.souls.name", "ソウルライク配置");
            Add("ja", "preset.souls.desc", "弱攻撃 RB、強攻撃 RT、ガード LB、回避とダッシュ B、調べる／処刑 Y。");
            Add("ja", "preset.space_sprint.name", "スペース＝ダッシュ");
            Add("ja", "preset.space_sprint.desc", "キーボードのダッシュにスペースを追加。Shift も残ります。");
            Add("ja", "preset.space_dodge.name", "スペース＝回避");
            Add("ja", "preset.space_dodge.desc", "キーボードの回避にスペースを追加。Shift も残ります。");
            Add("ja", "preset.vanilla.name", "すべての上書きを削除（既定に戻す）");
            Add("ja", "preset.vanilla.desc", "このツールが書いたキー設定を消し、ゲーム既定に戻します。");
            Add("ja", "preset.vanilla_current.name", "初期設定に戻す");
            Add("ja", "preset.vanilla_current.desc", "現在の設定のキーボードとゲームパッドだけを初期設定に戻します。");

            // ---------------- 한국어 ----------------
            Add("ko", "app.title", "그림자 무기 키 설정 도구");
            Add("ko", "lang.label", "언어");
            Add("ko", "file.label", "세이브 파일");
            Add("ko", "btn.browse", "찾아보기…");
            Add("ko", "btn.autofind", "자동 찾기");
            Add("ko", "btn.load", "불러오기");
            Add("ko", "tab.kb", "키보드/마우스");
            Add("ko", "tab.pad", "게임패드");
            Add("ko", "col.action", "동작");
            Add("ko", "preset.label", "프리셋");
            Add("ko", "btn.apply", "프리셋 적용");
            Add("ko", "btn.save", "세이브에 저장");
            Add("ko", "status.hint", "먼저 세이브를 불러오세요. 게임은 완전히 종료해야 하며, 저장 전에 백업을 만듭니다.");
            Add("ko", "status.loaded", "불러왔습니다. 사용 중인 프리셋 {0}, 키보드 덮어쓰기 {1}개, 게임패드 {2}组.");
            Add("ko", "status.loadfail", "불러오기 실패: {0}");
            Add("ko", "status.needfile", "먼저 세이브를 불러오세요.");
            Add("ko", "status.running", "게임이 실행 중입니다. 완전히 종료한 뒤 저장하세요.");
            Add("ko", "status.saved", "저장했습니다. 백업 {0}, 다시 읽기 검사 통과.");
            Add("ko", "status.savefail", "저장 실패: {0}");
            Add("ko", "status.applied", "프리셋을 적용했습니다. 표를 확인한 뒤 저장하세요.");
            Add("ko", "status.cleared", "편집 칸을 비웠습니다. 저장하면 해당 동작은 기본값으로 돌아갑니다.");
            Add("ko", "status.notfound", "세이브를 찾지 못했습니다. ProjectPBZ\\Saved\\EnhancedInputUserSettings.sav 를 직접 선택하세요.");
            Add("ko", "warn.editor", "⚠ 편집 후 게임 안의 키 설정 화면에서 적용하거나 초기화하면, 여기서 쓴 내용이 덮어써집니다.");
            Add("ko", "about.title", "정보");
            Add("ko", "about.text", "ProjectPBZ\\Saved\\EnhancedInputUserSettings.sav 의 키 덮어쓰기 기록을 직접 편집해 게임 내 충돌 검사를 우회합니다.\n\n· 비워 두면 게임 기본값 사용\n· 하나의 키를 여러 동작에 사용 가능\n· 제거: 백업 파일을 되돌리면 됩니다\n\nMIT License");

            Add("ko", "act.IA_LightAttack", "약공격");
            Add("ko", "act.IA_HeavyAttack", "강공격");
            Add("ko", "act.IA_Guard", "자세/방어");
            Add("ko", "act.IA_Dodge", "회피");
            Add("ko", "act.IA_Jump", "점프/특수 이동");
            Add("ko", "act.IA_Crouch", "앉기");
            Add("ko", "act.IA_MoveFast", "달리기");
            Add("ko", "act.IA_LockTarget", "타겟 락온");
            Add("ko", "act.IA_Interact", "대화/상호작용");
            Add("ko", "act.IA_Execute", "처형");
            Add("ko", "act.IA_ShadowArm", "팬텀 무장");
            Add("ko", "act.IA_ShadowArm_LongPress", "팬텀 무장(길게)");
            Add("ko", "act.IA_SwitchWeapon", "무기 교체");
            Add("ko", "act.IA_SwitchShadowArm", "팬텀 무장 교체");
            Add("ko", "act.IA_UltimateSkill", "궁극기");
            Add("ko", "act.IA_Sense_Default", "통찰");
            Add("ko", "act.IA_OpenCharacterMenu", "캐릭터 메뉴 열기");
            Add("ko", "act.IA_OpenMapMenu", "지도 열기");
            Add("ko", "act.IA_UseTopItem", "상단 물약 사용");
            Add("ko", "act.IA_UseBottomItem", "하단 물약 사용");

            Add("ko", "preset.onekey.name", "원키 공방: 공격 키로 동시 방어");
            Add("ko", "preset.onekey.desc", "좌클릭/X 로 공격과 방어를 동시에 발생시킵니다. 다른 키는 그대로 둡니다.");
            Add("ko", "preset.onekey_dodge.name", "원키 공방 + 방어 키를 회피로");
            Add("ko", "preset.onekey_dodge.desc", "위 설정에 더해 비워진 방어 키를 회피와 달리기에 사용합니다.");
            Add("ko", "preset.souls.name", "소울류 게임패드 배치");
            Add("ko", "preset.souls.desc", "약공격 RB, 강공격 RT, 방어 LB, 회피와 달리기 B, 상호작용/처형 Y.");
            Add("ko", "preset.space_sprint.name", "스페이스 = 달리기");
            Add("ko", "preset.space_sprint.desc", "키보드 달리기에 스페이스를 추가합니다. Shift 는 유지.");
            Add("ko", "preset.space_dodge.name", "스페이스 = 회피");
            Add("ko", "preset.space_dodge.desc", "키보드 회피에 스페이스를 추가합니다. Shift 는 유지.");
            Add("ko", "preset.vanilla.name", "모든 덮어쓰기 삭제(기본값)");
            Add("ko", "preset.vanilla.desc", "이 도구가 쓴 키 설정을 지우고 게임 기본값으로 돌립니다.");
            Add("ko", "preset.vanilla_current.name", "공장 기본값 복원");
            Add("ko", "preset.vanilla_current.desc", "현재 설정의 키보드와 게임패드만 공장 기본값으로 복원합니다.");

            // ---------------- Русский ----------------
            Add("ru", "app.title", "Настройка управления Phantom Blade Zero");
            Add("ru", "lang.label", "Язык");
            Add("ru", "file.label", "Файл сохранения");
            Add("ru", "btn.browse", "Обзор…");
            Add("ru", "btn.autofind", "Найти автоматически");
            Add("ru", "btn.load", "Загрузить");
            Add("ru", "tab.kb", "Клавиатура и мышь");
            Add("ru", "tab.pad", "Геймпад");
            Add("ru", "col.action", "Действие");
            Add("ru", "preset.label", "Пресет");
            Add("ru", "btn.apply", "Применить пресет");
            Add("ru", "btn.save", "Сохранить в файл");
            Add("ru", "status.hint", "Сначала загрузите сохранение. Игра должна быть закрыта; перед записью создаётся резервная копия.");
            Add("ru", "status.loaded", "Загружено. Активная схема {0}, переопределений клавиатуры {1}, профилей геймпада {2}.");
            Add("ru", "status.loadfail", "Ошибка загрузки: {0}");
            Add("ru", "status.needfile", "Сначала загрузите сохранение.");
            Add("ru", "status.running", "Игра запущена. Полностью закройте её перед сохранением.");
            Add("ru", "status.saved", "Сохранено. Резервная копия {0}; проверка повторным чтением пройдена.");
            Add("ru", "status.savefail", "Ошибка сохранения: {0}");
            Add("ru", "status.applied", "Пресет применён. Проверьте таблицу и сохраните.");
            Add("ru", "status.cleared", "Поля очищены. После сохранения эти действия вернутся к значениям игры.");
            Add("ru", "status.notfound", "Файл сохранения не найден. Укажите ProjectPBZ\\Saved\\EnhancedInputUserSettings.sav вручную.");
            Add("ru", "warn.editor", "⚠ После правки не открывайте экран клавиш в игре и не нажимайте «применить» или «сброс»: игра проверяет только при своём сохранении и перезапишет всё, что записано здесь.");
            Add("ru", "about.title", "О программе");
            Add("ru", "about.text", "Правит записи переопределения клавиш в ProjectPBZ\\Saved\\EnhancedInputUserSettings.sav в обход проверки конфликтов в игре.\n\n· Пустое поле = значение игры по умолчанию\n· Одна клавиша может вызывать несколько действий\n· Удаление: верните резервную копию\n\nMIT License");

            Add("ru", "act.IA_LightAttack", "Лёгкая атака");
            Add("ru", "act.IA_HeavyAttack", "Тяжёлая атака");
            Add("ru", "act.IA_Guard", "Блок");
            Add("ru", "act.IA_Dodge", "Уклонение");
            Add("ru", "act.IA_Jump", "Прыжок / особое движение");
            Add("ru", "act.IA_Crouch", "Присесть");
            Add("ru", "act.IA_MoveFast", "Ускорение");
            Add("ru", "act.IA_LockTarget", "Захват цели");
            Add("ru", "act.IA_Interact", "Взаимодействие / разговор");
            Add("ru", "act.IA_Execute", "Добивание");
            Add("ru", "act.IA_ShadowArm", "Клинок тени");
            Add("ru", "act.IA_ShadowArm_LongPress", "Клинок тени (удержание)");
            Add("ru", "act.IA_SwitchWeapon", "Смена оружия");
            Add("ru", "act.IA_SwitchShadowArm", "Смена клинка тени");
            Add("ru", "act.IA_UltimateSkill", "Ультимейт");
            Add("ru", "act.IA_Sense_Default", "Око разума");
            Add("ru", "act.IA_OpenCharacterMenu", "Меню персонажа");
            Add("ru", "act.IA_OpenMapMenu", "Карта");
            Add("ru", "act.IA_UseTopItem", "Верхнее лекарство");
            Add("ru", "act.IA_UseBottomItem", "Нижнее лекарство");

            Add("ru", "preset.onekey.name", "Атака и блок на одной клавише");
            Add("ru", "preset.onekey.desc", "ЛКМ / X вызывают атаку и блок одновременно. Остальные клавиши не меняются.");
            Add("ru", "preset.onekey_dodge.name", "То же + клавиша блока становится уклонением");
            Add("ru", "preset.onekey_dodge.desc", "Дополнительно освободившаяся клавиша блока даёт уклонение и ускорение.");
            Add("ru", "preset.souls.name", "Раскладка в стиле Souls");
            Add("ru", "preset.souls.desc", "Лёгкая RB, тяжёлая RT, блок LB, уклонение и ускорение B, взаимодействие и добивание Y.");
            Add("ru", "preset.space_sprint.name", "Пробел = ускорение");
            Add("ru", "preset.space_sprint.desc", "Добавляет пробел как вторую клавишу ускорения; Shift сохраняется.");
            Add("ru", "preset.space_dodge.name", "Пробел = уклонение");
            Add("ru", "preset.space_dodge.desc", "Добавляет пробел как вторую клавишу уклонения; Shift сохраняется.");
            Add("ru", "preset.vanilla.name", "Удалить все переопределения");
            Add("ru", "preset.vanilla.desc", "Убирает назначения, сделанные этим инструментом, и возвращает значения игры.");
            Add("ru", "preset.vanilla_current.name", "Заводские настройки");
            Add("ru", "preset.vanilla_current.desc", "Восстанавливает заводские назначения клавиатуры и геймпада только для этой схемы.");

            // ---- scheme / buttons ----
            Add("zh-Hans", "btn.activate", "启用该方案");
            Add("zh-Hans", "btn.about", "关于");
            Add("zh-Hans", "scheme.default", "默认方案");
            Add("zh-Hans", "scheme.custom1", "自定义方案 1");
            Add("zh-Hans", "scheme.custom2", "自定义方案 2");
            Add("zh-Hans", "status.scheme", "正在编辑：{0}");
            Add("zh-Hans", "scheme.label", "方案");
            Add("zh-Hans", "status.schemeunknown", "未能读取游戏的方案选择。");
            Add("zh-Hans", "status.activated", "已将「{0}」设为游戏当前方案。备份 {1}。");
            Add("zh-Hans", "status.activesame", "「{0}」已经是生效方案，存档没有改动。");

            Add("en", "btn.activate", "Activate scheme");
            Add("en", "btn.about", "About");
            Add("en", "scheme.default", "Default");
            Add("en", "scheme.custom1", "Custom 1");
            Add("en", "scheme.custom2", "Custom 2");
            Add("en", "status.scheme", "Editing: {0}");
            Add("en", "scheme.label", "Scheme");
            Add("en", "status.schemeunknown", "Could not read the game's scheme selection.");
            Add("en", "status.activated", "Set '{0}' as the current game scheme. Backup: {1}.");
            Add("en", "status.activesame", "'{0}' is already the active scheme; the save was left untouched.");

            Add("ja", "btn.activate", "このプリセットを有効化");
            Add("ja", "btn.about", "情報");
            Add("ja", "scheme.default", "既定");
            Add("ja", "scheme.custom1", "カスタム 1");
            Add("ja", "scheme.custom2", "カスタム 2");
            Add("ja", "status.scheme", "編集中：{0}");
            Add("ja", "scheme.label", "設定");
            Add("ja", "status.schemeunknown", "ゲームの設定選択を読み取れませんでした。");
            Add("ja", "status.activated", "ゲームの使用設定を「{0}」にしました。バックアップ：{1}。");
            Add("ja", "status.activesame", "「{0}」はすでに有効なプリセットです。セーブは変更していません。");

            Add("ko", "btn.activate", "이 프리셋 사용");
            Add("ko", "btn.about", "정보");
            Add("ko", "scheme.default", "기본");
            Add("ko", "scheme.custom1", "사용자 1");
            Add("ko", "scheme.custom2", "사용자 2");
            Add("ko", "status.scheme", "편집 중: {0}");
            Add("ko", "scheme.label", "프리셋");
            Add("ko", "status.schemeunknown", "게임의 프리셋 선택을 읽지 못했습니다.");
            Add("ko", "status.activated", "게임 사용 설정을 '{0}'로 지정했습니다. 백업: {1}.");
            Add("ko", "status.activesame", "'{0}' 은(는) 이미 활성 프리셋입니다. 세이브는 그대로 두었습니다.");

            Add("ru", "btn.activate", "Сделать активной");
            Add("ru", "btn.about", "О программе");
            Add("ru", "scheme.default", "По умолчанию");
            Add("ru", "scheme.custom1", "Своя 1");
            Add("ru", "scheme.custom2", "Своя 2");
            Add("ru", "status.scheme", "Редактируется: {0}");
            Add("ru", "scheme.label", "Схема");
            Add("ru", "status.schemeunknown", "Не удалось прочитать выбранную в игре схему.");
            Add("ru", "status.activated", "Схема «{0}» выбрана как текущая в игре. Копия: {1}.");
            Add("ru", "status.activesame", "Схема «{0}» уже активна; файл не изменялся.");

            // four step strip + quick action
            Add("zh-Hans", "step.find", "找到存档");
            Add("zh-Hans", "step.preset", "选择预设");
            Add("zh-Hans", "step.apply", "套用");
            Add("zh-Hans", "step.save", "保存到存档");
            Add("zh-Hans", "btn.quick", "一键攻防");
            Add("zh-Hans", "status.quick", "已套用一键攻防：攻击键同时防御。最后一步，点“保存到存档”。");

            Add("en", "step.find", "Find your save");
            Add("en", "step.preset", "Pick a preset");
            Add("en", "step.apply", "Apply");
            Add("en", "step.save", "Save to file");
            Add("en", "btn.quick", "One-key guard");
            Add("en", "status.quick", "Guard is now on your attack key. Last step: press Save to file.");

            Add("ja", "step.find", "セーブを検出");
            Add("ja", "step.preset", "プリセット選択");
            Add("ja", "step.apply", "適用");
            Add("ja", "step.save", "セーブに書き込む");
            Add("ja", "btn.quick", "攻防一体");
            Add("ja", "status.quick", "攻撃キーにガードを割り当てました。最後に「セーブに書き込む」を押してください。");

            Add("ko", "step.find", "세이브 찾기");
            Add("ko", "step.preset", "프리셋 선택");
            Add("ko", "step.apply", "적용");
            Add("ko", "step.save", "세이브에 저장");
            Add("ko", "btn.quick", "공방 일체");
            Add("ko", "status.quick", "공격 키에 방어를 넣었습니다. 마지막으로 세이브에 저장을 누르세요.");

            Add("ru", "step.find", "Найти файл");
            Add("ru", "step.preset", "Выбрать пресет");
            Add("ru", "step.apply", "Применить");
            Add("ru", "step.save", "Сохранить в файл");
            Add("ru", "btn.quick", "Блок на атаке");
            Add("ru", "status.quick", "Блок назначен на клавишу атаки. Последний шаг: Сохранить в файл.");
            EditorStrings();
            ApplyProgressStrings();
            ExtendedStrings();
            SchemeDiscoveryStrings();
            ResourceModStrings();
            KeyEnhancementStrings();
            KeyEnhancementLifecycleStrings();
            ComponentMenuStrings();
            Ue4ssUpdateStrings();
            KeyEnhancementEditorStrings();
            UnifiedBindingStrings();
            ActionCatalogStrings();
            CatalogGroupStrings();
            BindingEditingStrings();
            LiveEditingStrings();
            BindingUsabilityStrings();
            LiveConnectionStrings();
            FeedbackUpdateStrings();
            BindingWorkflowStrings();
            SchemeStrings();
            CompleteMoveStrings();
        }
    }
}




