// TAC app localization — self-contained so the 2D game's Loc table stays
// untouched. Persists the choice alongside the web's pw_lang convention.
using System.Collections.Generic;
using UnityEngine;

public static class TacLoc
{
    public static readonly string[] Langs = { "en", "ja", "zh", "es", "ko" };
    static string _lang;

    public static string Lang
    {
        get
        {
            if (_lang == null)
            {
                _lang = PlayerPrefs.GetString("tac_lang", "");
                if (_lang == "")
                {
                    var sys = Application.systemLanguage;
                    _lang = sys == SystemLanguage.Japanese ? "ja"
                        : (sys == SystemLanguage.Chinese || sys == SystemLanguage.ChineseSimplified || sys == SystemLanguage.ChineseTraditional) ? "zh"
                        : sys == SystemLanguage.Spanish ? "es"
                        : sys == SystemLanguage.Korean ? "ko" : "en";
                }
            }
            return _lang;
        }
    }

    public static void SetLang(string l)
    {
        _lang = l;
        PlayerPrefs.SetString("tac_lang", l);
    }

    static readonly Dictionary<string, string[]> T_ = new Dictionary<string, string[]>
    {
        // key -> en, ja, zh, es, ko
        { "title",   new[] { "PROMPT WORLD", "PROMPT WORLD", "PROMPT WORLD", "PROMPT WORLD", "PROMPT WORLD" } },
        { "tagline", new[] { "Diverse tactical arenas anyone can build with prompts & AI", "誰もがプロンプトとAIで作れる多様な戦術アリーナ", "人人都能用提示词和AI打造的多样战术竞技场", "Arenas tácticas diversas que cualquiera crea con prompts e IA", "누구나 프롬프트와 AI로 만드는 다양한 전술 아레나" } },
        { "training", new[] { "TRAINING GROUND", "訓練場", "训练场", "CAMPO DE ENTRENAMIENTO", "훈련장" } },
        { "loading", new[] { "LOADING...", "読み込み中...", "加载中...", "CARGANDO...", "로딩 중..." } },
        { "offline", new[] { "OFFLINE — training only", "オフライン — 訓練場のみ", "离线 — 仅训练场", "SIN CONEXIÓN — solo entrenamiento", "오프라인 — 훈련장만" } },
        { "start",   new[] { "TAP TO DEPLOY", "タップで出撃", "点击出击", "TOCA PARA DESPLEGAR", "탭하여 출격" } },
        { "cleared", new[] { "AREA CLEARED", "エリア制圧完了", "区域已肃清", "ZONA DESPEJADA", "구역 소탕 완료" } },
        { "dead",    new[] { "MISSION FAILED", "ミッション失敗", "任务失败", "MISIÓN FALLIDA", "임무 실패" } },
        { "timeout", new[] { "TIME OVER", "時間切れ", "时间到", "TIEMPO AGOTADO", "시간 초과" } },
        { "retry",   new[] { "RETRY", "リトライ", "重试", "REINTENTAR", "재도전" } },
        { "back",    new[] { "BACK", "戻る", "返回", "VOLVER", "뒤로" } },
        { "time",    new[] { "TIME", "タイム", "时间", "TIEMPO", "시간" } },
        { "hostiles", new[] { "HOSTILES", "敵残数", "敌人", "HOSTILES", "적" } },
        { "fire",    new[] { "FIRE", "射撃", "射击", "FUEGO", "발사" } },
        { "jump",    new[] { "JUMP", "ジャンプ", "跳跃", "SALTO", "점프" } },
        { "sneak",   new[] { "SNEAK", "しゃがみ", "潜行", "SIGILO", "은신" } },
        { "bomb",    new[] { "BOMB", "爆弾", "炸弹", "BOMBA", "폭탄" } },
        { "drone",   new[] { "DRONE", "ドローン", "无人机", "DRON", "드론" } },
        { "scope",   new[] { "SCOPE", "スコープ", "瞄准镜", "MIRA", "스코프" } },
        { "paused",  new[] { "PAUSED", "一時停止", "已暂停", "PAUSA", "일시정지" } },
        { "resume",  new[] { "RESUME", "再開", "继续", "CONTINUAR", "계속" } },
        { "scoreSent", new[] { "Run submitted to the leaderboard!", "記録をリーダーボードに送信しました!", "成绩已提交排行榜!", "¡Marca enviada a la clasificación!", "기록을 리더보드에 제출했습니다!" } },
        { "preview", new[] { "PREVIEW · UNPUBLISHED", "プレビュー · 未公開", "预览 · 未发布", "AVANCE · SIN PUBLICAR", "미리보기 · 미공개" } },
        { "preview_clear", new[] { "Preview clear — not saved. Clear it on the web to publish.", "プレビュークリア — 記録なし。公開はWebでクリアしてね。", "预览通关 — 未记录。请在网页通关后发布。", "Nivel superado en avance — no se guarda. Supéralo en la web para publicar.", "미리보기 클리어 — 저장 안 됨. 웹에서 클리어해 공개하세요." } },
        { "dl_notfound", new[] { "Stage link not found.", "ステージが見つかりません。", "未找到该关卡。", "Enlace de nivel no encontrado.", "스테이지를 찾을 수 없습니다." } },
        { "dl_published", new[] { "Already published — find it in the list.", "公開済みです — 一覧から遊べます。", "已发布 — 请在列表中查找。", "Ya publicado: búscalo en la lista.", "이미 공개됨 — 목록에서 찾으세요." } },
        { "settings", new[] { "SETTINGS", "設定", "设置", "AJUSTES", "설정" } },
        { "language", new[] { "LANGUAGE", "言語", "语言", "IDIOMA", "언어" } },
        { "bgm",     new[] { "MUSIC", "BGM", "音乐", "MÚSICA", "음악" } },
        { "sfx",     new[] { "SOUND FX", "効果音", "音效", "EFECTOS", "효과음" } },
        { "skin",    new[] { "OPERATIVE", "着せ替え", "外观", "ASPECTO", "스킨" } },
        { "handSide", new[] { "CONTROL SIDE", "操作の利き手", "操作用手", "MANO DE CONTROL", "조작 손" } },
        { "handR",   new[] { "RIGHT HAND", "右手配置", "右手布局", "DIESTRO", "오른손 배치" } },
        { "handL",   new[] { "LEFT HAND", "左手配置", "左手布局", "ZURDO", "왼손 배치" } },
        { "sortNew", new[] { "NEW", "新着", "最新", "NUEVO", "최신" } },
        { "sortGood", new[] { "GOOD", "高評価", "好评", "MEJOR", "인기" } },
        { "sortPlays", new[] { "PLAYS", "プレイ数", "游玩数", "JUGADAS", "플레이" } },
        { "sortHard", new[] { "HARD", "高難度", "高难度", "DIFÍCIL", "고난도" } },
        { "sortTb", new[] { "UNVERIFIED", "検証待ち", "待验证", "SIN VERIFICAR", "검증 대기" } },
        { "search", new[] { "Search stages…", "ステージ名で検索…", "搜索关卡…", "Buscar niveles…", "스테이지 검색…" } },
        { "obTitle", new[] { "PROMPT WORLD", "PROMPT WORLD", "PROMPT WORLD", "PROMPT WORLD", "PROMPT WORLD" } },
        { "obL1", new[] { "A 3D stealth shooter. Sneak past patrols, watch their sightlines, pick your moment.", "3Dステルスシューター。巡回をすり抜け、敵の視線を読み、好機を突け。", "3D潜行射击。避开巡逻,观察视线,把握时机。", "Shooter sigiloso 3D. Evita patrullas, vigila su visión, elige tu momento.", "3D 스텔스 슈터. 순찰을 피하고 시야를 읽어 기회를 노려라." } },
        { "obL2", new[] { "Line up the shot — aim locks on where you face. Get spotted and the whole squad converges.", "狙いは向いた方向に自動ロック。見つかれば部隊が一斉に集まる。", "瞄准会自动锁定你面朝的方向。被发现则全队围攻。", "El disparo se fija hacia donde miras. Si te ven, el escuadrón converge.", "조준은 바라보는 방향으로 자동 고정. 발각되면 부대가 몰려온다." } },
        { "obL3", new[] { "Clear a stage to publish it for everyone — or build your own by talking to your AI.", "クリアすれば全員に公開。AIに話しかけて自分でも作れる。", "通关即向所有人发布。也可对AI说话来自己创作。", "Supera un nivel para publicarlo — o crea el tuyo hablando con tu IA.", "클리어하면 모두에게 공개. AI에게 말해 직접 만들 수도 있다." } },
        { "obStart", new[] { "START", "はじめる", "开始", "EMPEZAR", "시작" } },
        { "firstClear", new[] { "🏆 WORLD FIRST CLEAR", "🏆 世界初クリア", "🏆 世界首次通关", "🏆 PRIMER PASE MUNDIAL", "🏆 세계 최초 클리어" } },
        { "firstClearSub", new[] { "You published this stage for everyone!", "このステージを全員に公開しました!", "你已将此关卡向所有人发布!", "¡Publicaste este nivel para todos!", "이 스테이지를 모두에게 공개했습니다!" } },
        { "hide", new[] { "Report / Hide", "通報・非表示", "举报・隐藏", "Reportar / Ocultar", "신고・숨기기" } },
        { "hideConfirm", new[] {
            "Report and hide this stage from your lists? It stays visible to others; our team reviews reports.",
            "このステージを通報して、あなたの一覧から非表示にしますか?他の人には表示されたままで、運営が通報を確認します。",
            "举报并从你的列表中隐藏此关卡?其他人仍可看到,我们会审核举报。",
            "¿Reportar y ocultar este nivel de tus listas? Sigue visible para otros; nuestro equipo revisa los reportes.",
            "이 스테이지를 신고하고 목록에서 숨기시겠습니까? 다른 사람에게는 계속 표시되며, 운영팀이 신고를 검토합니다." } },
        { "hidden", new[] { "Hidden", "非表示にしました", "已隐藏", "Oculto", "숨김" } },
        { "privacy", new[] { "PRIVACY", "プライバシー", "隐私政策", "PRIVACIDAD", "개인정보" } },
        { "terms", new[] { "TERMS", "利用規約", "服务条款", "TÉRMINOS", "이용약관" } },
        { "tbNone", new[] { "CLEARED BY: NONE", "クリア者: なし", "通关者: 无", "SUPERADO: NADIE", "클리어: 없음" } },
        { "create",  new[] { "CREATE", "コースを作る", "创作关卡", "CREAR", "코스 만들기" } },
        { "createLead", new[] { "Build your own arena by just TALKING to your AI assistant. No editor, no code — describe the stage, your AI builds it, you clear it, it goes live for everyone.", "エディタ不要・コード不要。AIアシスタントに話しかけるだけで自分のアリーナが作れます。あなたが説明し、AIが組み立て、あなたがクリアしたら全世界に公開されます。", "无需编辑器、无需代码——只要和你的AI助手对话就能创建竞技场。你来描述,AI来搭建,你通关后即向全世界发布。", "Crea tu propia arena simplemente HABLANDO con tu asistente de IA. Sin editor, sin código: tú la describes, tu IA la construye, tú la superas y se publica para todos.", "에디터도 코드도 필요 없습니다. AI 어시스턴트에게 말만 하면 나만의 아레나를 만들 수 있어요. 설명하면 AI가 만들고, 클리어하면 전 세계에 공개됩니다." } },
        { "createS1", new[] { "Connect Prompt World to your AI (Claude, etc.) with this command:", "AI(Claudeなど)にPrompt Worldを接続します。コマンドはこちら:", "用以下命令把 Prompt World 连接到你的AI(如Claude):", "Conecta Prompt World a tu IA (Claude, etc.) con este comando:", "이 명령어로 AI(Claude 등)에 Prompt World를 연결하세요:" } },
        { "createS2", new[] { "Ask: \"Make me a stealth arena on Prompt World\" — your AI designs enemies, trenches, drones, everything.", "「Prompt Worldでステルスアリーナを作って」と頼むだけ。敵・塹壕・ドローンまでAIが設計します。", "只要说\"帮我在 Prompt World 做一个潜行竞技场\"——敌人、战壕、无人机都由AI设计。", "Pide: \"Hazme una arena de sigilo en Prompt World\" — tu IA diseña enemigos, trincheras, drones, todo.", "\"Prompt World에서 스텔스 아레나 만들어줘\"라고 부탁하면 적·참호·드론까지 AI가 설계합니다." } },
        { "createS3", new[] { "Clear your own stage in the browser — that verified run is the proof, then it's published with a shareable URL.", "自分のステージをブラウザでクリアすれば、その記録が証明になり、共有URL付きで公開されます。", "在浏览器里通关你自己的关卡——通关记录就是证明,之后即以可分享的URL发布。", "Supera tu propio nivel en el navegador: esa partida verificada es la prueba, y se publica con URL para compartir.", "브라우저에서 자기 스테이지를 클리어하면 그 기록이 증명이 되어 공유 URL과 함께 공개됩니다." } },
        { "copyCmd", new[] { "COPY COMMAND", "コマンドをコピー", "复制命令", "COPIAR COMANDO", "명령어 복사" } },
        { "copied",  new[] { "Copied!", "コピーしました!", "已复制!", "¡Copiado!", "복사했습니다!" } },
        { "openGuide", new[] { "FULL GUIDE (WEB)", "詳しいガイド(Web)", "完整指南(网页)", "GUÍA COMPLETA (WEB)", "전체 가이드(웹)" } },
        { "rateStage", new[] { "Rate this stage", "このステージを評価", "为这个关卡评分", "Puntúa este nivel", "이 스테이지 평가" } },
        { "voteThanks", new[] { "Thanks for rating!", "評価ありがとう!", "感谢评分!", "¡Gracias por puntuar!", "평가 감사합니다!" } },
        { "voteGood", new[] { "LIKE", "良かった", "赞", "ME GUSTA", "좋아요" } },
        { "voteBad", new[] { "MEH", "イマイチ", "一般", "MEH", "별로" } },
        { "extTitle", new[] { "OPEN IN BROWSER?", "ブラウザで開きますか?", "在浏览器中打开?", "¿ABRIR EN EL NAVEGADOR?", "브라우저에서 열까요?" } },
        { "extBody", new[] { "This will leave the app and open the page in your browser.", "アプリを離れて、外部ブラウザでページを開きます。", "将离开应用,在外部浏览器中打开页面。", "Saldrás de la app y la página se abrirá en tu navegador.", "앱을 벗어나 외부 브라우저에서 페이지를 엽니다." } },
        { "extOpen", new[] { "OPEN", "開く", "打开", "ABRIR", "열기" } },
        { "extCancel", new[] { "CANCEL", "キャンセル", "取消", "CANCELAR", "취소" } },
    };

    public static string T(string key)
    {
        if (!T_.ContainsKey(key)) return key;
        int i = System.Array.IndexOf(Langs, Lang);
        if (i < 0) i = 0;
        return T_[key][i];
    }

    // stage-provided localized maps: {en,ja,zh,es,ko}
    public static string Pick(TacJson.JObj map, string fallback)
    {
        if (map == null) return fallback;
        if (map.Has(Lang)) return map.Str(Lang);
        if (map.Has("en")) return map.Str("en");
        return fallback;
    }
}
