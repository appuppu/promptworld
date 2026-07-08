using TMPro;
using UnityEngine;

public enum GameState { Playing, Cleared, GameOver }

/// <summary>
/// Owns the run lifecycle: countdown timer, stage-clear and game-over
/// transitions. Stage-specific values (time limit, etc.) live in serialized
/// fields so they can be loaded from deployed stage JSON on Day 2+.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Stage Settings")]
    [SerializeField] private float timeLimit = 60f;

    [Header("References")]
    [SerializeField] private PlayerController player;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text resultText;

    public GameState State { get; private set; } = GameState.Playing;

    private float timeRemaining;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        timeRemaining = timeLimit;
        resultText.gameObject.SetActive(false);
        UpdateTimerLabel();
    }

    private void Update()
    {
        if (State != GameState.Playing) return;

        timeRemaining -= Time.deltaTime;
        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndGame(GameState.GameOver, "Game Over");
        }
        UpdateTimerLabel();
    }

    /// <summary>Called by Goal when the player reaches it.</summary>
    public void StageClear()
    {
        if (State != GameState.Playing) return;
        EndGame(GameState.Cleared, "Stage Clear!");
    }

    private void EndGame(GameState result, string message)
    {
        State = result;
        player.Freeze();
        resultText.text = message;
        resultText.gameObject.SetActive(true);
        Debug.Log(message);
    }

    private void UpdateTimerLabel()
    {
        timerText.text = Mathf.CeilToInt(timeRemaining).ToString();
    }
}
