namespace PBZKeyForge
{
    public static partial class I18n
    {
        static void LiveConnectionStrings()
        {
            string[] keys = { "live.connected", "live.waiting", "live.paused", "live.waitingdetail" };
            LiveConnectionLanguage("zh-Hans", keys, "实时编辑已连接|实时编辑 · 等待游戏响应|实时已连接 · 增强调度暂停|正在等待游戏响应，加载或切回游戏后会自动恢复连接。草稿已保留，连接恢复后可应用。");
            LiveConnectionLanguage("zh-Hant", keys, "即時編輯已連線|即時編輯 · 等待遊戲回應|即時已連線 · 增強排程暫停|正在等待遊戲回應，載入或切回遊戲後會自動恢復連線。草稿已保留，連線恢復後可套用。");
            LiveConnectionLanguage("en", keys, "Live editing connected|Live editing · Waiting for game|Live connected · Routing paused|Waiting for the game to respond. Connection resumes automatically after loading or returning to the game. Draft kept; apply when connected.");
            LiveConnectionLanguage("ja", keys, "リアルタイム編集に接続済み|リアルタイム編集 · ゲームの応答待ち|接続済み · 拡張入力停止中|ゲームの応答を待っています。ロード完了またはゲームに戻ると自動で再接続します。編集中の内容は保持され、接続後に適用できます。");
            LiveConnectionLanguage("ko", keys, "실시간 편집 연결됨|실시간 편집 · 게임 응답 대기|연결됨 · 향상 입력 중지|게임 응답을 기다리는 중입니다. 로딩 완료 또는 게임 복귀 후 자동으로 다시 연결됩니다. 초안은 유지되며 연결 후 적용할 수 있습니다.");
            LiveConnectionLanguage("ru", keys, "Редактирование подключено|Ожидание ответа игры|Подключено · Ввод приостановлен|Ожидание ответа игры. Связь восстановится после загрузки или возврата в игру. Черновик сохранён; примените его после подключения.");
            LiveConnectionLanguage("fr", keys, "Édition en direct connectée|En direct · En attente du jeu|Connecté · Gestion suspendue|En attente du jeu. Reconnexion automatique après le chargement ou le retour au jeu. Brouillon conservé ; appliquez une fois connecté.");
            LiveConnectionLanguage("de", keys, "Live-Bearbeitung verbunden|Live · Warte auf das Spiel|Verbunden · Eingabe pausiert|Warte auf die Antwort des Spiels. Nach dem Laden oder der Rückkehr zum Spiel wird die Verbindung automatisch wiederhergestellt. Entwurf bleibt erhalten; danach anwenden.");
            LiveConnectionLanguage("es-ES", keys, "Edición en directo conectada|En directo · Esperando al juego|Conectado · Entrada suspendida|Esperando al juego. La conexión se restablece tras cargar o volver al juego. Borrador conservado; aplícalo al conectar.");
            LiveConnectionLanguage("es-419", keys, "Edición en directo conectada|En directo · Esperando al juego|Conectado · Entrada suspendida|Esperando al juego. La conexión se restablece tras cargar o volver al juego. Borrador conservado; aplícalo al conectar.");
            LiveConnectionLanguage("pt-BR", keys, "Edição ao vivo conectada|Ao vivo · Aguardando o jogo|Conectado · Entrada suspensa|Aguardando resposta do jogo. A conexão retorna automaticamente após carregar ou voltar ao jogo. Rascunho mantido; aplique quando conectado.");
            LiveConnectionLanguage("pt-PT", keys, "Edição ao vivo ligada|Ao vivo · A aguardar o jogo|Ligado · Entrada suspensa|A aguardar resposta do jogo. A ligação regressa automaticamente após carregar ou voltar ao jogo. Rascunho mantido; aplique quando ligado.");
            LiveConnectionLanguage("it", keys, "Modifica in tempo reale connessa|In tempo reale · Attesa del gioco|Connesso · Input sospeso|In attesa del gioco. La connessione riprende dopo il caricamento o il ritorno al gioco. Bozza conservata; applicala alla riconnessione.");
            LiveConnectionLanguage("vi", keys, "Đã kết nối chỉnh sửa trực tiếp|Trực tiếp · Đợi game phản hồi|Đã kết nối · Đầu vào tạm dừng|Đang đợi game phản hồi. Kết nối tự khôi phục sau khi tải hoặc quay lại game. Bản nháp được giữ; áp dụng khi đã kết nối.");
            LiveConnectionLanguage("th", keys, "เชื่อมต่อการแก้ไขสดแล้ว|แก้ไขสด · รอเกมตอบสนอง|เชื่อมต่อแล้ว · อินพุตหยุดชั่วคราว|กำลังรอเกมตอบสนอง จะเชื่อมต่อใหม่อัตโนมัติหลังโหลดหรือกลับเข้าเกม เก็บฉบับร่างแล้ว ใช้ได้เมื่อเชื่อมต่ออีกครั้ง");
        }

        static void LiveConnectionLanguage(string language, string[] keys, string values)
        {
            string[] words = values.Split('|');
            for (int i = 0; i < keys.Length; i++) Add(language, keys[i], words[i]);
        }
    }
}
