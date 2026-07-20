using UnityEngine;

/// <summary>
/// Smoothly tracks the player with a fixed orthographic zoom. Zoom is NOT
/// interactive — pinch/scroll zoom was removed because (a) it caused motion
/// sickness and (b) the pinch gesture leaked into the jump/move input. The view
/// size is set per stage (StageData.zoom) so creators frame their own course.
/// A deadzone keeps small hops from shaking the view.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    private const float MinZoom = 4f;
    private const float MaxZoom = 20f; // headroom for the mobile widen factor
    private const float DefaultZoom = 7f;

    [SerializeField] private Transform target;
    [SerializeField] private float smoothTime = 0.22f;

    private Camera cam;
    private Vector3 velocity;
    private Vector3 anchor;
    private bool anchorInit;

    // Deadzone half-extents (world units) around the anchor.
    private const float DeadzoneX = 1.6f;
    private const float DeadzoneY = 2.6f;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        cam.orthographicSize = DefaultZoom;
    }

    /// <summary>Set the fixed view size for this stage (clamped to sane bounds).</summary>
    public void SetZoom(float zoom)
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (zoom <= 0f) zoom = DefaultZoom;
        // Phones have small screens AND a tall-ish aspect, so the same ortho size
        // shows much less of the stage. Pull the view back so mobile players see
        // enough of what's around them (never zoom IN past the stage's value).
        if (IsHandheld())
        {
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.6f;
            float widen = 1.35f;
            if (aspect < 1.6f) widen += (1.6f - aspect) * 0.8f; // narrower screen -> pull back more
            zoom *= widen;
        }
        cam.orthographicSize = Mathf.Clamp(zoom, MinZoom, MaxZoom);
    }

    /// <summary>The camera's current half-height in world units (for framing the backdrop).</summary>
    public float ViewSize
    {
        get { if (cam == null) cam = GetComponent<Camera>(); return cam.orthographicSize; }
    }

    public Transform CameraTransform { get { return transform; } }

    private static bool IsHandheld()
    {
#if UNITY_IOS || UNITY_ANDROID
        return true;
#elif UNITY_WEBGL && !UNITY_EDITOR
        return WebBridge.IsMobileBrowser();
#else
        return false;
#endif
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        anchor = TargetPosition();
        anchorInit = true;
        transform.position = anchor;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 t = TargetPosition();
        if (!anchorInit) { anchor = t; anchorInit = true; }

        float dx = t.x - anchor.x;
        if (dx > DeadzoneX) anchor.x = t.x - DeadzoneX;
        else if (dx < -DeadzoneX) anchor.x = t.x + DeadzoneX;
        float dy = t.y - anchor.y;
        if (dy > DeadzoneY) anchor.y = t.y - DeadzoneY;
        else if (dy < -DeadzoneY) anchor.y = t.y + DeadzoneY;
        anchor.z = -10f;

        transform.position = Vector3.SmoothDamp(transform.position, anchor, ref velocity, smoothTime);
    }

    private Vector3 TargetPosition()
    {
        return new Vector3(target.position.x, target.position.y, -10f);
    }
}
