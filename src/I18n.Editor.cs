namespace PBZKeyForge
{
    public static partial class I18n
    {
        static void Editor(string key, params string[] values)
        {
            for (int i = 0; i < values.Length; i++) Add(Languages[i], key, values[i]);
        }
        static void EditorStrings()
        {
            Editor("editor.subtitle", "PBZKeyForge / 键位设置", "PBZKeyForge / Key bindings", "PBZKeyForge / キー設定", "PBZKeyForge / 키 설정", "PBZKeyForge / Управление");
            Editor("editor.yes", "是", "Yes", "はい", "예", "Да");
            Editor("editor.no", "否", "No", "いいえ", "아니요", "Нет");
            Editor("editor.minimize", "最小化", "Minimize", "最小化", "최소화", "Свернуть");
            Editor("editor.maximize", "最大化", "Maximize", "最大化", "최대화", "Развернуть");
            Editor("editor.restore", "还原窗口", "Restore window", "元のサイズに戻す", "창 복원", "Восстановить окно");
            Editor("editor.close", "关闭", "Close", "閉じる", "닫기", "Закрыть");
            Editor("preset.capacity", "{0}：防御绑定槽位已满，无法保留现有绑定并新增攻击键防御。请先腾出足够槽位；本次配置未修改任何键位。", "{0}: guard binding slots are full. Free enough slots to keep existing bindings and add attack-key guarding. This configuration changed nothing.", "{0}：防御の割り当て枠が埋まっています。既存の割り当てを保持して攻撃キーにも防御を追加するには、必要な枠を空けてください。今回の設定は何も変更していません。", "{0}: 방어 바인딩 슬롯이 모두 사용 중입니다. 기존 바인딩을 유지하고 공격 키 방어를 추가하려면 충분한 슬롯을 비우세요. 이번 설정은 아무 키도 변경하지 않았습니다.", "{0}: назначения блока заполнены. Освободите достаточно мест, чтобы добавить блок на клавишу атаки и сохранить прежние назначения. Эта настройка ничего не изменила.");
            Editor("editor.title", "影之刃零 · 键位设置", "Phantom Blade Zero · Controls", "影之刃零 · キー設定", "팬텀 블레이드 제로 · 키 설정", "Phantom Blade Zero · Управление");
            Editor("editor.refresh", "刷新", "Refresh", "更新", "새로 고침", "Обновить");
            Editor("editor.apply", "应用更改", "Apply changes", "変更を適用", "변경 적용", "Применить");
            Editor("editor.more", "更多", "More", "その他", "더 보기", "Ещё");
            Editor("editor.quick", "快速配置", "Quick setup", "クイック設定", "빠른 설정", "Готовая схема");
            Editor("editor.undo", "撤销更改", "Undo changes", "変更を取り消す", "변경 취소", "Отменить правки");
            Editor("editor.binding", "绑定 {0}", "Binding {0}", "割り当て {0}", "바인딩 {0}", "Назначение {0}");
            Editor("editor.keyboard", "键鼠", "Keys / mouse", "キーボード", "키보드 / 마우스", "Клавиатура");
            Editor("editor.gamepad", "手柄", "Gamepad", "ゲームパッド", "게임패드", "Геймпад");
            Editor("editor.current", "游戏当前", "In game", "ゲーム使用中", "게임 사용 중", "В игре");
            Editor("editor.running", "游戏运行中 · 退出后可应用", "Game running · close to apply", "ゲーム起動中 · 終了後に適用可能", "게임 실행 중 · 종료 후 적용 가능", "Игра запущена · закройте для записи");
            Editor("editor.closed", "游戏已退出", "Game closed", "ゲーム終了済み", "게임 종료됨", "Игра закрыта");
            Editor("editor.location", "存档位置   {0}", "Save location   {0}", "保存先   {0}", "저장 위치   {0}", "Сохранение   {0}");
            Editor("editor.nofile", "未选择存档", "No save selected", "セーブ未選択", "선택된 세이브 없음", "Файл не выбран");
            Editor("editor.empty", "没有找到游戏存档。请选择游戏的键位存档。", "No game save found. Choose the game's input save.", "セーブが見つかりません。キー設定のセーブを選択してください。", "게임 세이브를 찾지 못했습니다. 키 설정 세이브를 선택하세요.", "Сохранение не найдено. Выберите файл настроек управления игры.");
            Editor("editor.choose", "选择存档…", "Choose save…", "セーブを選択…", "세이브 선택…", "Выбрать файл…");
            Editor("editor.hint", "默认：继承游戏键位 · 点击绑定可修改 · 允许多个动作共用按键", "Default: inherit game binding · Click a binding to edit · Shared keys are allowed", "既定：ゲームの割り当てを継承 · クリックで編集 · 同じキーを複数動作に使用可能", "기본: 게임 키 설정 유지 · 클릭하여 편집 · 같은 키를 여러 동작에 사용 가능", "По умолчанию: клавиши игры · Нажмите назначение для правки · Общие клавиши разрешены");
            Editor("editor.backup", "应用前自动备份", "Backed up before applying", "適用前に自動バックアップ", "적용 전 자동 백업", "Резервная копия до записи");
            Editor("editor.nopreset", "不使用快速配置", "No quick setup", "クイック設定なし", "빠른 설정 사용 안 함", "Без готовой схемы");
            Editor("editor.activate", "设为游戏当前方案：「{0}」", "Set game scheme to {0}", "ゲームの使用設定を「{0}」にする", "게임 사용 설정을 '{0}'로 지정", "Использовать в игре: {0}");
            Editor("editor.activatehelp", "仅勾选不会立即启用；点击“应用更改”后生效。取消勾选可只修改键位。", "The selection is saved with Apply changes. Leave unchecked to edit without activating.", "チェック後「変更を適用」で保存します。オフならキー設定のみ保存します。", "변경 적용을 누르면 사용 설정이 저장됩니다. 체크하지 않으면 키만 편집합니다.", "Выбор сохраняется кнопкой «Применить». Снимите флажок, чтобы изменить только клавиши.");
            Editor("editor.scope", "{0} 待应用 · 键鼠 {1} 项 · 手柄 {2} 项", "{0} pending · keyboard: {1} · gamepad: {2}", "{0} 適用待ち · キー {1} 件 · パッド {2} 件", "{0} 적용 대기 · 키 {1}개 · 패드 {2}개", "{0} к записи · клавиши: {1} · геймпад: {2}");
            Editor("editor.clean", "{0} · 没有未应用的更改", "{0} · No pending changes", "{0} · 未適用の変更なし", "{0} · 적용 대기 중인 변경 없음", "{0} · Нет ожидающих изменений");
            Editor("editor.activationscope", " · 含启用方案", " · includes scheme activation", " · 使用設定の変更を含む", " · 사용 설정 변경 포함", " · со сменой активной схемы");
            Editor("editor.resetscope", "待应用 · 全部方案的键鼠和手柄恢复默认（{0} 项）", "Pending · Reset keyboard and gamepad in ALL schemes ({0} changes)", "適用待ち · 全設定のキーとパッドを初期化（{0} 件）", "적용 대기 · 모든 설정의 키 및 게임패드 초기화 ({0}개)", "К записи · Сброс клавиатуры и геймпада во ВСЕХ схемах ({0})");
            Editor("editor.ready", "切换方案自动读取最新设置；编辑后统一应用。", "Switching schemes reads the latest settings. Apply when ready.", "設定を切り替えると最新データを読み込みます。編集後に適用してください。", "설정 전환 시 최신 데이터를 읽습니다. 편집 후 적용하세요.", "При смене схемы читаются свежие данные. После правок нажмите «Применить».");
            Editor("editor.exittoapply", "退出游戏后可应用；现在可以查看和编辑草稿。", "Close the game to apply. You can view and edit drafts now.", "適用にはゲームを終了してください。表示と下書きの編集は可能です。", "적용하려면 게임을 종료하세요. 지금은 초안을 보고 편집할 수 있습니다.", "Закройте игру для записи. Просмотр и правки черновиков доступны.");
            Editor("editor.schemeunknown", "无法验证游戏方案（版本不支持或认证失败）。键位仍可编辑，请在游戏内手动切换方案。", "The game scheme could not be verified (unsupported build or authentication failure). Bindings remain editable; switch schemes in game.", "ゲーム設定を検証できません（未対応ビルドまたは認証失敗）。キー編集は可能です。ゲーム内で設定を切り替えてください。", "게임 설정을 확인할 수 없습니다(미지원 빌드 또는 인증 실패). 키 편집은 가능하며 게임에서 직접 변경하세요.", "Не удалось проверить схему игры (неподдерживаемая сборка или ошибка аутентификации). Клавиши доступны для редактирования; смените схему в игре.");
            Editor("editor.refreshed", "已读取最新设置，未应用的更改已保留。", "Latest settings read. Pending changes preserved.", "最新設定を読み込みました。未適用の変更は保持されています。", "최신 설정을 읽었습니다. 적용 대기 변경은 유지됩니다.", "Свежие данные прочитаны. Несохранённые правки сохранены в черновике.");
            Editor("editor.refreshfail", "刷新未完成，草稿已保留：{0}", "Refresh failed; drafts preserved: {0}", "更新できませんでした。下書きは保持されています：{0}", "새로 고침 실패. 초안 유지됨: {0}", "Не удалось обновить; черновики сохранены: {0}");
            Editor("editor.failed", "未完成：{0}", "Could not complete: {0}", "完了できませんでした：{0}", "완료하지 못함: {0}", "Не выполнено: {0}");
            Editor("editor.saved", "更改已保存并验证。", "Changes saved and verified.", "変更を保存・検証しました。", "변경 저장 및 검증 완료.", "Правки записаны и проверены.");
            Editor("editor.partial", "键位已保存，但启用方案失败；重试只处理启用：{0}", "Bindings saved; activation failed. Retry will only activate the scheme: {0}", "キーは保存済みですが、有効化に失敗しました。再試行では有効化のみ行います：{0}", "키 저장 완료, 활성화 실패. 재시도 시 활성화만 수행합니다: {0}", "Клавиши записаны, смена схемы не удалась. Повтор затронет только схему: {0}");
            Editor("editor.preview", "预览：{0}", "Preview: {0}", "プレビュー：{0}", "미리 보기: {0}", "Предпросмотр: {0}");
            Editor("editor.previewrule", "连续选择会替换上次预览；手动改键或切换方案后，结果作为自定义保留。", "Consecutive choices replace the preview. Manual edits or scheme switches keep it as customized.", "続けて選択するとプレビューを置き換えます。手動編集や設定切替後はカスタムとして保持します。", "연속 선택 시 미리 보기를 교체합니다. 수동 편집 또는 설정 전환 후에는 사용자 지정으로 유지됩니다.", "Выбор подряд заменяет предпросмотр. После ручной правки или смены схемы результат остаётся своим.");
            Editor("editor.directinfo", "直接点击绑定修改；选择快速配置可先预览效果，不会立即保存。", "Click a binding to edit. Quick setup previews changes without saving.", "割り当てをクリックして編集。クイック設定は保存せずにプレビューします。", "바인딩을 클릭하여 편집하세요. 빠른 설정은 저장 없이 미리 봅니다.", "Нажмите назначение для правки. Готовая схема сначала показывает изменения без записи.");
            Editor("editor.sharedinfo", "仅编辑当前方案的手柄键位；同方案的多组记录有差异时，未修改项保留各自值。", "Edit this scheme's gamepad only. Different records in this scheme keep their values unless edited.", "パッド編集は現在の設定のみ。同じ設定内で記録の値が異なる場合、未編集の値は個別に保持します。", "현재 설정의 게임패드만 편집합니다. 같은 설정 내 기록의 값이 달라도 편집하지 않은 값은 각각 유지됩니다.", "Правки геймпада относятся только к этой схеме. Разные значения её записей сохраняются без правки.");
            Editor("editor.resetinfo", "全部恢复默认待应用：可查看、刷新；请先应用或撤销，再继续编辑。", "Reset all is pending. View or refresh; apply or undo the reset before editing.", "全初期化の適用待ちです。表示と更新は可能です。適用または取り消してから編集してください。", "전체 초기화 적용 대기 중입니다. 보기와 새로 고침은 가능합니다. 적용하거나 취소한 후 편집하세요.", "Ожидается общий сброс. Просмотр и обновление доступны; примените или отмените сброс для правки.");
            Editor("editor.conflicts", "处理 {0} 项冲突…", "Resolve {0} conflicts…", "競合 {0} 件を解決…", "충돌 {0}개 해결…", "Разрешить конфликты ({0})…");
            Editor("editor.conflictinfo", "游戏和草稿修改了同一绑定。请逐项选择要保留的值，再应用。", "The game and draft changed the same binding. Choose which value to keep before applying.", "ゲームと下書きが同じ割り当てを変更しました。保持する値を選んでから適用してください。", "게임과 초안에서 같은 바인딩을 변경했습니다. 유지할 값을 선택한 후 적용하세요.", "Игра и черновик изменили одно назначение. Перед записью выберите нужное значение.");
            Editor("editor.conflicttitle", "解决绑定冲突", "Resolve binding conflict", "割り当ての競合を解決", "바인딩 충돌 해결", "Конфликт назначения");
            Editor("editor.resolve", "{0} · {1} · 绑定 {2}\n\n草稿：{3}\n游戏：{4}\n\n是：保留草稿\n否：采用游戏值\n取消：稍后处理", "{0} · {1} · Binding {2}\n\nDraft: {3}\nGame: {4}\n\nYes: keep draft\nNo: use game value\nCancel: resolve later", "{0} · {1} · 割り当て {2}\n\n下書き：{3}\nゲーム：{4}\n\nはい：下書きを保持\nいいえ：ゲームの値を使用\nキャンセル：後で解決", "{0} · {1} · 바인딩 {2}\n\n초안: {3}\n게임: {4}\n\n예: 초안 유지\n아니요: 게임 값 사용\n취소: 나중에 해결", "{0} · {1} · Назначение {2}\n\nЧерновик: {3}\nИгра: {4}\n\nДа: оставить черновик\nНет: взять из игры\nОтмена: решить позже");
            Editor("editor.drafttitle", "有未应用的更改", "Pending changes", "未適用の変更", "적용 대기 중인 변경", "Несохранённые изменения");
            Editor("editor.discard", "所有方案中仍有未应用的更改。放弃这些草稿并继续吗？", "There are pending changes across your schemes. Discard all drafts and continue?", "未適用の変更があります。全設定の下書きを破棄して続行しますか？", "적용하지 않은 변경이 있습니다. 모든 설정의 초안을 버리고 계속할까요?", "В схемах остались правки. Отбросить все черновики и продолжить?");
            Editor("editor.changefile", "更换存档…", "Change save…", "セーブを変更…", "세이브 변경…", "Сменить файл…");
            Editor("editor.rediscover", "重新查找游戏", "Find game again", "ゲームを再検出", "게임 다시 찾기", "Найти игру заново");
            Editor("editor.resetall", "全部恢复默认…", "Reset all to defaults…", "すべて既定に戻す…", "모두 기본값으로 복원…", "Сбросить всё…");
            Editor("editor.resetconfirm", "将全部方案的键鼠和手柄恢复默认。\n\n这会加入待应用更改，不会立即保存；可撤销并恢复之前的草稿。游戏当前方案不变。", "Reset keyboard and gamepad in ALL schemes.\n\nThis stages a draft without saving. Undo restores your previous drafts. The active game scheme stays the same.", "全設定のキーボードとパッドを既定に戻します。\n\n保存前の下書きとして追加します。取り消すと元の下書きに戻ります。使用中の設定は変更しません。", "모든 설정의 키보드와 게임패드를 기본값으로 복원합니다.\n\n저장 없이 초안으로 추가됩니다. 취소하면 이전 초안이 복원됩니다. 게임의 사용 설정은 유지됩니다.", "Сбросить клавиатуру и геймпад во ВСЕХ схемах.\n\nЭто черновик без записи. Отмена вернёт прежние черновики. Активная схема игры не изменится.");
            Editor("editor.openbackup", "打开备份目录", "Open backup folder", "バックアップフォルダーを開く", "백업 폴더 열기", "Открыть резервные копии");
            Editor("editor.default", "默认", "Default", "既定", "기본", "По умолчанию");
            Editor("editor.defaulthelp", "继承游戏默认键位", "Inherits the game's default binding", "ゲーム既定の割り当てを継承", "게임 기본 키 설정 유지", "Использует стандартную клавишу игры");
            Editor("editor.mixed", "多组不同值", "Multiple values", "複数の値", "여러 값", "Разные значения");
            Editor("editor.mixedhelp", "当前方案的不同手柄记录有差异；未编辑时保留各自值，修改后仅统一此方案的该绑定。", "Gamepad records within this scheme differ. Editing unifies this binding only in this scheme.", "現在の設定内のパッド記録で値が異なります。編集すると、この設定の該当割り当てだけ統一します。", "현재 설정 내 게임패드 기록의 값이 다릅니다. 편집하면 이 설정의 해당 바인딩만 통일됩니다.", "Записи геймпада этой схемы различаются. Правка объединит только это назначение в этой схеме.");
            Editor("editor.editbinding", "修改绑定 · {0}", "Edit binding · {0}", "割り当て編集 · {0}", "바인딩 편집 · {0}", "Назначение · {0}");
            Editor("editor.keyinput", "从列表选择，或点击下方录入区域后按键。也可输入游戏键名。", "Choose a key, or focus the capture field and press one. Game key names are also accepted.", "一覧から選択、または下の入力欄でキーを押してください。ゲームのキー名も入力できます。", "목록에서 선택하거나 아래 입력 영역에서 키를 누르세요. 게임 키 이름도 입력할 수 있습니다.", "Выберите клавишу или нажмите её в поле захвата. Можно ввести имя клавиши игры.");
            Editor("editor.padinput", "选择手柄按钮，仅修改当前方案。清除覆盖会恢复此方案各记录的默认值。", "Choose a gamepad button for this scheme. Clear override restores this scheme's record defaults.", "現在の設定のパッドボタンを選択。上書き解除は、この設定の各記録の既定値に戻します。", "현재 설정의 게임패드 버튼을 선택하세요. 덮어쓰기 해제 시 이 설정 내 각 기록의 기본값으로 복원됩니다.", "Выберите кнопку геймпада этой схемы. Удаление замены вернёт стандартные значения её записей.");
            Editor("editor.capture", "点击这里，然后按键（鼠标按键也可录入）", "Click here, then press a key (mouse buttons supported)", "ここをクリックしてキーを入力（マウス対応）", "여기를 클릭한 후 키 입력 (마우스 버튼 지원)", "Нажмите здесь и введите клавишу (мышь поддерживается)");
            Editor("editor.clear", "清除覆盖", "Clear override", "上書きを解除", "덮어쓰기 해제", "Убрать замену");
            Editor("editor.ok", "确定", "OK", "OK", "확인", "OK");
            Editor("editor.cancel", "取消", "Cancel", "キャンセル", "취소", "Отмена");
            Editor("key.mouseleft", "左键", "Left mouse", "左クリック", "왼쪽 클릭", "ЛКМ");
            Editor("key.mouseright", "右键", "Right mouse", "右クリック", "오른쪽 클릭", "ПКМ");
            Editor("key.mousemiddle", "中键", "Middle mouse", "中クリック", "가운데 클릭", "СКМ");
            Editor("key.mouse4", "鼠标侧键 1", "Mouse 4", "マウス 4", "마우스 4", "Мышь 4");
            Editor("key.mouse5", "鼠标侧键 2", "Mouse 5", "マウス 5", "마우스 5", "Мышь 5");
            Editor("key.space", "空格", "Space", "スペース", "스페이스", "Пробел");
            Editor("scheme.default", "默认预设", "Default", "既定", "기본", "По умолчанию");
            Editor("scheme.custom1", "自定义 1", "Custom 1", "カスタム 1", "사용자 1", "Своя 1");
            Editor("scheme.custom2", "自定义 2", "Custom 2", "カスタム 2", "사용자 2", "Своя 2");
            Editor("preset.onekey.name", "左键同时防御（保留右键）", "Left mouse also guards (keep right mouse)", "左クリックでも防御（右クリック維持）", "왼쪽 클릭도 방어 (오른쪽 클릭 유지)", "ЛКМ также блокирует (ПКМ остаётся)");
            Editor("preset.onekey.desc", "左键 / 手柄 X 同时攻击和防御；右键 / LB 仍可防御。", "Left mouse / X attack and guard together; right mouse / LB still guard.", "左クリック / X で攻撃と防御。右クリック / LB でも引き続き防御できます。", "왼쪽 클릭 / X로 공격과 방어를 동시에 합니다. 오른쪽 클릭 / LB도 방어를 유지합니다.", "ЛКМ / X атакуют и блокируют одновременно; ПКМ / LB сохраняют блок.");
            Editor("preset.onekey_dodge.name", "左键攻防＋右键闪避 / 高速移动", "Attack + guard, guard key to dodge / sprint", "攻防統合＋防御キーで回避 / 高速移動", "공방 통합＋방어 키로 회피 / 고속 이동", "Атака + блок; бывший блок — уклонение / бег");
            Editor("preset.onekey_dodge.desc", "防御改为左键 / X；右键 / LB 改为闪避和高速移动，替换相关动作已有绑定。", "Guard moves to left mouse / X; right mouse / LB dodge and sprint. Existing bindings for these actions are replaced.", "防御を左クリック / X に、右クリック / LB を回避と高速移動に変更。関連動作の割り当てを置き換えます。", "방어는 왼쪽 클릭 / X로, 오른쪽 클릭 / LB는 회피와 고속 이동으로 변경됩니다. 해당 동작의 기존 바인딩을 교체합니다.", "Блок: ЛКМ / X; ПКМ / LB: уклонение и бег. Прежние назначения этих действий заменяются.");
            Editor("preset.souls.name", "魂系手柄布局", "Souls gamepad layout", "ソウル風パッド配置", "소울류 게임패드 배치", "Геймпад в стиле Souls");
            Editor("preset.souls.desc", "仅手柄：轻攻击 RB、重攻击 RT、防御 LB、闪避 / 高速移动 A、交互 / 处决 Y。", "Gamepad only: light RB, heavy RT, guard LB, dodge / sprint A, interact / execute Y.", "パッドのみ：弱 RB、強 RT、防御 LB、回避 / 高速移動 A、会話 / 処刑 Y。", "게임패드만: 약공격 RB, 강공격 RT, 방어 LB, 회피 / 고속 이동 A, 상호작용 / 처형 Y.", "Только геймпад: лёгкая RB, тяжёлая RT, блок LB, уклонение / бег A, взаимодействие / добивание Y.");
            Editor("preset.space_sprint.name", "空格高速移动", "Space to sprint", "スペースで高速移動", "스페이스로 고속 이동", "Пробел — бег");
            Editor("preset.space_sprint.desc", "仅键鼠：高速移动改为 Shift / 空格，替换该动作已有绑定。", "Keyboard only: sprint becomes Shift / Space, replacing existing sprint bindings.", "キーボードのみ：高速移動を Shift / スペースに置き換えます。", "키보드만: 고속 이동을 Shift / 스페이스로 교체합니다.", "Только клавиатура: бег на Shift / пробел, прежние назначения бега заменяются.");
            Editor("preset.space_dodge.name", "空格闪避", "Space to dodge", "スペースで回避", "스페이스로 회피", "Пробел — уклонение");
            Editor("preset.space_dodge.desc", "仅键鼠：闪避改为 Shift / 空格，替换该动作已有绑定。", "Keyboard only: dodge becomes Shift / Space, replacing existing dodge bindings.", "キーボードのみ：回避を Shift / スペースに置き換えます。", "키보드만: 회피를 Shift / 스페이스로 교체합니다.", "Только клавиатура: уклонение на Shift / пробел, прежние назначения заменяются.");
        }
    }
}
