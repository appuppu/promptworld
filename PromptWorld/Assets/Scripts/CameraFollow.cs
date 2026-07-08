using UnityEngine;

/// <summary>
/// Smoothly tracks the player, with user-controlled zoom:
/// mouse wheel on desktop, pinch on touch devices. The zoom level is
/// remembered across sessions.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    private const string ZoomPrefKey = "pw_zoom";
    private const float MinZoom = 4f;
    private const float MaxZoom = 11f;

    [SerializeField] private Transform target;
    [SerializeField] private float smoothTime = 0.15f;
    [SerializeField] private float zoom = 7f;

    private Camera cam;
    private Vector3 velocity;
    private float lastPinchDistance = -1f;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        zoom = Mathf.Clamp(PlayerPrefs.GetFloat(ZoomPrefKey, zoom), MinZoom, MaxZoom);
        cam.orthographicSize = zoom;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        transform.position = TargetPosition();
    }

    private void Update()
    {
        float before = zoom;

        // Desktop: scroll wheel.
        zoom -= Input.mouseScrollDelta.y * 0.5f;

        // Touch: pinch with two fingers.
        if (Input.touchCount == 2)
        {
            float distance = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);
            if (lastPinchDistance > 0f)
            {
                float dpiScale = Screen.dpi > 0f ? Screen.dpi / 160f : 2f;
                zoom -= (distance - lastPinchDistance) / (60f * dpiScale);
            }
            lastPinchDistance = distance;
        }
        else
        {
            lastPinchDistance = -1f;
        }

        zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        if (!Mathf.Approximately(before, zoom))
        {
            PlayerPrefs.SetFloat(ZoomPrefKey, zoom);
        }
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, zoom, 10f * Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (target == null) return;
        transform.position = Vector3.SmoothDamp(transform.position, TargetPosition(), ref velocity, smoothTime);
    }

    private Vector3 TargetPosition()
    {
        return new Vector3(target.position.x, target.position.y, -10f);
    }
}
