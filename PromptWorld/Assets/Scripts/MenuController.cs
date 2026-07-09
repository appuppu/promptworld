using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Title screen. Handles:
/// 1. Deep links (?stage=<id>[&key=<editKey>]) jumping straight into a stage.
/// 2. Built-in + community stage lists with rendered thumbnails.
/// 3. Name search filtering.
/// 4. The creator funnel ("CREATE YOUR OWN WORLD" -> /create).
/// </summary>
public class MenuController : MonoBehaviour
{
    [SerializeField] private RectTransform listRoot;
    [SerializeField] private TMP_InputField searchInput;
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private Button createButton;

    private static readonly string[] SortModes = { "new", "top", "hard", "easy" };
    private string currentSort = "new";
    private readonly List<TMP_Text> sortLabels = new List<TMP_Text>();

    private class Entry
    {
        public GameObject Root;
        public string Title;
        public bool IsLabel;
        public RawImage Preview;
        public string RemoteId; // set for community entries awaiting a thumbnail
    }

    private readonly List<Entry> entries = new List<Entry>();
    private static Texture2D placeholderTex;

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

        GameSession.RemoteStageId = null;
        GameSession.EditKey = null;
        GameSession.SelectedStageFile = null;

        if (searchInput != null) searchInput.onValueChanged.AddListener(Filter);
        if (nameInput != null)
        {
            nameInput.text = PlayerIdentity.Name;
            nameInput.onValueChanged.AddListener(value => PlayerIdentity.Name = value);
        }
        if (createButton != null)
        {
            createButton.onClick.AddListener(() => WebBridge.OpenUrl($"{GameSession.ApiOrigin}/create"));
        }
        BuildSortRow();

        yield return LoadBuiltInStages();
        yield return LoadCommunityStages();
        yield return FillCommunityPreviews();
    }

    /// <summary>NEW / TOP / HARD / EASY — community list sort modes.</summary>
    private void BuildSortRow()
    {
        Canvas canvas = listRoot.GetComponentInParent<Canvas>();
        if (canvas == null) return;

        float[] xs = { -270f, -90f, 90f, 270f };
        for (int i = 0; i < SortModes.Length; i++)
        {
            string mode = SortModes[i];
            var go = new GameObject($"Sort_{mode}", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(xs[i], -258f);
            rect.sizeDelta = new Vector2(170f, 44f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = mode.ToUpperInvariant();
            tmp.fontSize = 24;
            tmp.characterSpacing = 6f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(1f, 1f, 1f, mode == currentSort ? 1f : 0.35f);
            sortLabels.Add(tmp);

            var button = go.AddComponent<Button>();
            button.targetGraphic = tmp;
            button.onClick.AddListener(() => SetSort(mode));
        }
    }

    private void SetSort(string mode)
    {
        if (mode == currentSort) return;
        currentSort = mode;
        for (int i = 0; i < sortLabels.Count; i++)
        {
            sortLabels[i].color = new Color(1f, 1f, 1f, SortModes[i] == currentSort ? 1f : 0.35f);
        }
        StartCoroutine(ReloadCommunity());
    }

    private IEnumerator ReloadCommunity()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Entry entry = entries[i];
            bool isCommunity = entry.RemoteId != null || (entry.IsLabel && entry.Title == "COMMUNITY");
            if (isCommunity)
            {
                Destroy(entry.Root);
                entries.RemoveAt(i);
            }
        }
        yield return LoadCommunityStages();
        yield return FillCommunityPreviews();
        Filter(searchInput != null ? searchInput.text : "");
    }

    private IEnumerator LoadBuiltInStages()
    {
        string url = System.IO.Path.Combine(Application.streamingAssetsPath, "Stages", "index.json");
        if (!url.Contains("://")) url = "file://" + url;

        using var request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success) yield break;

        var index = JsonUtility.FromJson<StageIndex>(request.downloadHandler.text);
        foreach (StageEntry stageEntry in index.stages)
        {
            string file = stageEntry.file;
            string stageUrl = System.IO.Path.Combine(Application.streamingAssetsPath, "Stages", file);
            if (!stageUrl.Contains("://")) stageUrl = "file://" + stageUrl;

            using var stageReq = UnityWebRequest.Get(stageUrl);
            yield return stageReq.SendWebRequest();
            StageData data = stageReq.result == UnityWebRequest.Result.Success
                ? JsonUtility.FromJson<StageData>(stageReq.downloadHandler.text)
                : null;

            Entry entry = AddEntry(stageEntry.title, $"{(data != null ? data.timeLimit : 0):0}s", () =>
            {
                GameSession.SelectedStageFile = file;
                SceneManager.LoadScene("Stage");
            });
            if (data != null) entry.Preview.texture = StagePreview.Render(data);
        }
    }

    private IEnumerator LoadCommunityStages()
    {
        using var request = UnityWebRequest.Get($"{GameSession.ApiOrigin}/api/stages?sort={currentSort}");
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success) yield break;

        var list = JsonUtility.FromJson<PublishedList>(request.downloadHandler.text);
        if (list?.stages == null || list.stages.Length == 0) yield break;

        AddLabel("COMMUNITY");
        foreach (PublishedStage stage in list.stages)
        {
            string id = stage.id;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(stage.creator)) parts.Add($"by {stage.creator}");
            if (stage.clear_time_ms > 0) parts.Add($"PAR {stage.clear_time_ms / 1000f:0.0}s");
            if (stage.goods + stage.bads > 0) parts.Add($"+{stage.goods} / -{stage.bads}");
            if (stage.attempts >= 10) parts.Add($"CLEAR {stage.clears * 100 / stage.attempts}%");

            Entry entry = AddEntry(stage.name, string.Join("  ·  ", parts), () =>
            {
                GameSession.RemoteStageId = id;
                SceneManager.LoadScene("Stage");
            });
            entry.RemoteId = id;
        }
    }

    /// <summary>Progressively fetches community stage JSONs to render thumbnails.</summary>
    private IEnumerator FillCommunityPreviews()
    {
        int fetched = 0;
        foreach (Entry entry in entries)
        {
            if (entry.RemoteId == null || fetched >= 30) continue;
            fetched++;

            using var request = UnityWebRequest.Get($"{GameSession.ApiOrigin}/api/stages/{entry.RemoteId}");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) continue;

            StageData data = JsonUtility.FromJson<StageData>(request.downloadHandler.text);
            if (data != null && entry.Preview != null)
            {
                entry.Preview.texture = StagePreview.Render(data);
            }
        }
    }

    private Entry AddEntry(string title, string subtitle, UnityEngine.Events.UnityAction onClick)
    {
        var row = new GameObject($"Stage_{title}", typeof(RectTransform));
        row.transform.SetParent(listRoot, false);
        var rowRect = row.GetComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(760f, 104f);
        var layout = row.AddComponent<LayoutElement>();
        layout.preferredHeight = 104f;
        layout.preferredWidth = 760f;

        var cardImage = row.AddComponent<Image>();
        cardImage.color = new Color(1f, 1f, 1f, 0.05f);

        var button = row.AddComponent<Button>();
        button.targetGraphic = cardImage;
        button.onClick.AddListener(onClick);

        var previewGo = new GameObject("Preview", typeof(RectTransform));
        previewGo.transform.SetParent(row.transform, false);
        var previewRect = previewGo.GetComponent<RectTransform>();
        previewRect.anchorMin = new Vector2(0f, 0.5f);
        previewRect.anchorMax = new Vector2(0f, 0.5f);
        previewRect.pivot = new Vector2(0f, 0.5f);
        previewRect.anchoredPosition = new Vector2(8f, 0f);
        previewRect.sizeDelta = new Vector2(184f, 92f);
        var preview = previewGo.AddComponent<RawImage>();
        preview.texture = Placeholder();
        preview.raycastTarget = false;

        CreateRowText(row.transform, title, 32, new Vector2(212f, 18f), Color.white);
        CreateRowText(row.transform, subtitle, 20, new Vector2(212f, -26f), new Color(1f, 1f, 1f, 0.5f));

        var entry = new Entry { Root = row, Title = title, Preview = preview };
        entries.Add(entry);
        return entry;
    }

    private void AddLabel(string title)
    {
        var go = new GameObject($"Label_{title}", typeof(RectTransform));
        go.transform.SetParent(listRoot, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(760f, 46f);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = 46f;
        layout.preferredWidth = 760f;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = title;
        tmp.fontSize = 24;
        tmp.characterSpacing = 12f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, 0.45f);

        entries.Add(new Entry { Root = go, Title = title, IsLabel = true });
    }

    private void CreateRowText(Transform parent, string text, float size, Vector2 pos, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(540f, 44f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void Filter(string query)
    {
        string q = (query ?? "").Trim().ToLowerInvariant();
        foreach (Entry entry in entries)
        {
            bool visible = q.Length == 0
                ? true
                : !entry.IsLabel && entry.Title.ToLowerInvariant().Contains(q);
            entry.Root.SetActive(visible);
        }
    }

    private static Texture2D Placeholder()
    {
        if (placeholderTex != null) return placeholderTex;
        placeholderTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        placeholderTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f));
        placeholderTex.Apply();
        return placeholderTex;
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
        public string creator;
        public int clear_time_ms;
        public int goods;
        public int bads;
        public int attempts;
        public int clears;
    }
}
