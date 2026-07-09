using System.Collections;
using TMPro;
using UnityEngine;

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
        float halfWidth = Screen.width / 2f;
        float axis = 0f;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    if (touch.position.x < halfWidth)
                    {
                        if (moveFingerId == -1)
                        {
                            moveFingerId = touch.fingerId;
                            moveAnchorX = touch.position.x;
                        }
                    }
                    else
                    {
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
