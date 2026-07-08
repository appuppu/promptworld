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
        }
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
    public void StageClearFromSim(int clearTimeMs, string replayJson)
    {
        if (State != GameState.Playing) return;
        pendingClearMs = clearTimeMs;
        pendingReplay = replayJson;
        EndGame(GameState.Cleared, "Stage Clear!");
    }

    public void GameOverFromSim()
    {
        if (State != GameState.Playing) return;
        EndGame(GameState.GameOver, "Game Over");
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
        Sfx.Play(result == GameState.Cleared ? SfxId.Clear : SfxId.GameOver);
        ShowResultOverlay();
        resultText.text = message;
        resultText.gameObject.SetActive(true);
        if (retryButton != null) retryButton.gameObject.SetActive(true);
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
