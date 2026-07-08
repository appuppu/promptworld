using UnityEngine;

/// <summary>Smoothly tracks the player so stages can be larger than one screen.</summary>
public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float smoothTime = 0.15f;

    private Vector3 velocity;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        transform.position = TargetPosition();
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
