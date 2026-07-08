using System;

/// <summary>
/// DTOs for the deployable stage JSON (schema v0.2). Kept flat and typed so
/// Unity's JsonUtility can parse them without external dependencies.
/// See docs/parts-catalog.md for the part vocabulary.
/// </summary>
[Serializable]
public class StageData
{
    public string schemaVersion;
    public string id;
    public string name;
    public float timeLimit;
    public Vec2 playerStart;
    public RectData goal;
    public PartData[] parts;
}

[Serializable]
public class Vec2
{
    public float x;
    public float y;
}

[Serializable]
public class RectData
{
    public float x;
    public float y;
    public float w;
    public float h;
}

[Serializable]
public class PartData
{
    public string type;   // solid | hazard | jumpPad | boost | gravityFlip
    public float x;
    public float y;
    public float w;
    public float h;
    public float dirX;    // boost: +1 (right) or -1 (left)
    public float power;   // jumpPad: launch speed; boost: horizontal speed
}
