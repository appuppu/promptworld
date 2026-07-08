using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Title screen. Handles three things:
/// 1. Deep links (?stage=<id>[&key=<editKey>]) jump straight into the stage.
/// 2. Built-in stages listed from the StreamingAssets manifest.
/// 3. Published community stages listed from the server API.
/// </summary>
public class MenuController : MonoBehaviour
{
    [SerializeField] private RectTransform listRoot;

    private IEnumerator Start()
    {
        // A shared URL should feel like launching the stage itself.
        if (!GameSession.DeepLinkConsumed)
        {
            GameSession.DeepLinkConsumed = true;
            GameSession.ParseDeepLink();
            if (!string.IsNullOrEmpty(GameSession.RemoteStageId))
            {
                SceneManager.LoadScene("Stage");
                yield break;
            }
        }

        // Arriving at the menu resets any previous selection.
        GameSession.RemoteStageId = null;
        GameSession.EditKey = null;
        GameSession.SelectedStageFile = null;

        yield return LoadBuiltInStages();
        yield return LoadCommunityStages();
    }

    private IEnumerator LoadBuiltInStages()
    {
        string url = System.IO.Path.Combine(Application.streamingAssetsPath, "Stages", "index.json");
        if (!url.Contains("://")) url = "file://" + url;

        using var request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[PromptWorld] Failed to load stage index: {request.error}");
            yield break;
        }

        var index = JsonUtility.FromJson<StageIndex>(request.downloadHandler.text);
        foreach (StageEntry entry in index.stages)
        {
            string file = entry.file;
            AddButton(entry.title, () =>
            {
                GameSession.SelectedStageFile = file;
                SceneManager.LoadScene("Stage");
            });
        }
    }

    private IEnumerator LoadCommunityStages()
    {
        using var request = UnityWebRequest.Get($"{GameSession.ApiOrigin}/api/stages");
        yield return request.SendWebRequest();

        // No server or no published stages — the menu just stays built-in.
        if (request.result != UnityWebRequest.Result.Success) yield break;

        var list = JsonUtility.FromJson<PublishedList>(request.downloadHandler.text);
        if (list?.stages == null || list.stages.Length == 0) yield break;

        AddLabel("COMMUNITY");
        foreach (PublishedStage stage in list.stages)
        {
            string id = stage.id;
            AddButton(stage.name, () =>
            {
                GameSession.RemoteStageId = id;
                SceneManager.LoadScene("Stage");
            });
        }
    }

    private void AddButton(string title, UnityEngine.Events.UnityAction onClick)
    {
        TextMeshProUGUI tmp = CreateEntry($"Stage_{title}", title, 44, Color.white);
        var button = tmp.gameObject.AddComponent<Button>();
        button.targetGraphic = tmp;
        button.onClick.AddListener(onClick);
    }

    private void AddLabel(string title)
    {
        CreateEntry($"Label_{title}", title, 26, new Color(1f, 1f, 1f, 0.5f));
    }

    private TextMeshProUGUI CreateEntry(string name, string text, float fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(listRoot, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(700f, 80f);

        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = fontSize > 30f ? 80f : 50f;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        return tmp;
    }

    [System.Serializable]
    private class PublishedList
    {
        public PublishedStage[] stages;
    }

    [System.Serializable]
    private class PublishedStage
    {
        public string id;
        public string name;
    }
}
