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
    public void ConfigureSim(string stageName, float stageTimeLimit)
    {
        simDriven = true;
        timeLimit = stageTimeLimit;
        timeRemaining = stageTimeLimit;
        lastTickSecond = -1;
        if (stageNameText != null) stageNameText.text = stageName;
        UpdateTimerLabel();
        Sfx.StartMusic();
    }

    private void Start()
    {
        timeRemaining = timeLimit;
        resultText.gameObject.SetActive(false);
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
    }

    /// <summary>Appends the current world-best time to the stage label.</summary>
    public void SetPar(int bestMs)
    {
        if (stageNameText != null)
        {
            stageNameText.text += $"   ·   BEST {bestMs / 1000f:0.00}s";
        }
    }

    private void SharePlayUrl()
    {
        WebBridge.Copy($"{GameSession.ApiOrigin}/?stage={GameSession.RemoteStageId}");
        var label = shareButton.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = "COPIED!";
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
            EndGame(GameState.GameOver, "Game Over");
        }
        TickSecond();
        UpdateTimerLabel();
    }

    /// <summary>Called by SimDriver every fixed step.</summary>
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
        EndGame(GameState.Cleared, $"Stage Clear!\n<size=45%>{clearTimeMs / 1000f:0.00}s</size>");
        ReportPlayOutcome(deaths, true);
    }

    public void GameOverFromSim(int deaths)
    {
        if (State != GameState.Playing) return;
        EndGame(GameState.GameOver, "Game Over");
        ReportPlayOutcome(deaths, false);
    }

    /// <summary>Anonymous play stats feed each stage's public clear rate.</summary>
    private void ReportPlayOutcome(int deaths, bool cleared)
    {
        if (!IsPublicRemoteSession()) return;
        int attempts = deaths + 1;
        string payload = $"{{\"attempts\":{attempts},\"clears\":{(cleared ? 1 : 0)}}}";
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
            var sb = new System.Text.StringBuilder("<size=75%>BEST TIMES</size>\n");
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
        EndGame(GameState.Cleared, "Stage Clear!");
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
        SceneManager.LoadScene("Menu");
    }

    private void EndGame(GameState result, string message)
    {
        State = result;
        if (player != null) player.Freeze();
        Sfx.StopMusic();
        Sfx.Play(result == GameState.Cleared ? SfxId.Clear : SfxId.GameOver);
        ShowResultOverlay();
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
            resultText.text += "\n<size=30%>CLEAR VERIFIED — READY TO PUBLISH</size>";
            Debug.Log("[PromptWorld] Replay-verified clear recorded.");
        }
        else
        {
            resultText.text += "\n<size=30%>CLEAR VERIFICATION FAILED</size>";
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
