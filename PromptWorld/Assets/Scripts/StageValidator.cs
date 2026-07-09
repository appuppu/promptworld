using System.Collections.Generic;

/// <summary>
/// Guard rails for user-generated stages. Runs client-side on load today;
/// the exact same rules will run server-side before a deploy is accepted.
/// Limits are documented in docs/parts-catalog.md.
/// </summary>
public static class StageValidator
{
    public const float MinTimeLimit = 5f;
    public const float MaxTimeLimit = 1800f; // hard cap — no 30-minute-plus stages
    public const int MaxParts = 300;
    public const float MaxCoord = 500f;
    public const float MinSize = 0.05f;
    public const float MaxSize = 100f;
    public const float MaxPower = 60f;
    public const float MinPeriod = 0.5f;
    public const float MaxPeriod = 30f;

    private static readonly HashSet<string> KnownTypes = new HashSet<string>
    {
        "solid", "hazard", "jumpPad", "boost", "gravityFlip", "movingPlatform", "crumble",
        "faller", "conveyor", "timedGate", "key", "door",
    };

    private static readonly HashSet<string> SupportedVersions = new HashSet<string> { "0.2", "0.3" };

    public static List<string> Validate(StageData data)
    {
        var errors = new List<string>();
        if (data == null)
        {
            errors.Add("Stage JSON could not be parsed.");
            return errors;
        }

        if (!SupportedVersions.Contains(data.schemaVersion))
            errors.Add($"Unsupported schemaVersion '{data.schemaVersion}'.");

        if (data.timeLimit < MinTimeLimit || data.timeLimit > MaxTimeLimit)
            errors.Add($"timeLimit {data.timeLimit}s is outside [{MinTimeLimit}, {MaxTimeLimit}] seconds.");

        if (data.playerStart == null)
            errors.Add("playerStart is required.");
        else if (!InWorld(data.playerStart.x, data.playerStart.y))
            errors.Add("playerStart is outside the world bounds.");

        if (data.goal == null)
            errors.Add("goal is required.");
        else
        {
            if (!InWorld(data.goal.x, data.goal.y)) errors.Add("goal is outside the world bounds.");
            if (!SizeOk(data.goal.w, data.goal.h)) errors.Add("goal size is out of range.");
        }

        if (data.parts == null || data.parts.Length == 0)
        {
            errors.Add("At least one part is required.");
            return errors;
        }
        if (data.parts.Length > MaxParts)
            errors.Add($"{data.parts.Length} parts exceeds the maximum of {MaxParts}.");

        for (int i = 0; i < data.parts.Length; i++)
        {
            PartData p = data.parts[i];
            string label = $"parts[{i}] ({p.type})";

            if (!KnownTypes.Contains(p.type))
            {
                errors.Add($"{label}: unknown type.");
                continue;
            }
            if (!InWorld(p.x, p.y)) errors.Add($"{label}: outside the world bounds.");
            if (!SizeOk(p.w, p.h)) errors.Add($"{label}: size out of range.");

            if ((p.type == "jumpPad" || p.type == "boost" || p.type == "conveyor") && (p.power < 0f || p.power > MaxPower))
                errors.Add($"{label}: power {p.power} exceeds the maximum of {MaxPower}.");

            if ((p.type == "movingPlatform" || p.type == "timedGate") && p.period != 0f && (p.period < MinPeriod || p.period > MaxPeriod))
                errors.Add($"{label}: period must be within [{MinPeriod}, {MaxPeriod}] seconds.");

            if (p.type == "faller" && p.dy != 0f && (p.dy < 0.5f || p.dy > 50f))
                errors.Add($"{label}: dy (fall distance) must be within [0.5, 50].");
        }

        return errors;
    }

    private static bool InWorld(float x, float y)
    {
        return x >= -MaxCoord && x <= MaxCoord && y >= -MaxCoord && y <= MaxCoord;
    }

    private static bool SizeOk(float w, float h)
    {
        return w >= MinSize && w <= MaxSize && h >= MinSize && h <= MaxSize;
    }
}
