using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Gesture-based touch input (no on-screen buttons):
/// - hold the left / right half of the screen to run that way
/// - quick tap anywhere to jump
/// - flick upward (even mid-hold) to jump
/// Desktop players never see or feel any of this.
/// </summary>
public class TouchControls : MonoBehaviour
{
    [SerializeField] private Canvas canvas;

    private const float TapMaxDuration = 0.22f;   // s
    private const float MoveGraceDelay = 0.1f;    // s before a hold starts steering
    private const float TapMaxMoveDp = 40f;       // density-independent px
    private const float SwipeJumpDp = 70f;

    private class TouchState
    {
        public Vector2 startPos;
        public float startTime;
        public bool jumped;
    }

    private readonly Dictionary<int, TouchState> activeTouches = new Dictionary<int, TouchState>();
    private float dpiScale = 1f;

    private void Start()
    {
        if (!Application.isMobilePlatform && !Input.touchSupported)
        {
            enabled = false;
            return;
        }
        dpiScale = Screen.dpi > 0f ? Screen.dpi / 160f : 2f;
        if (canvas == null) canvas = GetComponent<Canvas>();
        StartCoroutine(ShowHint());
    }

    private void Update()
    {
        float axis = 0f;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    activeTouches[touch.fingerId] = new TouchState
                    {
                        startPos = touch.position,
                        startTime = Time.unscaledTime,
                    };
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                {
                    if (!activeTouches.TryGetValue(touch.fingerId, out TouchState state)) break;

                    if (!state.jumped && touch.position.y - state.startPos.y > SwipeJumpDp * dpiScale)
                    {
                        state.jumped = true;
                        MobileInput.QueueJump();
                    }
                    if (Time.unscaledTime - state.startTime > MoveGraceDelay)
                    {
                        axis += touch.position.x < Screen.width / 2f ? -1f : 1f;
                    }
                    break;
                }

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                {
                    if (!activeTouches.TryGetValue(touch.fingerId, out TouchState state)) break;

                    bool quick = Time.unscaledTime - state.startTime < TapMaxDuration;
                    bool still = (touch.position - state.startPos).magnitude < TapMaxMoveDp * dpiScale;
                    if (!state.jumped && quick && still)
                    {
                        MobileInput.QueueJump();
                    }
                    activeTouches.Remove(touch.fingerId);
                    break;
                }
            }
        }

        MobileInput.LeftHeld = axis < 0f;
        MobileInput.RightHeld = axis > 0f;
    }

    private IEnumerator ShowHint()
    {
        var go = new GameObject("GestureHint", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 70f);
        rect.sizeDelta = new Vector2(1200f, 60f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "HOLD SIDE TO MOVE  ·  TAP TO JUMP";
        tmp.fontSize = 32;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, 0.55f);
        tmp.raycastTarget = false;

        yield return new WaitForSeconds(5f);
        for (float t = 1f; t > 0f; t -= Time.deltaTime)
        {
            tmp.color = new Color(1f, 1f, 1f, 0.55f * t);
            yield return null;
        }
        Destroy(go);
    }
}
