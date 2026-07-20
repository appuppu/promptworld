using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// In-game UI localization. Mirrors the 5 languages of the /create web page
/// (en/ja/zh/es/ko). Language is auto-detected from the browser (WebGL) or the
/// OS (native), overridable and persisted via PlayerPrefs + localStorage.
/// Lookup is Loc.T("key"); unknown keys fall back to English then to the key.
/// </summary>
public static class Loc
{
    public const string PrefKey = "pw_lang";
    public static readonly string[] Langs = { "en", "ja", "zh", "es", "ko" };
    public static readonly string[] LangNames = { "English", "日本語", "中文", "Español", "한국어" };

    private static bool warmed;

    /// <summary>
    /// Pre-rasterize every glyph used by the CURRENT language into the dynamic
    /// font fallback, so non-Latin text doesn't render blank the first frame it
    /// appears (a WebGL dynamic-atlas quirk). Runs once per language on demand.
    /// </summary>
    public static void WarmupFont()
    {
        // Never let a font-warmup hiccup abort UI setup — wrap everything.
        try
        {
            var def = TMP_Settings.defaultFontAsset;
            if (def == null) return;

            var sb = new StringBuilder();
            if (Table.TryGetValue(Current, out var dict))
                foreach (var kv in dict.Values) sb.Append(kv);
            foreach (var n in LangNames) sb.Append(n);
            string chars = sb.ToString();
            if (chars.Length == 0) return;

            def.TryAddCharacters(chars); // adds missing glyphs to the dynamic atlas
            warmed = true;
        }
        catch { /* dynamic atlas not ready / unsupported — safe to skip */ }
    }

    /// <summary>
    /// Ensure the glyphs for an arbitrary string (e.g. a server-supplied course
    /// name that may be Japanese) are in the dynamic atlas. Static UI labels are
    /// covered by WarmupFont, but course names arrive at runtime — without this
    /// they can render blank on iOS, where on-demand rasterization is flaky.
    /// </summary>
    public static void WarmupText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            var def = TMP_Settings.defaultFontAsset;
            if (def != null) def.TryAddCharacters(text);
        }
        catch { /* safe to skip */ }
    }

    private static string current;

    public static string Current
    {
        get
        {
            if (current == null) current = Detect();
            return current;
        }
    }

    public static void SetLang(string lang)
    {
        if (!Table.ContainsKey(lang)) lang = "en";
        current = lang;
        PlayerPrefs.SetString(PrefKey, lang);
        PlayerPrefs.Save();
        WebBridge.SaveLang(lang);
    }

    private static string Detect()
    {
        // The site defaults to ENGLISH. We honor only an explicit saved choice
        // (the player picked a language in Settings); otherwise English — we no
        // longer auto-switch by browser/OS locale, so first-time visitors always
        // see the English UI. They can still change it in Settings.
        string saved = PlayerPrefs.GetString(PrefKey, null);
        if (!string.IsNullOrEmpty(saved) && Table.ContainsKey(saved)) return saved;
        return "en";
    }

    public static string T(string key)
    {
        var dict = Table.TryGetValue(Current, out var d) ? d : Table["en"];
        if (dict.TryGetValue(key, out var v)) return v;
        if (Table["en"].TryGetValue(key, out var e)) return e;
        return key;
    }

    // {0}-style formatting helper.
    public static string T(string key, params object[] args)
    {
        return string.Format(T(key), args);
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Table =
        new Dictionary<string, Dictionary<string, string>>
    {
        ["en"] = new Dictionary<string, string>
        {
            ["retry"] = "RETRY",
            ["menu"] = "MENU",
            ["next"] = "NEXT >",
            ["share"] = "SHARE URL",
            ["copied"] = "COPIED!",
            ["createOwn"] = "CREATE YOUR OWN WORLD  →",
            ["good"] = "GOOD",
            ["bad"] = "BAD",
            ["stageClear"] = "Stage Clear!",
            ["gameOver"] = "Game Over",
            ["outOfLives"] = "out of lives",
            ["timeUp"] = "time up",
            ["clearVerified"] = "CLEAR VERIFIED — READY TO PUBLISH",
            ["clearFailed"] = "CLEAR VERIFICATION FAILED",
            ["lives"] = "LIVES",
            ["keysDoorOpen"] = "KEYS {0}/{1}  —  DOOR OPEN!",
            ["keysCollect"] = "KEYS {0}/{1}  —  collect all to open the door",
            ["searchPlaceholder"] = "search stages…",
            ["playerName"] = "PLAYER NAME",
            ["settings"] = "SETTINGS",
            ["language"] = "LANGUAGE",
            ["close"] = "CLOSE",
            ["privacy"] = "PRIVACY",
            ["terms"] = "TERMS",
            ["comingSoon"] = "A great course could go here",
            ["sortBest100"] = "BEST 100",
            ["sortNew"] = "NEW",
            ["sortTop"] = "GOOD %",
            ["sortHard"] = "HARD",
            ["sortEasy"] = "EASY",
            ["sfx"] = "SFX",
            ["music"] = "MUSIC",
            ["on"] = "ON",
            ["off"] = "OFF",
            ["best"] = "BEST",
            ["plays"] = "PLAYS",
            ["clearRate"] = "CLEAR",
            ["by"] = "by",
            ["par"] = "PAR",
            ["bestTimes"] = "BEST TIMES",
            ["shareCaption"] = "I cleared \"{0}\" in Prompt World! Can you beat my time? Made by prompting an AI. #PromptWorld",
        },
        ["ja"] = new Dictionary<string, string>
        {
            ["retry"] = "リトライ",
            ["menu"] = "メニュー",
            ["next"] = "次へ >",
            ["share"] = "URLを共有",
            ["copied"] = "コピーしました!",
            ["createOwn"] = "自分の世界を作る  →",
            ["good"] = "いいね",
            ["bad"] = "うーん",
            ["stageClear"] = "クリア!",
            ["gameOver"] = "ゲームオーバー",
            ["outOfLives"] = "ライフ切れ",
            ["timeUp"] = "時間切れ",
            ["clearVerified"] = "クリア認証済み — 公開できます",
            ["clearFailed"] = "クリア認証に失敗",
            ["lives"] = "ライフ",
            ["keysDoorOpen"] = "カギ {0}/{1}  —  扉が開いた!",
            ["keysCollect"] = "カギ {0}/{1}  —  全部集めて扉を開けよう",
            ["searchPlaceholder"] = "コースを検索…",
            ["playerName"] = "プレイヤー名",
            ["settings"] = "設定",
            ["language"] = "言語",
            ["close"] = "閉じる",
            ["privacy"] = "プライバシー",
            ["terms"] = "利用規約",
            ["comingSoon"] = "いいコースができたらここに",
            ["sortBest100"] = "ベスト100",
            ["sortNew"] = "新着",
            ["sortTop"] = "いいね順",
            ["sortHard"] = "難しい順",
            ["sortEasy"] = "やさしい順",
            ["sfx"] = "効果音",
            ["music"] = "音楽",
            ["on"] = "オン",
            ["off"] = "オフ",
            ["best"] = "最速",
            ["plays"] = "プレイ",
            ["clearRate"] = "クリア率",
            ["by"] = "作者",
            ["par"] = "目標",
            ["bestTimes"] = "ベストタイム",
            ["shareCaption"] = "Prompt Worldで「{0}」をクリア！あなたはこのタイムを超えられる？AIに頼んで作られたステージ。 #PromptWorld",
        },
        ["zh"] = new Dictionary<string, string>
        {
            ["retry"] = "重试",
            ["menu"] = "菜单",
            ["next"] = "下一个 >",
            ["share"] = "分享网址",
            ["copied"] = "已复制!",
            ["createOwn"] = "创建你的世界  →",
            ["good"] = "赞",
            ["bad"] = "差",
            ["stageClear"] = "通关!",
            ["gameOver"] = "游戏结束",
            ["outOfLives"] = "生命耗尽",
            ["timeUp"] = "时间到",
            ["clearVerified"] = "通关已验证 — 可以发布",
            ["clearFailed"] = "通关验证失败",
            ["lives"] = "生命",
            ["keysDoorOpen"] = "钥匙 {0}/{1}  —  门已打开!",
            ["keysCollect"] = "钥匙 {0}/{1}  —  集齐全部即可开门",
            ["searchPlaceholder"] = "搜索关卡…",
            ["playerName"] = "玩家名",
            ["settings"] = "设置",
            ["language"] = "语言",
            ["close"] = "关闭",
            ["privacy"] = "隐私",
            ["terms"] = "条款",
            ["comingSoon"] = "期待优秀关卡入选",
            ["sortBest100"] = "百佳",
            ["sortNew"] = "最新",
            ["sortTop"] = "好评率",
            ["sortHard"] = "困难",
            ["sortEasy"] = "简单",
            ["sfx"] = "音效",
            ["music"] = "音乐",
            ["on"] = "开",
            ["off"] = "关",
            ["best"] = "最快",
            ["plays"] = "游玩",
            ["clearRate"] = "通关率",
            ["by"] = "作者",
            ["par"] = "目标",
            ["bestTimes"] = "最佳时间",
            ["shareCaption"] = "我在 Prompt World 通关了「{0}」！你能打破我的纪录吗？由AI生成的关卡。 #PromptWorld",
        },
        ["es"] = new Dictionary<string, string>
        {
            ["retry"] = "REINTENTAR",
            ["menu"] = "MENÚ",
            ["next"] = "SIGUIENTE >",
            ["share"] = "COMPARTIR URL",
            ["copied"] = "¡COPIADO!",
            ["createOwn"] = "CREA TU PROPIO MUNDO  →",
            ["good"] = "BIEN",
            ["bad"] = "MAL",
            ["stageClear"] = "¡Nivel superado!",
            ["gameOver"] = "Fin del juego",
            ["outOfLives"] = "sin vidas",
            ["timeUp"] = "tiempo agotado",
            ["clearVerified"] = "VERIFICADO — LISTO PARA PUBLICAR",
            ["clearFailed"] = "VERIFICACIÓN FALLIDA",
            ["lives"] = "VIDAS",
            ["keysDoorOpen"] = "LLAVES {0}/{1}  —  ¡PUERTA ABIERTA!",
            ["keysCollect"] = "LLAVES {0}/{1}  —  reúne todas para abrir la puerta",
            ["searchPlaceholder"] = "buscar niveles…",
            ["playerName"] = "NOMBRE",
            ["settings"] = "AJUSTES",
            ["language"] = "IDIOMA",
            ["close"] = "CERRAR",
            ["privacy"] = "PRIVACIDAD",
            ["terms"] = "TÉRMINOS",
            ["comingSoon"] = "Aquí podría ir un gran nivel",
            ["sortBest100"] = "TOP 100",
            ["sortNew"] = "NUEVO",
            ["sortTop"] = "% BIEN",
            ["sortHard"] = "DIFÍCIL",
            ["sortEasy"] = "FÁCIL",
            ["sfx"] = "SONIDO",
            ["music"] = "MÚSICA",
            ["on"] = "SÍ",
            ["off"] = "NO",
            ["best"] = "MEJOR",
            ["plays"] = "JUGADAS",
            ["clearRate"] = "LOGRO",
            ["by"] = "por",
            ["par"] = "META",
            ["bestTimes"] = "MEJORES TIEMPOS",
            ["shareCaption"] = "¡Superé \"{0}\" en Prompt World! ¿Puedes batir mi tiempo? Nivel creado con IA. #PromptWorld",
        },
        ["ko"] = new Dictionary<string, string>
        {
            ["retry"] = "다시하기",
            ["menu"] = "메뉴",
            ["next"] = "다음 >",
            ["share"] = "URL 공유",
            ["copied"] = "복사됨!",
            ["createOwn"] = "나만의 월드 만들기  →",
            ["good"] = "좋아요",
            ["bad"] = "별로",
            ["stageClear"] = "클리어!",
            ["gameOver"] = "게임 오버",
            ["outOfLives"] = "생명 소진",
            ["timeUp"] = "시간 초과",
            ["clearVerified"] = "클리어 인증됨 — 게시 가능",
            ["clearFailed"] = "클리어 인증 실패",
            ["lives"] = "생명",
            ["keysDoorOpen"] = "열쇠 {0}/{1}  —  문이 열렸다!",
            ["keysCollect"] = "열쇠 {0}/{1}  —  모두 모아 문을 열자",
            ["searchPlaceholder"] = "스테이지 검색…",
            ["playerName"] = "플레이어 이름",
            ["settings"] = "설정",
            ["language"] = "언어",
            ["close"] = "닫기",
            ["privacy"] = "개인정보",
            ["terms"] = "이용약관",
            ["comingSoon"] = "멋진 코스가 여기에",
            ["sortBest100"] = "베스트100",
            ["sortNew"] = "최신",
            ["sortTop"] = "좋아요순",
            ["sortHard"] = "어려움",
            ["sortEasy"] = "쉬움",
            ["sfx"] = "효과음",
            ["music"] = "음악",
            ["on"] = "켬",
            ["off"] = "끔",
            ["best"] = "최고",
            ["plays"] = "플레이",
            ["clearRate"] = "클리어율",
            ["by"] = "제작",
            ["par"] = "목표",
            ["bestTimes"] = "베스트 타임",
            ["shareCaption"] = "Prompt World에서 \"{0}\" 클리어! 내 기록을 깰 수 있어? AI로 만든 스테이지. #PromptWorld",
        },
    };
}
