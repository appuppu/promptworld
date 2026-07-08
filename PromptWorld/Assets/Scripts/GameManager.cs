using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameState { Playing, Cleared, GameOver }

/// <summary>
/// Owns the run lifecycle: countdown timer, clear / game-over transitions,
/// kill-zone respawns and restarts. Stage-specific values arrive from
/// StageLoader via Configure() — nothing here is hardcoded to one stage.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Stage Settings (overridden by StageLoader)")]
    [SerializeField] private float timeLimit = 60f;
    [SerializeField] private float killZoneBottom = -12f;
    [SerializeField] private float killZoneTop = 15f;

    [Header("References")]
    [SerializeField] private PlayerController player;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text stageNameText;

    public GameState State { get; private set; } = GameState.Playing;

    private float timeRemaining;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>Called by StageLoader after building the stage from JSON.</summary>
    public void Configure(PlayerController stagePlayer, string stageName, float stageTimeLimit)
    {
        player = stagePlayer;
        timeLimit = stageTimeLimit;
        if (stageNameText != null) stageNameText.text = stageName;
    }

    private void Start()
    {
        timeRemaining = timeLimit;
        resultText.gameObject.SetActive(false);
        UpdateTimerLabel();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartStage();
            return;
        }

        if (State != GameState.Playing) return;

        timeRemaining -= Time.deltaTime;
        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndGame(GameState.GameOver, "Game Over");
        }
        UpdateTimerLabel();

        if (player != null)
        {
            float y = player.transform.position.y;
            if (y < killZoneBottom || y > killZoneTop) RespawnPlayer();
        }
    }

    /// <summary>Called by Goal when the player reaches it.</summary>
    public void StageClear()
    {
        if (State != GameState.Playing) return;
        EndGame(GameState.Cleared, "Stage Clear!");
    }

    /// <summary>Hazards and kill zones send the player back to the start. The clock keeps running.</summary>
    public void RespawnPlayer()
    {
        if (State != GameState.Playing) return;
        player.Respawn();
    }

    public void RestartStage()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void EndGame(GameState result, string message)
    {
        State = result;
        player.Freeze();
        resultText.text = $"{message}\n<size=35%>Press R to Restart</size>";
        resultText.gameObject.SetActive(true);
        Debug.Log(message);
    }

    private void UpdateTimerLabel()
    {
        timerText.text = Mathf.CeilToInt(timeRemaining).ToString();
    }
}
