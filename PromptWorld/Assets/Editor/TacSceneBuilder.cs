// Builds the self-contained TAC scene (camera + light + TacGame root).
// CLI: -executeMethod TacSceneBuilder.Build
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TacSceneBuilder
{
    [MenuItem("PromptWorld/Build Tac Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        cam.transform.position = new Vector3(0, 6, -8);
        camGo.GetComponent<Camera>().backgroundColor = new Color(0.66f, 0.71f, 0.78f);

        var lightGo = new GameObject("Sun");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.88f;
        light.color = new Color(1f, 0.98f, 0.92f);
        lightGo.transform.rotation = Quaternion.Euler(55f, 35f, 0f);

        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.55f;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.46f, 0.48f, 0.52f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 55f;
        RenderSettings.fogEndDistance = 180f;
        RenderSettings.fogColor = new Color(0.66f, 0.71f, 0.78f);

        new GameObject("TacGame").AddComponent<TacGame>();

        // iOS builds strip shaders with no asset references; the whole world is
        // built from code, so pin the shaders we Shader.Find at runtime to a
        // hidden reference object baked into the scene.
        var shaderRef = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shaderRef.name = "ShaderRef";
        shaderRef.transform.position = new Vector3(0, -500, 0);
        var mr = shaderRef.GetComponent<MeshRenderer>();
        var mStd = new Material(Shader.Find("Standard"));
        var mTrans = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));
        var mUnlit = new Material(Shader.Find("Unlit/Color"));
        var mUi = new Material(Shader.Find("UI/Default"));
        var mSpr = new Material(Shader.Find("Sprites/Default"));
        mr.sharedMaterials = new[] { mStd, mTrans, mUnlit, mUi, mSpr };

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Tac.unity");
        Debug.Log("[PromptWorld] Tac scene built: Assets/Scenes/Tac.unity");
    }
}
