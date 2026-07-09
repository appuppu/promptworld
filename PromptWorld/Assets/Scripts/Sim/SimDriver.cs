using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Runs the deterministic sim as the actual gameplay: feeds it player input
/// each fixed tick, records the input trace (the future replay certificate),
/// and syncs the white-square views to sim state.
/// </summary>
public class SimDriver : MonoBehaviour
{
    private SimWorld world;
    private Transform playerView;
    private Transform[] moverViews;
    private SpriteRenderer[] crumbleViews;
    private GameManager gameManager;

    private readonly List<int> trace = new List<int>();
    private bool jumpLatch;
    private bool running;
    private Vector3 prevPos;
    private Vector3 curPos;
    private float lastStepTime;

    private SimWorld ghostWorld;
    private int[] ghostCodes;
    private int ghostTick;
    private Transform ghostView;
    private int deaths;

    /// <summary>Plays the creator's verified replay alongside the live run.</summary>
    public void AttachGhost(SimWorld world, int[] rle, Transform view)
    {
        ghostWorld = world;
        ghostView = view;
        ghostTick = 0;

        var codes = new List<int>();
        for (int i = 0; i + 1 < rle.Length; i += 2)
        {
            int code = rle[i];
            int count = rle[i + 1];
            for (int n = 0; n < count; n++)
            {
                // jump is an edge: only the first tick of a run carries the press
                codes.Add(n == 0 ? code : code & ~4);
            }
        }
        ghostCodes = codes.ToArray();
    }

    public void Init(SimWorld simWorld, Transform player, Transform[] movers,
        SpriteRenderer[] crumbles, GameManager gm)
    {
        world = simWorld;
        playerView = player;
        moverViews = movers;
        crumbleViews = crumbles;
        gameManager = gm;
        curPos = new Vector3((float)world.Px, (float)world.Py, 0f);
        prevPos = curPos;
        lastStepTime = Time.time;
        running = true;
    }

    private void Update()
    {
        if (!running) return;

        if (Input.GetKeyDown(KeyCode.Space) || MobileInput.ConsumeJump())
        {
            jumpLatch = true;
        }

        // Interpolate the 50 Hz sim for smooth rendering.
        float alpha = Mathf.Clamp01((Time.time - lastStepTime) / Time.fixedDeltaTime);
        playerView.position = Vector3.Lerp(prevPos, curPos, alpha);
    }

    private void FixedUpdate()
    {
        if (!running) return;

        float axis = Mathf.Clamp(Input.GetAxisRaw("Horizontal") + MobileInput.Axis, -1f, 1f);
        var input = new SimInput
        {
            Left = axis < -0.25f,
            Right = axis > 0.25f,
            Jump = jumpLatch,
        };
        jumpLatch = false;

        trace.Add(input.Encode());
        SimEvents ev = world.Step(input);
        StepGhost();

        prevPos = curPos;
        curPos = new Vector3((float)world.Px, (float)world.Py, 0f);
        lastStepTime = Time.time;

        SyncViews();
        PlayEventSounds(ev);
        if ((ev & SimEvents.Respawned) != 0) deaths++;
        gameManager.OnSimTick(world.MaxTicks - world.TickCount);

        if ((ev & SimEvents.Cleared) != 0)
        {
            running = false;
            playerView.position = curPos;
            gameManager.StageClearFromSim(world.TickCount * 20, EncodeTrace(), deaths);
        }
        else if ((ev & SimEvents.TimedOut) != 0)
        {
            running = false;
            gameManager.GameOverFromSim(deaths);
        }
    }

    private void StepGhost()
    {
        if (ghostWorld == null || ghostView == null) return;
        if (ghostTick >= ghostCodes.Length || ghostWorld.ClearedFlag)
        {
            ghostView.gameObject.SetActive(false);
            return;
        }
        SimInput input = SimInput.Decode(ghostCodes[ghostTick]);
        ghostTick++;
        ghostWorld.Step(input);
        ghostView.position = new Vector3((float)ghostWorld.Px, (float)ghostWorld.Py, 0f);
    }

    private void SyncViews()
    {
        for (int i = 0; i < moverViews.Length; i++)
        {
            SimMover m = world.Movers[i];
            moverViews[i].position = new Vector3((float)m.X, (float)m.Y, 0f);
        }
        for (int i = 0; i < crumbleViews.Length; i++)
        {
            SimCrumble c = world.Crumbles[i];
            bool intact = c.TouchedTick < 0;
            bool vanished = !intact && world.TickCount >= c.TouchedTick + SimWorld.CrumbleDelayTicks;
            bool blinkOff = !intact && !vanished && (world.TickCount / 4) % 2 == 1;
            crumbleViews[i].enabled = !vanished && !blinkOff;
        }
    }

    private void PlayEventSounds(SimEvents ev)
    {
        if ((ev & SimEvents.Jumped) != 0) Sfx.Play(SfxId.Jump);
        if ((ev & SimEvents.Bounced) != 0) Sfx.Play(SfxId.Pad);
        if ((ev & SimEvents.Boosted) != 0) Sfx.Play(SfxId.Boost);
        if ((ev & SimEvents.Flipped) != 0) Sfx.Play(SfxId.Flip);
        if ((ev & SimEvents.Respawned) != 0) Sfx.Play(SfxId.Respawn);
    }

    /// <summary>
    /// RLE-encodes the trace as the replay certificate. Ticks carrying a jump
    /// press are never merged so the edge semantics survive round-trips.
    /// </summary>
    private string EncodeTrace()
    {
        var sb = new StringBuilder();
        sb.Append("{\"v\":1,\"ticks\":").Append(trace.Count).Append(",\"rle\":[");
        bool first = true;
        int i = 0;
        while (i < trace.Count)
        {
            int code = trace[i];
            int count = 1;
            if ((code & 4) == 0)
            {
                while (i + count < trace.Count && trace[i + count] == code) count++;
            }
            if (!first) sb.Append(',');
            sb.Append(code).Append(',').Append(count);
            first = false;
            i += count;
        }
        sb.Append("]}");
        return sb.ToString();
    }
}
