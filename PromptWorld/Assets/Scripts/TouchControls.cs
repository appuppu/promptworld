using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Touch input, split-screen scheme:
/// - LEFT half  = movement stick: touch down, then slide the finger left or
///   right of the touch point to run that way (small dead zone).
/// - RIGHT half = jump: fires the instant a finger touches down; tap again
///   to jump again.
/// Desktop players never see or feel any of this.
/// </summary>
public class TouchControls : MonoBehaviour
{
    [SerializeField] private Canvas canvas;

    private const float DeadZoneDp = 15f;   // density-independent px
    private const float HudStripPx = 90f;   // top HUD band (timer/lives/MENU) — taps here go to UI, not gameplay

    private int moveFingerId = -1;
    private float moveAnchorX;
    private float dpiScale = 1f;

    private void Start()
    {
        if (canvas == null) canvas = GetComponent<Canvas>();
        bool touch = Application.isMobilePlatform || Input.touchSupported;
        StartCoroutine(ShowHint(touch
            ? "LEFT: SLIDE TO MOVE   ·   RIGHT: TAP TO JUMP"
            : "MOVE: A/D or ARROWS   ·   JUMP: SPACE   ·   RETRY: R"));
        if (!touch)
        {
            enabled = false; // keyboard players need no touch polling (coroutine keeps running)
            return;
        }
        dpiScale = Screen.dpi > 0f ? Screen.dpi / 160f : 2f;
    }

    private void Update()
    {
        // Once the run is over (Cleared / Game Over), the result panel with its
        // MENU / RETRY buttons is up. Stop consuming touches entirely so those
        // buttons work — otherwise a tap gets eaten as a jump/move and the
        // buttons never fire. (IsPointerOverGameObject alone is unreliable on
        // WebGL, so we gate on game state, which is authoritative.)
        var gm = GameManager.Instance;
        if (gm != null && gm.State != GameState.Playing)
        {
            MobileInput.LeftHeld = false;
            MobileInput.RightHeld = false;
            moveFingerId = -1;
            return;
        }

        float halfWidth = Screen.width / 2f;
        float axis = 0f;

        // LEFT half = a move stick (one finger). RIGHT half = jump (any finger
        // that touches down there). These run INDEPENDENTLY so you can slide to
        // move with one finger AND tap to jump with another at the same time.
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    // Ignore taps in the TOP HUD STRIP (timer/lives on the left,
                    // MENU button on the right). Gameplay taps happen below it,
                    // so excluding this band lets the in-play MENU button work
                    // without a jump firing underneath — deterministic, unlike
                    // IsPointerOverGameObject which is unreliable on WebGL. We
                    // still also honor the EventSystem check as a backup.
                    float topStrip = Screen.height - HudStripPx * dpiScale;
                    if (touch.position.y > topStrip) break;
                    if (EventSystem.current != null &&
                        EventSystem.current.IsPointerOverGameObject(touch.fingerId))
                        break;

                    if (touch.position.x < halfWidth)
                    {
                        // claim the move finger only if we don't have one yet
                        if (moveFingerId == -1)
                        {
                            moveFingerId = touch.fingerId;
                            moveAnchorX = touch.position.x;
                        }
                    }
                    else
                    {
                        // right-half tap = jump, regardless of the move finger
                        MobileInput.QueueJump();
                    }
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    if (touch.fingerId == moveFingerId) moveFingerId = -1;
                    break;
            }

            if (touch.fingerId == moveFingerId &&
                (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary))
            {
                float offset = touch.position.x - moveAnchorX;
                float deadZone = DeadZoneDp * dpiScale;
                if (offset > deadZone) axis = 1f;
                else if (offset < -deadZone) axis = -1f;

                // Ratchet: reversing direction shouldn't require dragging all
                // the way back past the original anchor.
                float maxLead = deadZone * 2f;
                if (offset > maxLead) moveAnchorX = touch.position.x - maxLead;
                else if (offset < -maxLead) moveAnchorX = touch.position.x + maxLead;
            }
        }

        MobileInput.LeftHeld = axis < 0f;
        MobileInput.RightHeld = axis > 0f;
    }

    private IEnumerator ShowHint(string hintText)
    {
        var go = new GameObject("GestureHint", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 70f);
        rect.sizeDelta = new Vector2(1400f, 60f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = hintText;
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
