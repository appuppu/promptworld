using UnityEngine;

/// <summary>
/// Visual for a gravitySet block: an arrow showing the FIXED gravity direction
/// this block sets when touched (+1 down, -1 up). The arrow lights up brightly
/// when the current world gravity already matches this block's direction, and
/// dims otherwise — so at a glance the player can read both "which way this one
/// points" and "which way gravity is right now". SimDriver pushes the current
/// gravity direction in each tick; every gravitySet block shares that one value.
/// </summary>
public class GravitySetIndicator : MonoBehaviour
{
    private int dir;                 // +1 down, -1 up
    private SpriteRenderer shaft;
    private SpriteRenderer head;

    private static readonly Color Active = Color.white;
    private static readonly Color Idle = new Color(1f, 1f, 1f, 0.32f);

    public void Init(int direction, SpriteRenderer shaftRenderer, SpriteRenderer headRenderer)
    {
        dir = direction;
        shaft = shaftRenderer;
        head = headRenderer;
        Apply(1); // default gravity is down at start
    }

    /// <summary>Called by SimDriver with the current world gravity direction.</summary>
    public void SetGravity(int currentGravityDir)
    {
        Apply(currentGravityDir);
    }

    private void Apply(int currentGravityDir)
    {
        Color c = currentGravityDir == dir ? Active : Idle;
        if (shaft != null) shaft.color = c;
        if (head != null) head.color = c;
    }
}
