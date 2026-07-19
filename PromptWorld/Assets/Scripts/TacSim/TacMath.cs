// Prompt World TAC — deterministic math kernel, C# port of server/tacsim.js.
//
// PURE C#: no UnityEngine reference, so the exact same file compiles in the
// Unity client AND in the dotnet crosscheck CLI (scripts/tac-crosscheck).
// Porting contract (mirrors PromptSim.cs <-> sim.js):
//   - every statement mirrors tacsim.js line-for-line, same operation order
//   - all sim state is double; stage inputs quantized to float32 via Q()
//   - only + - * / comparisons and Math.Sqrt/Math.Floor; trig is the fixed
//     Taylor polynomial below; angles are integer units (65536 per turn)
// Any edit to tacsim.js math MUST be mirrored here; scripts/tac-crosscheck.sh
// proves bit-identity and gates the mobile build.

public static class TacMath
{
    public const double RAD_PER_UNIT = 9.587379924285257e-5; // 2*pi / 65536

    public static double Q(double v) { return (float)v; }

    public static double SinQ(int q)
    {
        int a = q & 65535;
        bool neg = false;
        if (a >= 32768) { a = a - 32768; neg = true; }
        if (a > 16384) { a = 32768 - a; }
        double x = a * RAD_PER_UNIT;
        double x2 = x * x;
        double t9 = x2 / 72.0;
        double p7 = 1.0 - t9;
        double t7 = x2 * p7;
        double t7b = t7 / 42.0;
        double p5 = 1.0 - t7b;
        double t5 = x2 * p5;
        double t5b = t5 / 20.0;
        double p3 = 1.0 - t5b;
        double t3 = x2 * p3;
        double t3b = t3 / 6.0;
        double p1 = 1.0 - t3b;
        double s = x * p1;
        if (neg) { return -s; }
        return s;
    }

    public static double CosQ(int q) { return SinQ(q + 16384); }

    public static int TurnToward(int cur, int target, int rate)
    {
        int diff = (target - cur) & 65535;
        if (diff > 32768) diff -= 65536;
        if (diff > rate) diff = rate;
        if (diff < -rate) diff = -rate;
        return (cur + diff) & 65535;
    }

    // integer yaw whose sin/cos best matches direction (dx,dz): coarse-to-fine
    // deterministic search on the quantized circle (no atan)
    public static int YawFor(double dx, double dz)
    {
        int best = 0;
        double bestDot = -2.0;
        int i, q;
        double s, c, len, dot;
        len = System.Math.Sqrt(dx * dx + dz * dz);
        if (len < 0.000001) return 0;
        double nx = dx / len;
        double nz = dz / len;
        for (i = 0; i < 64; i++)
        {
            q = i * 1024;
            s = SinQ(q);
            c = CosQ(q);
            dot = s * nx + c * nz;
            if (dot > bestDot) { bestDot = dot; best = q; }
        }
        int lo2 = best - 512;
        for (i = 0; i < 33; i++)
        {
            q = (lo2 + i * 32) & 65535;
            s = SinQ(q);
            c = CosQ(q);
            dot = s * nx + c * nz;
            if (dot > bestDot) { bestDot = dot; best = q; }
        }
        return best & 65535;
    }

    // first hit t in [0,1] of segment (x0,y0,z0)->(x1,y1,z1) against the upright
    // cylinder at (cx,cy,cz) radius cr height ch, or -1
    public static double SegCylinder(double x0, double y0, double z0, double x1, double y1, double z1, double cx, double cy, double cz, double cr, double ch)
    {
        double dx = x1 - x0;
        double dz = z1 - z0;
        double fx = x0 - cx;
        double fz = z0 - cz;
        double a = dx * dx + dz * dz;
        double b = 2.0 * (fx * dx + fz * dz);
        double c = fx * fx + fz * fz - cr * cr;
        // [tin, tout] = the parameter interval the ray spends inside the infinite
        // vertical cylinder (XZ circle). Then intersect with the height slab. The
        // old code only tested the wall-entry height, so a shot whose entry point
        // grazed above the head was rejected even when the body was hit further
        // along the ray — the "aimed dead-on but nothing dies" bug.
        double tin, tout;
        if (a < 0.0000001)
        {
            // ray is vertical (no XZ travel): inside the circle for all t iff origin is
            if (c > 0.0) return -1.0;
            tin = 0.0; tout = 1.0;
        }
        else
        {
            double disc = b * b - 4.0 * a * c;
            if (disc < 0.0) return -1.0;
            double sq = System.Math.Sqrt(disc);
            tin = (-b - sq) / (2.0 * a);
            tout = (-b + sq) / (2.0 * a);
        }
        if (tin < 0.0) tin = 0.0;
        if (tout > 1.0) tout = 1.0;
        if (tin > tout) return -1.0;   // circle span doesn't overlap the [0,1] segment
        // clip the in-circle span to the height slab [cy, cy+ch]
        double dy = y1 - y0;
        double yin = y0 + dy * tin;
        double yout = y0 + dy * tout;
        double loY = cy, hiY = cy + ch;
        if (dy > 0.0000001 || dy < -0.0000001)
        {
            double ta = (loY - y0) / dy;
            double tb = (hiY - y0) / dy;
            double tlo = ta < tb ? ta : tb;
            double thi = ta < tb ? tb : ta;
            if (tlo > tin) tin = tlo;
            if (thi < tout) tout = thi;
            if (tin > tout) return -1.0;
        }
        else
        {
            // ray is horizontal: constant height, must be within the slab
            if (y0 < loY || y0 > hiY) return -1.0;
        }
        return tin < 0.0 ? 0.0 : tin;
    }
}
