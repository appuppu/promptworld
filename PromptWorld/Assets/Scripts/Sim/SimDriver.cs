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
    private Transform eyes;
    private int facing = 1;           // +1 right, -1 left
    private float eyeShift;           // smoothed eye offset
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

    private Transform[] fallerViews = new Transform[0];
    private GameObject[] gateViews = new GameObject[0];
    private GameObject[] keyViews = new GameObject[0];
    private GameObject[] doorViews = new GameObject[0];
    private GameObject[] bulletViews = new GameObject[0];
    private GravitySetIndicator[] gravSetViews = new GravitySetIndicator[0];
    private Transform[] rotorHeadViews = new Transform[0];
    private Transform[] waveViews = new Transform[0];
    // Render the head on a TRUE circle from a continuously-advancing angle
    // (client-only trig — the sim's collision still uses its 24-step table, so
    // determinism is untouched). This removes the 24-gon "cornering" the old
    // step-to-step lerp left behind.
    private double[] rotorCenterX = new double[0];
    private double[] rotorCenterY = new double[0];
    private double[] rotorRadius = new double[0];
    private double[] rotorSpin = new double[0];
    private int[] rotorPeriodTicks = new int[0];
    private int[] rotorPhaseTicks = new int[0];
    private GameObject[] switchGateViews = new GameObject[0];
    private Vector3[] switchGateBaseScale = new Vector3[0];
    private GameObject[] switchViews = new GameObject[0];
    private Transform[] switchNubs = new Transform[0];
    private float[] switchNubRest = new float[0];
    private float[] switchDepress = new float[0];
    private GameObject[] enemyViews = new GameObject[0];
    private Vector3[] enemyBaseScale = new Vector3[0];
    private GameObject[] bossDoorViews = new GameObject[0];
    private Sprite whiteSprite;

    public void SetEyes(Transform eyesPivot)
    {
        eyes = eyesPivot;
    }

    public void SetGravitySetViews(GravitySetIndicator[] views)
    {
        gravSetViews = views;
    }

    public void SetWaveViews(Transform[] waves)
    {
        waveViews = waves;
    }

    public void SetGimmickViews(Transform[] rotorHeads, GameObject[] switchGates, GameObject[] switches)
    {
        rotorHeadViews = rotorHeads;
        switchGateViews = switchGates;
        switchViews = switches;
        int rn = rotorHeads.Length;
        rotorCenterX = new double[rn];
        rotorCenterY = new double[rn];
        rotorRadius = new double[rn];
        rotorSpin = new double[rn];
        rotorPeriodTicks = new int[rn];
        rotorPhaseTicks = new int[rn];
        for (int i = 0; i < rn && i < world.Rotors.Count; i++)
        {
            SimRotor r = world.Rotors[i];
            rotorCenterX[i] = r.X;
            rotorCenterY[i] = r.Y;
            rotorRadius[i] = r.Radius;
            rotorSpin[i] = r.SpinDir;
            rotorPeriodTicks[i] = r.PeriodTicks;
            rotorPhaseTicks[i] = r.PhaseTicks;
        }
        // Remember each switch gate's authored scale so we can shrink/grow its
        // HEIGHT smoothly as it opens/closes (retract into the ground).
        switchGateBaseScale = new Vector3[switchGates.Length];
        for (int i = 0; i < switchGates.Length; i++)
        {
            switchGateBaseScale[i] = switchGates[i].transform.localScale;
        }
        // Cache each switch's raised nub so we can sink it when pressed.
        switchNubs = new Transform[switches.Length];
        switchNubRest = new float[switches.Length];
        switchDepress = new float[switches.Length];
        for (int i = 0; i < switches.Length; i++)
        {
            Transform nub = switches[i].transform.Find("Nub");
            switchNubs[i] = nub;
            switchNubRest[i] = nub != null ? nub.localPosition.y : 0f;
        }
    }

    private GameObject[] fireballViews = new GameObject[0];

    public void SetEnemyViews(GameObject[] enemies, GameObject[] bossDoors)
    {
        enemyViews = enemies;
        bossDoorViews = bossDoors;
        enemyBaseScale = new Vector3[enemies.Length];
        for (int i = 0; i < enemies.Length; i++)
        {
            enemyBaseScale[i] = enemies[i].transform.localScale;
        }
        // One reusable fireball per enemy slot (only bosses ever light theirs).
        fireballViews = new GameObject[world.Enemies.Count];
        for (int i = 0; i < fireballViews.Length; i++)
        {
            var f = new GameObject("Fireball");
            f.transform.SetParent(transform, false);
            f.transform.localScale = new Vector3(0.56f, 0.56f, 1f);
            var sr = f.AddComponent<SpriteRenderer>();
            sr.sprite = whiteSprite;
            sr.color = Color.white;
            f.transform.localRotation = Quaternion.Euler(0f, 0f, 45f); // a spiky mote
            f.SetActive(false);
            fireballViews[i] = f;
        }
    }

    public void SetPartViews(Transform[] fallers, GameObject[] gates, GameObject[] keys, GameObject[] doors, Sprite white)
    {
        fallerViews = fallers;
        gateViews = gates;
        keyViews = keys;
        doorViews = doors;
        whiteSprite = white;

        // One reusable bullet square per cannon (deterministic: at most one
        // live bullet per cannon at a time).
        bulletViews = new GameObject[world.Cannons.Count];
        for (int i = 0; i < bulletViews.Length; i++)
        {
            var b = new GameObject("Bullet");
            b.transform.SetParent(transform, false);
            b.transform.localScale = new Vector3(0.44f, 0.44f, 1f);
            var sr = b.AddComponent<SpriteRenderer>();
            sr.sprite = white;
            sr.color = Color.white;
            b.SetActive(false);
            bulletViews[i] = b;
        }
    }

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

        // Rotating-hazard heads ride a TRUE circle at a continuously-advancing
        // angle, so the motion is perfectly smooth (no 24-gon cornering). This
        // is render-only trig; the sim's kill test uses its own stepped head.
        for (int i = 0; i < rotorHeadViews.Length; i++)
        {
            if (rotorPeriodTicks[i] <= 0) continue;
            double contTick = world.TickCount + alpha + rotorPhaseTicks[i];
            double rev = contTick / rotorPeriodTicks[i];        // revolutions
            double frac = rev - System.Math.Floor(rev);         // 0..1 around circle
            if (rotorSpin[i] < 0.0) frac = 1.0 - frac;          // reverse spin
            double theta = frac * (System.Math.PI * 2.0);
            double ox = System.Math.Cos(theta) * rotorRadius[i];
            double oy = System.Math.Sin(theta) * rotorRadius[i];
            rotorHeadViews[i].position = new Vector3(
                (float)(rotorCenterX[i] + ox), (float)(rotorCenterY[i] + oy), 0f);
        }

        // Eyes glance toward the facing direction so you can tell which way the
        // square is looking. Smoothed so the flip isn't a hard snap.
        if (eyes != null)
        {
            float target = facing * 0.16f;
            eyeShift = Mathf.MoveTowards(eyeShift, target, 1.6f * Time.deltaTime);
            Vector3 lp = eyes.localPosition;
            lp.x = eyeShift;
            eyes.localPosition = lp;
        }
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

        if (input.Right) facing = 1;
        else if (input.Left) facing = -1;

        trace.Add(input.Encode());
        SimEvents ev = world.Step(input);
        StepGhost();

        prevPos = curPos;
        curPos = new Vector3((float)world.Px, (float)world.Py, 0f);
        lastStepTime = Time.time;

        SyncViews();
        UpdateBossMusic();
        PlayEventSounds(ev);
        if ((ev & SimEvents.Respawned) != 0)
        {
            deaths++;
            // Shatter the square where it was destroyed (prevPos = pre-respawn).
            if (whiteSprite != null) ShatterEffect.Burst(prevPos, whiteSprite);
        }
        gameManager.OnSimTick(world.MaxTicks - world.TickCount);
        gameManager.UpdateLives(world.LivesLeft, world.MaxLives);

        if ((ev & SimEvents.Cleared) != 0)
        {
            running = false;
            playerView.position = curPos;
            gameManager.StageClearFromSim(world.TickCount * 20, EncodeTrace(), deaths);
        }
        else if ((ev & SimEvents.TimedOut) != 0)
        {
            running = false;
            gameManager.GameOverFromSim(deaths, world.LivesOut);
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
        // Waves follow the sim's live sweep position each frame.
        for (int i = 0; i < waveViews.Length && i < world.Waves.Count; i++)
        {
            SimWave wv = world.Waves[i];
            waveViews[i].position = new Vector3((float)wv.CurX, (float)wv.CurY, 0f);
        }
        for (int i = 0; i < crumbleViews.Length; i++)
        {
            SimCrumble c = world.Crumbles[i];
            bool intact = c.TouchedTick < 0;
            bool vanished = !intact && world.TickCount >= c.TouchedTick + SimWorld.CrumbleDelayTicks;
            bool blinkOff = !intact && !vanished && (world.TickCount / 4) % 2 == 1;
            crumbleViews[i].enabled = !vanished && !blinkOff;
        }
        for (int i = 0; i < fallerViews.Length; i++)
        {
            SimFaller f = world.Fallers[i];
            // Shudder horizontally during the telegraph so the slam reads clearly.
            float shake = f.State == 4 ? Mathf.Sin(world.TickCount * 2.2f) * 0.12f : 0f;
            fallerViews[i].position = new Vector3((float)f.X + shake, (float)f.Y, 0f);
        }
        for (int i = 0; i < gateViews.Length; i++)
        {
            gateViews[i].SetActive(world.GateActive(world.Gates[i]));
        }
        for (int i = 0; i < keyViews.Length; i++)
        {
            keyViews[i].SetActive(!world.Keys[i].Collected);
        }
        bool doorsOpen = world.DoorsOpen();
        for (int i = 0; i < doorViews.Length; i++)
        {
            doorViews[i].SetActive(!doorsOpen);
        }
        for (int i = 0; i < bulletViews.Length; i++)
        {
            SimCannon c = world.Cannons[i];
            if (c.BulletActive)
            {
                bulletViews[i].SetActive(true);
                bulletViews[i].transform.position = new Vector3((float)c.BulletX, (float)c.BulletY, 0f);
            }
            else
            {
                bulletViews[i].SetActive(false);
            }
        }

        gameManager.UpdateKeys(world.KeysCollected, world.Keys.Count);

        // (Rotating-hazard heads are positioned in Update() on a true circle
        // from a continuous angle, independent of the 24-step sim table.)

        // Switch gates: the sim keeps each gate's LIVE box (Y/HalfH) in sync with
        // its retract state — it's a real floor whose height shrinks from the top
        // down. Mirror that box directly so the visual matches the collision
        // exactly (you can stand on the shrinking top).
        for (int i = 0; i < switchGateViews.Length && i < world.SwitchGates.Count; i++)
        {
            SimSwitchGate sg = world.SwitchGates[i];
            var go = switchGateViews[i];
            Vector3 baseScale = switchGateBaseScale[i];
            float h = (float)(sg.HalfH + sg.HalfH);
            go.transform.localScale = new Vector3(baseScale.x, Mathf.Max(h, 0.0001f), baseScale.z);
            go.transform.position = new Vector3((float)sg.X, (float)sg.Y, 0f);
            go.SetActive(sg.HalfH > 0.02);
        }

        // Switches: sink the nub (and blend it toward white) while the player is
        // standing on the plate, so pressing it clearly registers. Read-only
        // overlap test against the player — mirrors the sim's press check.
        for (int i = 0; i < switchViews.Length && i < world.Switches.Count; i++)
        {
            SimSwitch sw = world.Switches[i];
            bool pressed =
                System.Math.Abs(world.Px - sw.X) < (0.5 + sw.HalfW) &&
                System.Math.Abs(world.Py - sw.Y) < (0.5 + sw.HalfH);
            float target = pressed ? 1f : 0f;
            switchDepress[i] = Mathf.MoveTowards(switchDepress[i], target, 8f * Time.deltaTime);
            Transform nub = switchNubs[i];
            if (nub != null)
            {
                Vector3 lp = nub.localPosition;
                // Sink the nub down into the plate as it's pressed.
                lp.y = switchNubRest[i] - switchDepress[i] * 0.42f;
                nub.localPosition = lp;
                var sr = nub.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = Color.Lerp(Color.black, new Color(0.55f, 0.55f, 0.55f, 1f), switchDepress[i]);
            }
        }

        // Enemies: follow their patrol, glance toward travel, flash + shrink for a
        // few ticks after a stomp, and vanish (with a last shrink) when defeated.
        for (int i = 0; i < enemyViews.Length && i < world.Enemies.Count; i++)
        {
            SimEnemy en = world.Enemies[i];
            var go = enemyViews[i];
            if (en.Dead)
            {
                go.SetActive(false);
                continue;
            }
            go.transform.position = new Vector3((float)en.X, (float)en.Y, 0f);
            // Hit reaction: brief squash + a flash to white-on-black invert feel.
            int since = world.TickCount - en.HitTick;
            float react = 0f;
            if (since >= 0 && since < SimWorld.EnemyHitFlashTicks)
            {
                react = 1f - (float)since / SimWorld.EnemyHitFlashTicks;
            }
            Vector3 bs = enemyBaseScale[i];
            float squash = 1f - 0.25f * react;
            go.transform.localScale = new Vector3(bs.x * (1f + 0.18f * react), bs.y * squash, bs.z);
            // Glance pupils toward facing.
            var face = go.transform.Find("Face");
            if (face != null)
            {
                foreach (Transform child in face)
                {
                    if (child.name == "Pupil")
                    {
                        Vector3 lp = child.localPosition;
                        float baseX = lp.x >= 0f ? 0.24f : -0.24f;
                        lp.x = baseX + en.Facing * 0.05f;
                        child.localPosition = lp;
                    }
                }
                // Flash the whole face renderer set on hit (blink the body).
                var body = go.GetComponent<SpriteRenderer>();
                if (body != null) body.color = react > 0.5f ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
            }
        }

        // Boss fireballs: light the mote where a boss's breath currently is.
        for (int i = 0; i < fireballViews.Length && i < world.Enemies.Count; i++)
        {
            SimEnemy en = world.Enemies[i];
            bool lit = en.IsBoss && !en.Dead && en.FireActive;
            fireballViews[i].SetActive(lit);
            if (lit)
            {
                fireballViews[i].transform.position = new Vector3((float)en.FireX, (float)en.FireY, 0f);
            }
        }

        // Boss doors: solid (shown) until every boss is defeated, then gone.
        bool bossOpen = world.BossDoorsOpen();
        for (int i = 0; i < bossDoorViews.Length; i++)
        {
            bossDoorViews[i].SetActive(!bossOpen);
        }

        // Every gravitySet arrow reflects the one shared world gravity so the
        // player can read the current direction across the whole course.
        if (gravSetViews.Length > 0)
        {
            int g = world.GravityDir < 0.0 ? -1 : 1;
            for (int i = 0; i < gravSetViews.Length; i++)
            {
                if (gravSetViews[i] != null) gravSetViews[i].SetGravity(g);
            }
        }
    }

    private bool bossMusicActive;

    /// <summary>Kick the music into its intense BOSS variant while any boss is
    /// alive, and drop back to the calm loop once every boss is beaten. We
    /// re-assert the desired track every tick (the Sfx calls are cheap no-ops
    /// once it's already playing) so that if the browser blocked audio at stage
    /// start — WebGL only starts audio after the first user gesture — the boss
    /// loop still kicks in the moment the player moves, instead of being stuck on
    /// a Play() that was silently refused.</summary>
    private void UpdateBossMusic()
    {
        bool liveBoss = false;
        for (int i = 0; i < world.Enemies.Count; i++)
        {
            SimEnemy en = world.Enemies[i];
            if (en.IsBoss && !en.Dead) { liveBoss = true; break; }
        }
        if (liveBoss)
        {
            bossMusicActive = true;
            Sfx.StartBossMusic();
        }
        else if (bossMusicActive)
        {
            bossMusicActive = false;
            Sfx.StartMusic(); // all bosses down — back to the calm loop
        }
    }

    private void PlayEventSounds(SimEvents ev)
    {
        if ((ev & SimEvents.Jumped) != 0) Sfx.Play(SfxId.Jump);
        if ((ev & SimEvents.Bounced) != 0) Sfx.Play(SfxId.Pad);
        if ((ev & SimEvents.Boosted) != 0) Sfx.Play(SfxId.Boost);
        if ((ev & SimEvents.Flipped) != 0) Sfx.Play(SfxId.Flip);
        if ((ev & SimEvents.Respawned) != 0) Sfx.Play(SfxId.Respawn);
        if ((ev & SimEvents.Slammed) != 0) Sfx.Play(SfxId.Boost, 0.7f);
        if ((ev & SimEvents.KeyPickup) != 0) Sfx.Play(SfxId.Tick, 0.9f);
        if ((ev & SimEvents.DoorOpened) != 0) Sfx.Play(SfxId.Flip, 0.8f);
        if ((ev & SimEvents.Stomped) != 0) Sfx.Play(SfxId.Pad, 0.85f);
        if ((ev & SimEvents.EnemyDown) != 0) Sfx.Play(SfxId.Boost, 0.9f);
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
