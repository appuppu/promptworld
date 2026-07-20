using System;

/// <summary>
/// DTOs for the deployable stage JSON (schema v0.3). Kept flat and typed so
/// Unity's JsonUtility can parse them without external dependencies.
/// See docs/parts-catalog.md for the part vocabulary and limits.
/// </summary>
[Serializable]
public class StageData
{
    public string schemaVersion;
    public string id;
    public string name;
    public float timeLimit;
    public int lives;          // optional; 0/absent = default (5)
    public float zoom;         // optional camera view size; 0/absent = default (7)
    public BgData bg;          // optional 1-bit dithered backdrop (pure decoration)
    public MusicData music;    // optional per-stage BGM recipe (pure decoration, synthesized)
    public bool hideGhost;     // optional; true = don't show the creator's ghost/par (for trick/blind stages)
    public Vec2 playerStart;
    public RectData goal;
    public PartData[] parts;
}

/// <summary>
/// An optional per-stage music recipe. NOT audio data — a tiny set of parameters
/// (tempo, key/scale, drum pattern, bass + lead note rows) that the game
/// synthesizes into a 4-bar loop at runtime. Purely decorative: it never touches
/// the deterministic sim. Absent music = the default drum groove (unchanged).
/// Note values are SCALE DEGREES (0 = the key's root, 7 = root one octave up,
/// negative = below the root); -99 means "rest" (silence for that step).
/// </summary>
[Serializable]
public class MusicData
{
    public float bpm;       // 60..180; 0/absent -> 100
    public string key;      // root note: C C# D D# E F F# G G# A A# B; absent -> A
    public string scale;    // major | minor | pentatonic | japanese | phrygian
    public string drums;    // none | basic | fourFloor | sparse | busy
    public int[] bass;      // one degree per beat (up to 16 = 4 bars); -99 = rest
    public LeadData lead;    // optional melodic line
    public ChordData chords; // optional harmony: one chord per beat
}

[Serializable]
public class LeadData
{
    public string voice;    // square | saw | sine | bell | pad | koto | flute
    public int[] notes;     // one degree per beat (up to 16 = 4 bars); -99 = rest
}

/// <summary>A chord progression: one chord per beat (up to 16 = 4 bars). Each
/// chord is a set of scale degrees played simultaneously. Purely decorative.
/// (prog is an array of {notes} wrappers because Unity's JsonUtility can't parse
/// a raw array-of-arrays.)</summary>
[Serializable]
public class ChordData
{
    public string voice;      // square | saw | sine | bell | pad | koto | flute
    public string preset;     // optional: a named famous progression (see Sfx.ChordPreset)
    public ChordStep[] prog;  // explicit chords; overrides preset when present
}

[Serializable]
public class ChordStep
{
    public int[] notes;       // scale degrees sounded together (up to 4); [] = rest
}

/// <summary>
/// An optional decorative backdrop: a 1-bit (black/white) image, w x h pixels,
/// stored as base64 of a run-length stream (first byte = starting colour, then
/// varint run lengths alternating colour). Purely visual — never touches the sim.
/// </summary>
[Serializable]
public class BgData
{
    public int w;
    public int h;
    public string data;    // base64 of the RLE varint stream
    public bool invert;    // optional: swap black/white
}

[Serializable]
public class StageIndex
{
    public StageEntry[] stages;
}

[Serializable]
public class StageEntry
{
    public string file;
    public string title;
}

[Serializable]
public class GhostData
{
    public int clearTimeMs;
    public int bestTimeMs;
    public ReplayData replay;
}

[Serializable]
public class ReplayData
{
    public int v;
    public int ticks;
    public int[] rle;
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
    public string type;   // solid | hazard | jumpPad | boost | gravityFlip | movingPlatform | crumble
    public float x;
    public float y;
    public float w;
    public float h;
    public float dirX;    // boost: +1 (right) or -1 (left)
    public float power;   // jumpPad: launch speed; boost: horizontal speed
    public float dx;      // movingPlatform: travel offset x
    public float dy;      // movingPlatform: travel offset y
    public float period;  // movingPlatform: seconds per round trip
}
