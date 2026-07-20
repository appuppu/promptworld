using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Title screen. Handles:
/// 1. Deep links (?stage=<id>[&key=<editKey>]) jumping straight into a stage.
/// 2. Built-in + community stage lists with rendered thumbnails.
/// 3. Name search filtering.
/// 4. The creator funnel ("CREATE YOUR OWN WORLD" -> /create).
/// </summary>
public class MenuController : MonoBehaviour
{
    [SerializeField] private RectTransform listRoot;
    [SerializeField] private TMP_InputField searchInput;
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private Button createButton;
    [SerializeField] private RectTransform sortHolder;
    [SerializeField] private Button settingsButton;
    [SerializeField] private RectTransform settingsPanel;

    private static readonly string[] SortModes = { "best100", "new", "top", "hard", "easy" };
    private string currentSort = "new";
    private readonly List<TMP_Text> sortLabels = new List<TMP_Text>();

    private class Entry
    {
        public GameObject Root;
        public string Title;
        public bool IsLabel;
        public RawImage Preview;
        public string RemoteId; // set for community entries awaiting a thumbnail
    }

    private readonly List<Entry> entries = new List<Entry>();
    private static Texture2D placeholderTex;

    // Native (iOS/Android): a stage link tapped while the app is already running
    // arrives via Application.deepLinkActivated. Parse it and jump to the stage.
    // (At cold start, Application.absoluteURL already holds the launch URL, which
    // Start() reads through GameSession.ParseDeepLink.)
    private void OnEnable() { Application.deepLinkActivated += OnDeepLink; }
    private void OnDisable() { Application.deepLinkActivated -= OnDeepLink; }

    private void OnDeepLink(string url)
    {
        GameSession.ParseDeepLink(url);
        if (!string.IsNullOrEmpty(GameSession.RemoteStageId))
        {
            GameSession.DeepLinkConsumed = true;
            SceneManager.LoadScene("Stage");
        }
    }

    private IEnumerator Start()
    {
        // A shared URL should feel like launching the stage itself.
        if (!GameSession.DeepLinkConsumed)
        {
            GameSession.DeepLinkConsumed = true;
            GameSession.ParseDeepLink();
            if (!string.IsNullOrEmpty(GameSession.RemoteStageId))
            {
                SceneManager.LoadScene("Stage");
                yield break;
            }
        }

        GameSession.RemoteStageId = null;
        GameSession.EditKey = null;
        GameSession.SelectedStageFile = null;
        WebBridge.SetUrlStage(""); // back on the menu — clear ?stage= from the address bar

        Loc.WarmupFont(); // pre-rasterize this language's glyphs so non-Latin text isn't blank

        if (searchInput != null)
        {
            searchInput.onValueChanged.AddListener(Filter);
            SetPlaceholder(searchInput, Loc.T("searchPlaceholder"));
        }
        if (nameInput != null)
        {
            nameInput.text = PlayerIdentity.Name;
            nameInput.onValueChanged.AddListener(value => PlayerIdentity.Name = value);
            SetPlaceholder(nameInput, Loc.T("playerName"));
        }
        if (createButton != null)
        {
            createButton.onClick.AddListener(() => WebBridge.OpenUrl($"{GameSession.ApiOrigin}/create"));
            var cl = createButton.GetComponentInChildren<TMP_Text>();
            if (cl != null) cl.text = Loc.T("createOwn");
        }
        BuildSortColumn();
        BuildSettings();

        Sfx.StopMusic();
        yield return LoadCommunityStages();
        yield return FillCommunityPreviews();
    }

    private readonly List<Image> sortBgs = new List<Image>();

    /// <summary>
    /// NEW / TOP / HARD / EASY. Horizontal row on portrait/mobile (matches the
    /// approved mockup); vertical stack on wide desktop where the left column has
    /// the room. The active mode is marked with a bright bg + centered/left text.
    /// </summary>
    private void BuildSortColumn()
    {
        if (sortHolder == null) return;
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.6f;
        bool horizontal = aspect < 1.15f; // portrait/mobile => row

        if (horizontal)
        {
            var h = sortHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f; h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
        }
        else
        {
            var v = sortHolder.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6f; v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        }

        foreach (string modeName in SortModes)
        {
            string mode = modeName;
            bool active = mode == currentSort;
            var go = new GameObject($"Sort_{mode}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(sortHolder, false);
            var img = go.GetComponent<Image>();
            img.color = active ? UI.Surface2 : UI.Surface1;
            if (horizontal) UI.Sized(go, minH: 44f, flexW: 1f);
            else UI.Sized(go, minH: 48f);
            sortBgs.Add(img);

            // active marker: bottom bar (row) or left bar (column)
            var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(go.transform, false);
            var ar = accent.GetComponent<RectTransform>();
            if (horizontal)
            {
                ar.anchorMin = new Vector2(0f, 0f); ar.anchorMax = new Vector2(1f, 0f);
                ar.pivot = new Vector2(0.5f, 0f); ar.sizeDelta = new Vector2(0f, 3f);
            }
            else
            {
                ar.anchorMin = new Vector2(0f, 0f); ar.anchorMax = new Vector2(0f, 1f);
                ar.pivot = new Vector2(0f, 0.5f); ar.sizeDelta = new Vector2(3f, 0f);
            }
            ar.anchoredPosition = Vector2.zero;
            var aImg = accent.GetComponent<Image>();
            aImg.color = active ? UI.Fg : UI.Clear;
            aImg.raycastTarget = false;

            var tmp = UI.Text(go.transform, "Text", SortLabel(mode), horizontal ? 20f : 24f,
                active ? UI.Fg : UI.Dim,
                horizontal ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft, 2f, active);
            var trect = (RectTransform)tmp.transform;
            trect.anchorMin = Vector2.zero; trect.anchorMax = Vector2.one;
            trect.offsetMin = new Vector2(horizontal ? 2f : 16f, 0f); trect.offsetMax = Vector2.zero;
            // Auto-size: on mobile the 5 tabs split the width evenly, so long
            // localized labels ("BEST 100" / "MEJORES 100") must shrink, not clip.
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 11f;
            tmp.fontSizeMax = horizontal ? 20f : 24f;
            sortLabels.Add(tmp);
            sortAccents.Add(aImg);

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SetSort(mode));
        }
    }

    private readonly List<Image> sortAccents = new List<Image>();

    private static void SetPlaceholder(TMP_InputField field, string text)
    {
        if (field != null && field.placeholder is TMP_Text ph) ph.text = text;
    }

    private static string SortLabel(string mode)
    {
        switch (mode)
        {
            case "best100": return Loc.T("sortBest100");
            case "new": return Loc.T("sortNew");
            case "top": return Loc.T("sortTop");
            case "hard": return Loc.T("sortHard");
            case "easy": return Loc.T("sortEasy");
            default: return mode.ToUpperInvariant();
        }
    }

    /// <summary>
    /// Wires the gear button to open the settings card and fills the card
    /// (title / LANGUAGE row / SFX·MUSIC / CLOSE) with auto-layout. Everything
    /// that used to be scattered lives here now. Tapping the dim backdrop closes.
    /// </summary>
    /// <summary>Opens the settings panel. Public so the web page's top-right
    /// SETTINGS link can call it via SendMessage("MenuController","OpenSettings").</summary>
    public void OpenSettings()
    {
        if (settingsPanel != null) settingsPanel.gameObject.SetActive(true);
    }

    private void BuildSettings()
    {
        if (settingsButton != null)
        {
            // Localized "SETTINGS" word (not the clipped "SET"). Auto-size down
            // if the localized word is longer than the button so it never clips.
            var sl = settingsButton.GetComponentInChildren<TMP_Text>();
            if (sl != null)
            {
                sl.text = Loc.T("settings");
                sl.enableAutoSizing = true;
                sl.fontSizeMin = 10f;
                sl.fontSizeMax = 15f;
                sl.enableWordWrapping = false;
                sl.overflowMode = TextOverflowModes.Ellipsis;
            }
            settingsButton.onClick.AddListener(OpenSettings);
        }
        if (settingsPanel == null) return;

        // Backdrop click closes; the card swallows its own clicks.
        var backBtn = settingsPanel.gameObject.GetComponent<Button>() ?? settingsPanel.gameObject.AddComponent<Button>();
        backBtn.transition = Selectable.Transition.None;
        backBtn.onClick.AddListener(() => settingsPanel.gameObject.SetActive(false));

        var card = settingsPanel.Find("Card") as RectTransform;
        if (card == null) return;
        // Card catches clicks so they don't fall through to the backdrop.
        card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

        var body = UI.Stretch(card, "Body", new Vector4(UI.S4, UI.S3, UI.S4, UI.S3));
        UI.VStack(body, UI.S2, TextAnchor.UpperCenter);

        var title = UI.Text(body, "Title", Loc.T("settings"), 30f, UI.Fg, TextAlignmentOptions.Center, 6f, true);
        UI.Sized(title.gameObject, minH: 40f);

        var langLabel = UI.Label(body, Loc.T("language"), TextAlignmentOptions.Center);
        UI.Sized(langLabel.gameObject, minH: 22f);

        var langRow = UI.Box(body, "LangRow", UI.Clear);
        UI.HStack(langRow, 6f, TextAnchor.MiddleCenter);
        UI.Sized(langRow.gameObject, minH: 52f);
        foreach (string langName in Loc.Langs)
        {
            string chosen = langName;
            bool on = chosen == Loc.Current;
            var b = UI.Button(langRow, $"Lang_{chosen}", NameForLang(chosen), 20f, ghost: false);
            UI.Sized(b.gameObject, minH: 48f, flexW: 1f);
            var lbl = b.GetComponentInChildren<TMP_Text>();
            if (lbl != null) { lbl.color = on ? UI.Fg : UI.Dim; lbl.alignment = TextAlignmentOptions.Center; }
            b.onClick.AddListener(() => { Loc.SetLang(chosen); SceneManager.LoadScene("Menu"); });
        }

        var audioRow = UI.Box(body, "AudioRow", UI.Clear);
        UI.HStack(audioRow, 8f, TextAnchor.MiddleCenter);
        UI.Sized(audioRow.gameObject, minH: 56f);
        AddPanelToggle(audioRow, "sfx", () => Sfx.SoundEnabled, v => Sfx.SetSoundEnabled(v));
        AddPanelToggle(audioRow, "music", () => Sfx.MusicEnabled, v => Sfx.SetMusicEnabled(v));

        // Legal links — reachable from the home menu (needed for AdSense) but
        // out of the way during play. Open the web privacy/terms pages.
        var legalRow = UI.Box(body, "LegalRow", UI.Clear);
        UI.HStack(legalRow, 8f, TextAnchor.MiddleCenter);
        UI.Sized(legalRow.gameObject, minH: 40f);
        var privacyBtn = UI.Button(legalRow, "Privacy", Loc.T("privacy"), 15f, ghost: true);
        UI.Sized(privacyBtn.gameObject, minH: 36f, flexW: 1f);
        privacyBtn.onClick.AddListener(() => WebBridge.OpenUrl($"{GameSession.ApiOrigin}/privacy"));
        var termsBtn = UI.Button(legalRow, "Terms", Loc.T("terms"), 15f, ghost: true);
        UI.Sized(termsBtn.gameObject, minH: 36f, flexW: 1f);
        termsBtn.onClick.AddListener(() => WebBridge.OpenUrl($"{GameSession.ApiOrigin}/terms"));

        var spacer = UI.Box(body, "Spacer", UI.Clear);
        UI.Sized(spacer.gameObject, flexH: 1f, minH: 4f);

        var close = UI.Button(body, "Close", Loc.T("close"), 24f);
        UI.Sized(close.gameObject, minH: 56f);
        close.onClick.AddListener(() => settingsPanel.gameObject.SetActive(false));
    }

    private static string NameForLang(string lang)
    {
        for (int i = 0; i < Loc.Langs.Length; i++)
            if (Loc.Langs[i] == lang) return Loc.LangNames[i];
        return lang;
    }

    private void AddPanelToggle(Transform parent, string locKey,
        System.Func<bool> get, System.Action<bool> set)
    {
        var btn = UI.Button(parent, $"Toggle_{locKey}", "", 22f);
        UI.Sized(btn.gameObject, minH: 52f, flexW: 1f);
        var tmp = btn.GetComponentInChildren<TMP_Text>();
        tmp.alignment = TextAlignmentOptions.Center;
        void Refresh()
        {
            bool on = get();
            tmp.text = $"{Loc.T(locKey)}: {(on ? Loc.T("on") : Loc.T("off"))}";
            tmp.color = on ? UI.Fg : UI.Dim;
        }
        Refresh();
        btn.onClick.AddListener(() => { set(!get()); Refresh(); });
    }

    private void SetSort(string mode)
    {
        if (mode == currentSort) return;
        currentSort = mode;
        for (int i = 0; i < sortLabels.Count; i++)
        {
            bool active = SortModes[i] == currentSort;
            sortLabels[i].color = active ? UI.Fg : UI.Dim;
            sortLabels[i].fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
            if (i < sortBgs.Count) sortBgs[i].color = active ? UI.Surface2 : UI.Surface1;
            if (i < sortAccents.Count) sortAccents[i].color = active ? UI.Fg : UI.Clear;
        }
        StartCoroutine(ReloadCommunity());
    }

    private IEnumerator ReloadCommunity()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Destroy(entries[i].Root);
            entries.RemoveAt(i);
        }
        yield return LoadCommunityStages();
        yield return FillCommunityPreviews();
        Filter(searchInput != null ? searchInput.text : "");
    }

    private IEnumerator LoadCommunityStages()
    {
        using var request = UnityWebRequest.Get($"{GameSession.ApiOrigin}/api/stages?sort={currentSort}");
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success) yield break;

        var list = JsonUtility.FromJson<PublishedList>(request.downloadHandler.text);
        if (list?.stages == null || list.stages.Length == 0) yield break;

        bool isBest100 = currentSort == "best100";
        foreach (PublishedStage stage in list.stages)
        {
            // BEST 100: an unfilled slot shows a "N. Coming Soon" placeholder
            // (no thumbnail, not tappable) so the list reads as a 100-slot chart.
            if (stage.comingSoon)
            {
                AddEntry($"{stage.rank}. {Loc.T("comingSoon")}", "", null);
                continue;
            }

            string id = stage.id;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(stage.creator)) parts.Add($"{Loc.T("by")} {stage.creator}");
            // Plays are ALWAYS shown (even 0) so the stat is visible from day one.
            parts.Add($"{Loc.T("plays")} {stage.attempts}");
            if (stage.attempts > 0)
                parts.Add($"{Loc.T("clearRate")} {stage.clears * 100 / stage.attempts}%");
            int best = stage.best_time_ms > 0 ? stage.best_time_ms : stage.clear_time_ms;
            if (best > 0) parts.Add($"{Loc.T("best")} {best / 1000f:0.0}s");
            // GOOD% is ALWAYS shown — it's the metric the TOP sort ranks by, so
            // players can see why a stage ranks where it does. "—" until voted.
            int votes = stage.goods + stage.bads;
            parts.Add(votes > 0
                ? $"{Loc.T("good")} {stage.goods * 100 / votes}%"
                : $"{Loc.T("good")} —");

            // Prefix the rank number ("1. ") when showing the BEST 100 chart.
            string title = isBest100 ? $"{stage.rank}. {stage.name}" : stage.name;
            Entry entry = AddEntry(title, string.Join("  ·  ", parts), () =>
            {
                GameSession.RemoteStageId = id;
                // Mark the link consumed so that pressing MENU after this stage
                // returns to the menu instead of the menu re-reading ?stage= from
                // the URL and immediately bouncing back into the stage.
                GameSession.DeepLinkConsumed = true;
                SceneManager.LoadScene("Stage");
            });
            entry.RemoteId = id;
        }
    }

    /// <summary>Progressively fetches community stage JSONs to render thumbnails.</summary>
    private IEnumerator FillCommunityPreviews()
    {
        int fetched = 0;
        foreach (Entry entry in entries)
        {
            if (entry.RemoteId == null || fetched >= 30) continue;
            fetched++;

            using var request = UnityWebRequest.Get($"{GameSession.ApiOrigin}/api/stages/{entry.RemoteId}");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) continue;

            StageData data = JsonUtility.FromJson<StageData>(request.downloadHandler.text);
            if (data != null && entry.Preview != null)
            {
                entry.Preview.texture = StagePreview.Render(data);
            }
        }
    }

    private const float CardHeight = 112f;

    private Entry AddEntry(string title, string subtitle, UnityEngine.Events.UnityAction onClick)
    {
        // Fixed-height card. Everything is anchored DIRECTLY on the row (no nested
        // layout groups that could collapse to 0 height / hide the text).
        var row = new GameObject($"Stage_{title}", typeof(RectTransform), typeof(Image));
        row.transform.SetParent(listRoot, false);
        var cardImage = row.GetComponent<Image>();
        cardImage.color = UI.Surface2;                 // brighter card so text pops
        UI.Sized(row, minH: CardHeight, prefH: CardHeight);

        // onClick == null → a non-interactive placeholder (e.g. a BEST 100
        // "Coming Soon" slot): dimmer, no button, no hover.
        bool interactive = onClick != null;
        if (interactive)
        {
            var button = row.AddComponent<Button>();
            button.targetGraphic = cardImage;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);
            row.AddComponent<HoverTint>().Init(cardImage, UI.Surface2, UI.Surface3);
        }
        else
        {
            cardImage.color = UI.Surface1; // dimmer than an active card
        }

        // left accent bar
        var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
        accent.transform.SetParent(row.transform, false);
        var ar = accent.GetComponent<RectTransform>();
        ar.anchorMin = new Vector2(0f, 0f); ar.anchorMax = new Vector2(0f, 1f);
        ar.pivot = new Vector2(0f, 0.5f); ar.sizeDelta = new Vector2(3f, 0f);
        var aimg = accent.GetComponent<Image>();
        aimg.color = Color.white; aimg.raycastTarget = false;

        // Thumbnail (left), framed with a hairline so it reads as a snapshot.
        // The snapshot texture is 2:1 (192x96), so the frame is 2:1 too — a
        // square frame would squish/clip the wide stage render. Kept modest
        // (150x75) and inset 16px so its left edge never reaches the card edge.
        float thumbH = 75f;
        float thumbW = thumbH * 2f;               // 150, 2:1 to match the texture
        float thumbLeft = 24f;                    // roomy left margin inside the card
        float textLeft = thumbLeft + thumbW + 16f;
        var frame = new GameObject("Thumb", typeof(RectTransform), typeof(Image));
        frame.transform.SetParent(row.transform, false);
        var fr = frame.GetComponent<RectTransform>();
        fr.anchorMin = new Vector2(0f, 0.5f); fr.anchorMax = new Vector2(0f, 0.5f);
        fr.pivot = new Vector2(0f, 0.5f); fr.sizeDelta = new Vector2(thumbW, thumbH);
        fr.anchoredPosition = new Vector2(thumbLeft, 0f);
        frame.GetComponent<Image>().color = Color.black; // bg behind the render
        UI.AddBorder(frame.transform, UI.Line);
        var previewGo = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
        previewGo.transform.SetParent(frame.transform, false);
        var pr = previewGo.GetComponent<RectTransform>();
        pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one;
        pr.offsetMin = new Vector2(2f, 2f); pr.offsetMax = new Vector2(-2f, -2f);
        var preview = previewGo.GetComponent<RawImage>();
        preview.texture = Placeholder(); preview.raycastTarget = false;

        // Course NAME — big, pure white, bold; top portion of the card.
        var nameGo = new GameObject("Name", typeof(RectTransform));
        nameGo.transform.SetParent(row.transform, false);
        var nr = nameGo.GetComponent<RectTransform>();
        nr.anchorMin = new Vector2(0f, 0.5f); nr.anchorMax = new Vector2(1f, 1f);
        nr.offsetMin = new Vector2(textLeft, 2f); nr.offsetMax = new Vector2(-14f, -10f);
        // Course names arrive at runtime and may be Japanese — pre-add their
        // glyphs to the dynamic atlas so they don't render blank on iOS.
        Loc.WarmupText(title);
        var nameT = nameGo.AddComponent<TextMeshProUGUI>();
        nameT.text = title; nameT.color = Color.white;
        nameT.alignment = TextAlignmentOptions.BottomLeft; nameT.characterSpacing = 1f;
        nameT.fontStyle = FontStyles.Bold; nameT.raycastTarget = false;
        nameT.enableWordWrapping = false;
        // AUTO-SIZE: shrink the font to fit the card width instead of truncating
        // with an ellipsis, so long names (and "N. Coming Soon" on narrow phones)
        // stay fully legible on every device size. Ellipsis is the last resort.
        nameT.enableAutoSizing = true;
        nameT.fontSizeMin = 16f; nameT.fontSizeMax = 32f;
        nameT.overflowMode = TextOverflowModes.Ellipsis;
        nameT.richText = false; // user-supplied names must never inject TMP tags

        // Stat line — brighter than before so it's legible; bottom portion.
        var subGo = new GameObject("Sub", typeof(RectTransform));
        subGo.transform.SetParent(row.transform, false);
        var sr = subGo.GetComponent<RectTransform>();
        sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 0.5f);
        sr.offsetMin = new Vector2(textLeft, 10f); sr.offsetMax = new Vector2(-14f, -2f);
        Loc.WarmupText(subtitle);
        var subT = subGo.AddComponent<TextMeshProUGUI>();
        subT.text = subtitle;
        subT.color = new Color(1f, 1f, 1f, 0.72f);     // was 0.5 — now clearly readable
        subT.alignment = TextAlignmentOptions.TopLeft; subT.characterSpacing = 0.5f;
        subT.raycastTarget = false; subT.enableWordWrapping = false;
        subT.enableAutoSizing = true; subT.fontSizeMin = 11f; subT.fontSizeMax = 17f;
        subT.overflowMode = TextOverflowModes.Ellipsis;

        var entry = new Entry { Root = row, Title = title, Preview = preview };
        entries.Add(entry);
        return entry;
    }

    private void AddLabel(string title)
    {
        var go = new GameObject($"Label_{title}", typeof(RectTransform));
        go.transform.SetParent(listRoot, false);
        UI.Sized(go, minH: 40f, prefH: 40f);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = title; tmp.fontSize = 22; tmp.characterSpacing = 8f;
        tmp.alignment = TextAlignmentOptions.Center; tmp.color = UI.Dim;

        entries.Add(new Entry { Root = go, Title = title, IsLabel = true });
    }

    private void Filter(string query)
    {
        string q = (query ?? "").Trim().ToLowerInvariant();
        foreach (Entry entry in entries)
        {
            bool visible = q.Length == 0
                ? true
                : !entry.IsLabel && entry.Title.ToLowerInvariant().Contains(q);
            entry.Root.SetActive(visible);
        }
    }

    private static Texture2D Placeholder()
    {
        if (placeholderTex != null) return placeholderTex;
        placeholderTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        placeholderTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f));
        placeholderTex.Apply();
        return placeholderTex;
    }

    [System.Serializable]
    private class PublishedList
    {
        public PublishedStage[] stages;
    }

    [System.Serializable]
    private class PublishedStage
    {
        public string id;
        public string name;
        public string creator;
        public int clear_time_ms;
        public int best_time_ms;
        public int goods;
        public int bads;
        public int attempts;
        public int clears;
        public int rank;         // BEST 100 rank (1-based); 0 for other sorts
        public bool comingSoon;  // BEST 100 placeholder slot
    }
}
