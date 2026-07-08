using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Title screen: reads the stage manifest (StreamingAssets/Stages/index.json)
/// and builds one button per stage. Later the manifest is replaced by a
/// server-side listing — the UI code stays identical.
/// </summary>
public class MenuController : MonoBehaviour
{
    [SerializeField] private RectTransform listRoot;

    private IEnumerator Start()
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
            AddStageButton(entry);
        }
    }

    private void AddStageButton(StageEntry entry)
    {
        var go = new GameObject($"Stage_{entry.file}", typeof(RectTransform));
        go.transform.SetParent(listRoot, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(700f, 80f);

        var layout = go.AddComponent<LayoutElement>();
        layout.preferredHeight = 80f;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = entry.title;
        tmp.fontSize = 44;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        var button = go.AddComponent<Button>();
        button.targetGraphic = tmp;
        string file = entry.file;
        button.onClick.AddListener(() =>
        {
            GameSession.SelectedStageFile = file;
            SceneManager.LoadScene("Stage");
        });
    }
}
