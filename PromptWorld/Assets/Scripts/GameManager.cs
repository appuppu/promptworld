using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum GameState { Playing, Cleared, GameOver }

/// <summary>
/// Owns the run lifecycle UI and transitions. In sim-driven stages (all
/// server/JSON stages) the deterministic sim owns time, deaths and the
/// clear — this class renders state and reports replay-certified clears.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Stage Settings (overridden by StageLoader)")]
    [SerializeField] private float timeLimit = 60f;

    [Header("References")]
    [SerializeField] private PlayerController player; // legacy scenes only
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text stageNameText;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button menuButton;
    [SerializeField] private Button shareButton;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button createLinkButton;
    [SerializeField] private Button voteGoodButton;
    [SerializeField] private Button voteBadButton;
    [SerializeField] private TMP_Text leaderboardText;
    [SerializeField] private Button homeButton;
    [SerializeField] private TMP_Text keyText;
    [SerializeField] private TMP_Text livesText;
    [SerializeField] private GameObject resultRoot; // dim backdrop; only shown on clear/game-over

    public GameState State { get; private set; } = GameState.Playing;

    private bool simDriven;
    private float timeRemaining;
    private int lastTickSecond = -1;
    private int pendingClearMs = -1;
    private string pendingReplay;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>Sim-driven stages: the sim owns the clock; we only display it.</summary>
    public void ConfigureSim(string stageName, float stageTimeLimit, bool hasBoss = false, MusicData music = null)
    {
        simDriven = true;
        timeLimit = stageTimeLimit;
        timeRemaining = stageTimeLimit;
        lastTickSecond = -1;
        if (stageNameText != null) stageNameText.text = stageName;
        UpdateTimerLabel();
        // A stage's own "music" recipe takes priority — the author chose it on
        // purpose. Otherwise fall back to the default loops.
        if (music != null) Sfx.StartStageMusic(music);
        else if (hasBoss) Sfx.StartBossMusic();
        else Sfx.StartMusic();
    }

    // Turn on TMP auto-sizing so a label shrinks to fit its box instead of
    // clipping with an ellipsis. min/max bound the range; max = the scene's
    // intended size. Safe on a null ref.
    private static void FitText(TMP_Text t, float min, float max)
    {
        if (t == null) return;
        t.enableAutoSizing = true;
        t.fontSizeMin = min;
        t.fontSizeMax = max;
    }

    // Same, for a Button's child label.
    private static void FitButton(Button b, float min, float max)
    {
        if (b == null) return;
        var t = b.GetComponentInChildren<TMP_Text>();
        FitText(t, min, max);
    }

    private void Start()
    {
        Loc.WarmupFont(); // pre-rasterize glyphs so non-Latin HUD text isn't blank
        // Make every HUD / result / button label AUTO-SIZE so nothing clips on
        // narrow phones or in longer localized languages (the scene assets ship
        // with autosizing off / fixed font sizes). Shrinks to fit; never truncates.
        FitText(resultText, 24f, 60f);
        FitText(stageNameText, 14f, 24f);
        FitText(keyText, 14f, 26f);
        FitText(livesText, 14f, 26f);
        FitText(timerText, 18f, 34f);
        FitText(leaderboardText, 16f, 22f);
        FitButton(retryButton, 12f, 24f);
        FitButton(menuButton, 12f, 24f);
        FitButton(shareButton, 12f, 24f);
        FitButton(nextButton, 12f, 24f);
        FitButton(createLinkButton, 12f, 22f);
        FitButton(voteGoodButton, 12f, 24f);
        FitButton(voteBadButton, 12f, 24f);
        timeRemaining = timeLimit;
        resultText.gameObject.SetActive(false);
        if (resultRoot != null) resultRoot.SetActive(false); // no dim overlay while playing
        UpdateTimerLabel();

        if (retryButton != null)
        {
            retryButton.onClick.AddListener(RestartStage);
            retryButton.gameObject.SetActive(false);
        }
        if (menuButton != null)
        {
            menuButton.onClick.AddListener(BackToMenu);
            menuButton.gameObject.SetActive(false);
        }
        if (shareButton != null)
        {
            shareButton.onClick.AddListener(SharePlayUrl);
            shareButton.gameObject.SetActive(false);
        }
        if (nextButton != null)
        {
            nextButton.onClick.AddListener(() => StartCoroutine(NextRandomStage()));
            nextButton.gameObject.SetActive(false);
        }
        if (createLinkButton != null)
        {
            createLinkButton.onClick.AddListener(() => WebBridge.OpenUrl($"{GameSession.ApiOrigin}/create"));
            createLinkButton.gameObject.SetActive(false);
        }
        if (voteGoodButton != null)
        {
            voteGoodButton.onClick.AddListener(() => StartCoroutine(SubmitVote(true)));
            voteGoodButton.gameObject.SetActive(false);
        }
        if (voteBadButton != null)
        {
            voteBadButton.onClick.AddListener(() => StartCoroutine(SubmitVote(false)));
            voteBadButton.gameObject.SetActive(false);
        }
        if (leaderboardText != null) leaderboardText.text = "";
        if (homeButton != null) homeButton.onClick.AddListener(BackToMenu);

        // Localize the baked-in button labels to the detected language.
        SetButtonLabel(retryButton, Loc.T("retry"));
        SetButtonLabel(menuButton, Loc.T("menu"));
        SetButtonLabel(nextButton, Loc.T("next"));
        SetButtonLabel(shareButton, Loc.T("share"));
        SetButtonLabel(createLinkButton, Loc.T("createOwn"));
        SetButtonLabel(voteGoodButton, Loc.T("good"));
        SetButtonLabel(voteBadButton, Loc.T("bad"));
        SetButtonLabel(homeButton, Loc.T("menu"));
    }

    private static void SetButtonLabel(Button b, string text)
    {
        if (b == null) return;
        var label = b.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = text;
    }

    /// <summary>Appends the current world-best time to the stage label.</summary>
    public void SetPar(int bestMs)
    {
        if (stageNameText != null)
        {
            stageNameText.text += $"   ·   {Loc.T("best")} {bestMs / 1000f:0.00}s";
        }
    }

    private void SharePlayUrl()
    {
        string url = $"{GameSession.ApiOrigin}/?stage={GameSession.RemoteStageId}";
        string title = string.IsNullOrEmpty(stageNameText != null ? stageNameText.text : null)
            ? "this stage" : StripTags(stageNameText.text);
        // A ready-to-post caption; opens the native share sheet (Instagram/X on
        // mobile) or an X compose window on desktop, with the URL on clipboard.
        WebBridge.Share(Loc.T("shareCaption", title), url);
        var label = shareButton.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = Loc.T("copied");
    }

    private static string StripTags(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        int cut = s.IndexOf('\n');
        if (cut >= 0) s = s.Substring(0, cut);        // drop the "· BEST ..." suffix line
        cut = s.IndexOf("   ·");
        if (cut >= 0) s = s.Substring(0, cut);
        return s.Trim();
    }

    private IEnumerator NextRandomStage()
    {
        using var request = UnityWebRequest.Get($"{GameSession.ApiOrigin}/api/stages");
        yield return request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            var list = JsonUtility.FromJson<StageList>(request.downloadHandler.text);
            if (list?.stages != null && list.stages.Length > 0)
            {
                var candidates = new System.Collections.Generic.List<string>();
                foreach (StageListEntry stage in list.stages)
                {
                    if (stage.id != GameSession.RemoteStageId) candidates.Add(stage.id);
                }
                if (candidates.Count > 0)
                {
                    GameSession.RemoteStageId = candidates[Random.Range(0, candidates.Count)];
                    GameSession.EditKey = null;
                    GameSession.SelectedStageFile = null;
                    // Consumed: a later MENU press should return to the menu, not
                    // re-trigger a deep-link jump from the URL.
                    GameSession.DeepLinkConsumed = true;
                    SceneManager.LoadScene("Stage");
                    yield break;
                }
            }
        }
        BackToMenu();
    }

    [System.Serializable]
    private class StageList
    {
        public StageListEntry[] stages;
    }

    [System.Serializable]
    private class StageListEntry
    {
        public string id;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartStage();
            return;
        }

        if (simDriven || State != GameState.Playing) return;

        // Legacy (non-sim) scenes keep their own wall-clock countdown.
        timeRemaining -= Time.deltaTime;
        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndGame(GameState.GameOver, Loc.T("gameOver"));
        }
        TickSecond();
        UpdateTimerLabel();
    }

    /// <summary>Called by SimDriver every fixed step.</summary>
    /// <summary>Shows a "KEYS n / total — reach the door" prompt when the stage has keys.</summary>
    public void UpdateKeys(int collected, int total)
    {
        if (keyText == null) return;
        if (total <= 0)
        {
            keyText.gameObject.SetActive(false);
            return;
        }
        keyText.gameObject.SetActive(true);
        if (collected >= total)
        {
            keyText.text = Loc.T("keysDoorOpen", collected, total);
            keyText.color = new Color(1f, 1f, 1f, 0.95f);
        }
        else
        {
            keyText.text = Loc.T("keysCollect", collected, total);
            keyText.color = new Color(1f, 1f, 1f, 0.7f);
        }
    }

    private RectTransform livesRow;
    private Image[] heartIcons;
    private static Sprite heartSprite;

    /// <summary>
    /// Shows remaining lives as a prominent row of heart icons under the timer.
    /// Filled hearts = lives left, dim outlines = lost. Drawn as generated
    /// sprites (no font glyphs) so it renders identically everywhere.
    /// </summary>
    public void UpdateLives(int left, int max)
    {
        if (max <= 0)
        {
            if (livesRow != null) livesRow.gameObject.SetActive(false);
            if (livesText != null) livesText.gameObject.SetActive(false);
            return;
        }
        if (left < 0) left = 0;
        if (left > max) left = max;

        // Many lives (>12) would overflow a row of icons: show one heart + count.
        if (max > 12)
        {
            if (livesRow != null) livesRow.gameObject.SetActive(false);
            if (livesText != null)
            {
                livesText.gameObject.SetActive(true);
                livesText.alignment = TextAlignmentOptions.Left;
                livesText.rectTransform.anchorMin = new Vector2(0f, 1f);
                livesText.rectTransform.anchorMax = new Vector2(0f, 1f);
                livesText.rectTransform.pivot = new Vector2(0f, 1f);
                livesText.rectTransform.anchoredPosition = new Vector2(40f, -84f);
                livesText.fontSize = 26;
                livesText.text = $"{Loc.T("lives")}  {left} / {max}";
                livesText.color = left <= 3 ? new Color(1f, 0.85f, 0.85f, 1f) : Color.white;
            }
            return;
        }

        // The old ASCII label is retired in favor of heart icons.
        if (livesText != null) livesText.gameObject.SetActive(false);

        EnsureLivesRow(max);
        livesRow.gameObject.SetActive(true);
        for (int i = 0; i < heartIcons.Length; i++)
        {
            bool alive = i < left;
            heartIcons[i].color = alive
                ? new Color(1f, 1f, 1f, 1f)          // full heart
                : new Color(1f, 1f, 1f, 0.18f);      // lost heart (ghosted)
        }
    }

    /// <summary>Lazily builds the heart row inside the HUD stack (under the timer).</summary>
    private void EnsureLivesRow(int max)
    {
        if (livesRow != null && heartIcons != null && heartIcons.Length == max) return;

        // Live inside the same auto-layout HUD column as the timer/livesText so
        // it stacks under them and never overlaps at any aspect.
        Transform hud = livesText != null ? livesText.transform.parent
            : (timerText != null ? timerText.transform.parent : transform);

        if (livesRow != null) Destroy(livesRow.gameObject);

        var rowGo = new GameObject("LivesRow", typeof(RectTransform));
        rowGo.transform.SetParent(hud, false);
        // Place right after the timer (index 1) so it reads timer → hearts.
        rowGo.transform.SetSiblingIndex(1);
        livesRow = rowGo.GetComponent<RectTransform>();
        livesRow.anchorMin = new Vector2(0f, 1f);
        livesRow.anchorMax = new Vector2(0f, 1f);
        livesRow.pivot = new Vector2(0f, 1f);

        const float sizePx = 22f;
        const float gapPx = 5f;
        float step = sizePx + gapPx;
        // A layout element so the HUD VStack reserves height for it.
        var le = rowGo.AddComponent<LayoutElement>();
        le.minHeight = sizePx; le.preferredHeight = sizePx;

        heartIcons = new Image[max];
        for (int i = 0; i < max; i++)
        {
            var h = new GameObject($"Heart{i}", typeof(RectTransform));
            h.transform.SetParent(livesRow, false);
            var hr = h.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0f, 1f);
            hr.anchorMax = new Vector2(0f, 1f);
            hr.pivot = new Vector2(0f, 1f);
            hr.anchoredPosition = new Vector2(i * step, 0f); // grow rightward from the left edge
            hr.sizeDelta = new Vector2(sizePx, sizePx);
            var img = h.AddComponent<Image>();
            img.sprite = GetHeartSprite();
            img.raycastTarget = false;
            heartIcons[i] = img;
        }
    }

    /// <summary>A filled heart sprite generated in memory (no asset dependency).</summary>
    private static Sprite GetHeartSprite()
    {
        if (heartSprite != null) return heartSprite;

        const int n = 48;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var clear = new Color32(255, 255, 255, 0);
        var solid = new Color32(255, 255, 255, 255);
        var px = new Color32[n * n];
        for (int yy = 0; yy < n; yy++)
        {
            for (int xx = 0; xx < n; xx++)
            {
                // Texture row 0 is the BOTTOM in Unity, so map yy=0 to y=-1 to
                // keep the heart upright (lobes at top, point at bottom).
                float x = (xx + 0.5f) / n * 2f - 1f;
                float y = (yy + 0.5f) / n * 2f - 1f;
                // Heart implicit curve: (x^2 + y^2 - 1)^3 - x^2 y^3 <= 0
                float xs = x * 1.15f;
                float ys = y * 1.15f + 0.35f;
                float a = xs * xs + ys * ys - 1f;
                float f = a * a * a - xs * xs * ys * ys * ys;
                px[yy * n + xx] = f <= 0f ? solid : clear;
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        heartSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        return heartSprite;
    }

    public void OnSimTick(int remainingTicks)
    {
        if (State != GameState.Playing) return;
        timeRemaining = remainingTicks * 0.02f;
        if (timeRemaining < 0f) timeRemaining = 0f;
        TickSecond();
        UpdateTimerLabel();
    }

    private void TickSecond()
    {
        int second = Mathf.CeilToInt(timeRemaining);
        if (second != lastTickSecond)
        {
            lastTickSecond = second;
            if (second > 0 && second <= 10) Sfx.Play(SfxId.Tick, 0.35f);
        }
    }

    /// <summary>Called by SimDriver when the sim reaches the goal.</summary>
    public void StageClearFromSim(int clearTimeMs, string replayJson, int deaths)
    {
        if (State != GameState.Playing) return;
        pendingClearMs = clearTimeMs;
        pendingReplay = replayJson;
        EndGame(GameState.Cleared, $"{Loc.T("stageClear")}\n<size=45%>{clearTimeMs / 1000f:0.00}s</size>");
        ReportPlayOutcome(deaths, true);
    }

    public void GameOverFromSim(int deaths, bool livesOut = false)
    {
        if (State != GameState.Playing) return;
        string reason = livesOut ? Loc.T("outOfLives") : Loc.T("timeUp");
        string msg = $"{Loc.T("gameOver")}\n<size=45%>{reason}</size>";
        EndGame(GameState.GameOver, msg);
        ReportPlayOutcome(deaths, false);
    }

    /// <summary>Anonymous play stats feed each stage's public clear rate.</summary>
    private void ReportPlayOutcome(int deaths, bool cleared)
    {
        if (!IsPublicRemoteSession()) return;
        // The server deduplicates per device: this play counts once no matter
        // how many times the same player retries.
        string payload = $"{{\"playerId\":\"{PlayerIdentity.Id}\",\"cleared\":{(cleared ? "true" : "false")}}}";
        StartCoroutine(PostJson($"{GameSession.ApiOrigin}/api/stages/{GameSession.RemoteStageId}/stats", payload, null));

        if (cleared) StartCoroutine(SubmitScore());
    }

    private bool IsPublicRemoteSession()
    {
        return !string.IsNullOrEmpty(GameSession.RemoteStageId) && string.IsNullOrEmpty(GameSession.EditKey);
    }

    private IEnumerator SubmitScore()
    {
        if (string.IsNullOrEmpty(pendingReplay)) yield break;
        string name = JsonEscape(PlayerIdentity.DisplayName);
        string payload = $"{{\"playerId\":\"{PlayerIdentity.Id}\",\"name\":\"{name}\",\"replay\":{pendingReplay}}}";
        yield return PostJson($"{GameSession.ApiOrigin}/api/stages/{GameSession.RemoteStageId}/score", payload, response =>
        {
            var result = JsonUtility.FromJson<ScoreResponse>(response);
            if (result?.top == null || result.top.Length == 0 || leaderboardText == null) return;
            var sb = new System.Text.StringBuilder($"<size=75%>{Loc.T("bestTimes")}</size>\n");
            for (int i = 0; i < result.top.Length; i++)
            {
                sb.Append($"{i + 1}.  {result.top[i].name}   {result.top[i].time_ms / 1000f:0.00}s\n");
            }
            leaderboardText.text = sb.ToString();
        });
    }

    private IEnumerator SubmitVote(bool good)
    {
        SetVoteVisual(good);
        string payload = $"{{\"playerId\":\"{PlayerIdentity.Id}\",\"good\":{(good ? "true" : "false")}}}";
        yield return PostJson($"{GameSession.ApiOrigin}/api/stages/{GameSession.RemoteStageId}/vote", payload, null);
    }

    private void SetVoteVisual(bool good)
    {
        SetButtonAlpha(voteGoodButton, good ? 1f : 0.35f);
        SetButtonAlpha(voteBadButton, good ? 0.35f : 1f);
    }

    private static void SetButtonAlpha(Button button, float alpha)
    {
        if (button == null) return;
        var label = button.GetComponentInChildren<TMP_Text>();
        if (label != null) label.color = new Color(1f, 1f, 1f, alpha);
    }

    private IEnumerator PostJson(string url, string payload, System.Action<string> onSuccess)
    {
        using var request = UnityWebRequest.Post(url, payload, "application/json");
        yield return request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            onSuccess?.Invoke(request.downloadHandler.text);
        }
    }

    private static string JsonEscape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    [System.Serializable]
    private class ScoreResponse
    {
        public int timeMs;
        public ScoreEntry[] top;
    }

    [System.Serializable]
    private class ScoreEntry
    {
        public string name;
        public int time_ms;
    }

    /// <summary>Legacy path (Goal component in non-sim scenes).</summary>
    public void StageClear()
    {
        if (State != GameState.Playing) return;
        EndGame(GameState.Cleared, Loc.T("stageClear"));
    }

    public void RespawnPlayer()
    {
        if (State != GameState.Playing) return;
        if (player != null) player.Respawn();
    }

    public void RestartStage()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void BackToMenu()
    {
        // Deliberately returning to the menu: wipe the deep-link/session so the
        // menu can't re-read a leftover ?stage= (or a re-fired deepLinkActivated)
        // and bounce us straight back into the stage we just left.
        GameSession.RemoteStageId = null;
        GameSession.EditKey = null;
        GameSession.SelectedStageFile = null;
        GameSession.DeepLinkConsumed = true;
        WebBridge.SetUrlStage(""); // clear ?stage= from the address bar immediately
        SceneManager.LoadScene("Menu");
    }

    private void EndGame(GameState result, string message)
    {
        State = result;
        if (player != null) player.Freeze();
        Sfx.StopMusic();
        Sfx.Play(result == GameState.Cleared ? SfxId.Clear : SfxId.GameOver);
        // Ad policy: show an interstitial IMMEDIATELY on GAME OVER only.
        // No ad on CLEAR — the clear screen (share / next / vote) stays clean.
        // 45s frequency cap is enforced inside Ads.ShowInterstitial().
        if (result == GameState.GameOver) Ads.ShowInterstitial();
        if (resultRoot != null) resultRoot.SetActive(true); // now show the dim backdrop
        else ShowResultOverlay();                            // legacy scenes without a ResultRoot
        resultText.text = message;
        resultText.gameObject.SetActive(true);
        if (retryButton != null) retryButton.gameObject.SetActive(true);
        if (menuButton != null) menuButton.gameObject.SetActive(true);
        if (result == GameState.Cleared)
        {
            if (shareButton != null && !string.IsNullOrEmpty(GameSession.RemoteStageId))
            {
                shareButton.gameObject.SetActive(true);
            }
            if (nextButton != null) nextButton.gameObject.SetActive(true);
            if (createLinkButton != null) createLinkButton.gameObject.SetActive(true);
        }
        if (IsPublicRemoteSession())
        {
            if (voteGoodButton != null) voteGoodButton.gameObject.SetActive(true);
            if (voteBadButton != null) voteBadButton.gameObject.SetActive(true);
        }
        Debug.Log(message);

        // Creator test session: submit the replay certificate so the stage
        // can be published once the server re-simulation confirms the clear.
        if (result == GameState.Cleared &&
            !string.IsNullOrEmpty(GameSession.RemoteStageId) &&
            !string.IsNullOrEmpty(GameSession.EditKey))
        {
            int ms = pendingClearMs >= 0 ? pendingClearMs : Mathf.RoundToInt((timeLimit - timeRemaining) * 1000f);
            StartCoroutine(ReportTestClear(ms, pendingReplay));
        }
    }

    private IEnumerator ReportTestClear(int clearTimeMs, string replayJson)
    {
        string payload = "{\"editKey\":\"" + GameSession.EditKey + "\",\"clearTimeMs\":" + clearTimeMs +
                         ",\"replay\":" + (string.IsNullOrEmpty(replayJson) ? "null" : replayJson) + "}";
        string url = $"{GameSession.ApiOrigin}/api/stages/{GameSession.RemoteStageId}/clear";
        using var request = UnityWebRequest.Post(url, payload, "application/json");
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            resultText.text += $"\n<size=30%>{Loc.T("clearVerified")}</size>";
            Debug.Log("[PromptWorld] Replay-verified clear recorded.");
        }
        else
        {
            resultText.text += $"\n<size=30%>{Loc.T("clearFailed")}</size>";
            Debug.LogError($"[PromptWorld] Failed to record clear: {request.error} {request.downloadHandler.text}");
        }
    }

    private void UpdateTimerLabel()
    {
        timerText.text = Mathf.CeilToInt(timeRemaining).ToString();
    }

    /// <summary>Black full-screen panel behind the result text so it reads clearly over any stage.</summary>
    private void ShowResultOverlay()
    {
        var overlay = new GameObject("ResultOverlay", typeof(RectTransform));
        overlay.transform.SetParent(resultText.transform.parent, false);
        overlay.transform.SetSiblingIndex(resultText.transform.GetSiblingIndex());

        var rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;

        var image = overlay.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.92f);
        image.raycastTarget = false;
    }
}
