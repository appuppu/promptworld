// TAC app networking: stage list / stage fetch / play stats / verified score
// submission against promptworldgame.org. All calls are best-effort.
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public static class TacNet
{
    public const string Origin = "https://promptworldgame.org";

    public static string PlayerId
    {
        get
        {
            var id = PlayerPrefs.GetString("tac_pid", "");
            if (id == "")
            {
                id = "app-" + SystemInfo.deviceUniqueIdentifier.Replace("-", "").Substring(0, 20);
                PlayerPrefs.SetString("tac_pid", id);
            }
            return id;
        }
    }

    public static IEnumerator GetJson(string path, Action<string> ok, Action fail)
    {
        using (var req = UnityWebRequest.Get(Origin + path))
        {
            req.timeout = 12;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) ok(req.downloadHandler.text);
            else if (fail != null) fail();
        }
    }

    public static IEnumerator PostJson(string path, string body, Action<string> ok, Action fail)
    {
        using (var req = new UnityWebRequest(Origin + path, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 20;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) { if (ok != null) ok(req.downloadHandler.text); }
            else if (fail != null) fail();
        }
    }

    static string Esc(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // fire-and-forget play stat (attempt or clear); surviveMs feeds the
    // testbench's average-survival stat when > 0
    public static IEnumerator ReportPlay(string stageId, bool cleared, int surviveMs = 0)
    {
        string body = "{\"playerId\":\"" + Esc(PlayerId) + "\",\"cleared\":" + (cleared ? "true" : "false") +
            (surviveMs > 0 ? ",\"surviveMs\":" + surviveMs : "") + "}";
        yield return PostJson("/api/stages/" + stageId + "/stats", body, null, null);
    }

    // replay-verified leaderboard submission — the server re-simulates the run
    public static IEnumerator SubmitScore(string stageId, int ticks, string data, Action<bool> done)
    {
        string body = "{\"playerId\":\"" + Esc(PlayerId) + "\",\"replay\":{\"v\":\"t1\",\"ticks\":" + ticks + ",\"data\":\"" + data + "\"}}";
        bool sent = false;
        yield return PostJson("/api/stages/" + stageId + "/score", body, (_) => { sent = true; }, null);
        if (done != null) done(sent);
    }

    // GOOD / BAD rating (one per device, updatable) — feeds the 高評価 sort
    public static IEnumerator Vote(string stageId, bool good, Action done)
    {
        string body = "{\"playerId\":\"" + Esc(PlayerId) + "\",\"good\":" + (good ? "true" : "false") + "}";
        yield return PostJson("/api/stages/" + stageId + "/vote", body, (_) => { if (done != null) done(); }, () => { if (done != null) done(); });
    }
}
