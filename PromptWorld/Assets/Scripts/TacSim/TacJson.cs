// Minimal JSON reader for TAC stage documents. PURE C# (no UnityEngine, no
// System.Text.Json — Unity's runtime lacks it). Numbers parse via
// double.Parse(InvariantCulture), which is IEEE-754 correctly rounded on
// .NET Core 3+ / Unity's runtime — matching ECMAScript's JSON.parse exactly.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public static class TacJson
{
    public class JObj
    {
        public Dictionary<string, object> d = new Dictionary<string, object>();
        public bool Has(string k) { return d.ContainsKey(k) && d[k] != null; }
        public double Num(string k) { return (double)d[k]; }
        public double Num(string k, double def) { return Has(k) ? (double)d[k] : def; }
        public string Str(string k) { return Has(k) ? (string)d[k] : null; }
        public JObj Obj(string k) { return (JObj)d[k]; }
        public JArr Arr(string k) { return Has(k) ? (JArr)d[k] : new JArr(); }
    }
    public class JArr
    {
        public List<object> l = new List<object>();
        public int Count { get { return l.Count; } }
        public JObj Obj(int i) { return (JObj)l[i]; }
    }

    public static JObj Parse(string s)
    {
        int i = 0;
        var v = ParseValue(s, ref i);
        return (JObj)v;
    }

    static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
    }

    static object ParseValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        char c = s[i];
        if (c == '{') return ParseObj(s, ref i);
        if (c == '[') return ParseArr(s, ref i);
        if (c == '"') return ParseStr(s, ref i);
        if (c == 't') { i += 4; return true; }
        if (c == 'f') { i += 5; return false; }
        if (c == 'n') { i += 4; return null; }
        int start = i;
        while (i < s.Length && (s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || (s[i] >= '0' && s[i] <= '9'))) i++;
        return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
    }

    static JObj ParseObj(string s, ref int i)
    {
        var o = new JObj();
        i++; // {
        SkipWs(s, ref i);
        if (s[i] == '}') { i++; return o; }
        while (true)
        {
            SkipWs(s, ref i);
            string key = ParseStr(s, ref i);
            SkipWs(s, ref i);
            i++; // :
            var v = ParseValue(s, ref i);
            o.d[key] = v is bool bb ? (object)(bb ? 1.0 : 0.0) : v;
            SkipWs(s, ref i);
            if (s[i] == ',') { i++; continue; }
            i++; // }
            return o;
        }
    }

    static JArr ParseArr(string s, ref int i)
    {
        var a = new JArr();
        i++; // [
        SkipWs(s, ref i);
        if (s[i] == ']') { i++; return a; }
        while (true)
        {
            var v = ParseValue(s, ref i);
            a.l.Add(v);
            SkipWs(s, ref i);
            if (s[i] == ',') { i++; continue; }
            i++; // ]
            return a;
        }
    }

    static string ParseStr(string s, ref int i)
    {
        i++; // "
        var sb = new StringBuilder();
        while (s[i] != '"')
        {
            if (s[i] == '\\')
            {
                i++;
                char e = s[i];
                if (e == 'n') sb.Append('\n');
                else if (e == 't') sb.Append('\t');
                else if (e == 'r') sb.Append('\r');
                else if (e == 'b') sb.Append('\b');
                else if (e == 'f') sb.Append('\f');
                else if (e == 'u')
                {
                    int code = int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    sb.Append((char)code);
                    i += 4;
                }
                else sb.Append(e);
                i++;
            }
            else
            {
                sb.Append(s[i]);
                i++;
            }
        }
        i++; // "
        return sb.ToString();
    }
}
