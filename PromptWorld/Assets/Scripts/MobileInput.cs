using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Shared state written by on-screen touch buttons, read by PlayerController.</summary>
public static class MobileInput
{
    public static bool LeftHeld;
    public static bool RightHeld;

    private static bool jumpQueued;

    public static float Axis => (RightHeld ? 1f : 0f) - (LeftHeld ? 1f : 0f);

    public static void QueueJump() => jumpQueued = true;

    public static bool ConsumeJump()
    {
        bool queued = jumpQueued;
        jumpQueued = false;
        return queued;
    }
}

/// <summary>Press-and-hold button for touch controls.</summary>
public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public System.Action onDown;
    public System.Action onUp;

    public void OnPointerDown(PointerEventData eventData) => onDown?.Invoke();
    public void OnPointerUp(PointerEventData eventData) => onUp?.Invoke();
    public void OnPointerExit(PointerEventData eventData) => onUp?.Invoke();
}
