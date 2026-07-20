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

    // Deep-link status probe: {status, cleared, name, game} without the stage
    // body, so the app can gate draft-preview to drafts only. ok(null) on any
    // failure (network, 404) so the caller can fall back to "open the app".
    public static IEnumerator GetStageMeta(string stageId, Action<string> ok)
    {
        string got = null;
        yield return GetJson("/api/stages/" + stageId + "?meta=1", (j) => got = j, null);
        ok(got);
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
    // Submit a verified clear replay. The server records the leaderboard time +
    // ghost and, if this is the FIRST clear of an unverified stage, promotes it
    // to published. done(sent, firstClear): firstClear drives the world-first
    // celebration on the result screen.
    public static IEnumerator SubmitScore(string stageId, int ticks, string data, Action<bool, bool> done)
    {
        string body = "{\"playerId\":\"" + Esc(PlayerId) + "\",\"name\":\"anonymous\",\"replay\":{\"v\":\"t1\",\"ticks\":" + ticks + ",\"data\":\"" + data + "\"}}";
        bool sent = false, first = false;
        yield return PostJson("/api/stages/" + stageId + "/score", body, (resp) =>
        {
            sent = true;
            // cheap flag scan — avoids a full JSON parse for two booleans
            if (resp != null && (resp.Contains("\"firstClear\":true") || resp.Contains("\"promoted\":true"))) first = true;
        }, null);
        if (done != null) done(sent, first);
    }

    // GOOD / BAD rating (one per device, updatable) — feeds the 高評価 sort
    public static IEnumerator Vote(string stageId, bool good, Action done)
    {
        string body = "{\"playerId\":\"" + Esc(PlayerId) + "\",\"good\":" + (good ? "true" : "false") + "}";
        yield return PostJson("/api/stages/" + stageId + "/vote", body, (_) => { if (done != null) done(); }, () => { if (done != null) done(); });
    }

    // Report / hide a stage for THIS device only. The server filters it out of
    // this player's future lists; it stays visible to everyone else.
    public static IEnumerator Hide(string stageId, Action done)
    {
        string body = "{\"playerId\":\"" + Esc(PlayerId) + "\"}";
        yield return PostJson("/api/stages/" + stageId + "/hide", body, (_) => { if (done != null) done(); }, () => { if (done != null) done(); });
    }
}
