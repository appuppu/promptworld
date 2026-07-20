using UnityEditor;
using UnityEngine;
using TMPro;

/// <summary>
/// Builds a DYNAMIC TMP font asset from ArialUnicode.ttf (covers Latin + JP +
/// CN + KR in one file) and registers it as the global fallback for the default
/// LiberationSans font. Dynamic mode rasterizes glyphs on demand at runtime, so
/// the WebGL/mobile build only ships the TTF — no giant pre-baked CJK atlas.
/// Without this, Japanese/Chinese/Korean UI renders as tofu boxes.
///
/// CLI: -executeMethod FontSetup.Build
/// </summary>
public static class FontSetup
{
    private const string TtfPath = "Assets/Fonts/ArialUnicode.ttf";
    private const string OutPath = "Assets/Fonts/ArialUnicode SDF.asset";
    private const string DefaultFontPath =
        "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<Font>(TtfPath);
        if (source == null)
        {
            Debug.LogError($"[PromptWorld] Font TTF not found at {TtfPath}");
            EditorApplication.Exit(1);
            return;
        }

        TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutPath);
        if (fallback == null)
        {
            // Dynamic atlas: 1024² atlas, glyphs rendered on demand at runtime.
            fallback = TMP_FontAsset.CreateFontAsset(
                source, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic, true);
            fallback.name = "ArialUnicode SDF";
            AssetDatabase.CreateAsset(fallback, OutPath);
            // Store the atlas/material sub-assets inside the font asset.
            if (fallback.atlasTextures != null)
            {
                foreach (var tex in fallback.atlasTextures)
                {
                    if (tex != null && !AssetDatabase.Contains(tex))
                        AssetDatabase.AddObjectToAsset(tex, fallback);
                }
            }
            if (fallback.material != null && !AssetDatabase.Contains(fallback.material))
                AssetDatabase.AddObjectToAsset(fallback.material, fallback);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PromptWorld] Created dynamic font asset: {OutPath}");
        }

        // Register as a global fallback on the default font.
        var def = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultFontPath);
        if (def != null)
        {
            if (def.fallbackFontAssetTable == null)
                def.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
            if (!def.fallbackFontAssetTable.Contains(fallback))
            {
                def.fallbackFontAssetTable.Add(fallback);
                EditorUtility.SetDirty(def);
                Debug.Log("[PromptWorld] Registered ArialUnicode as fallback on LiberationSans SDF.");
            }
        }
        else
        {
            Debug.LogWarning($"[PromptWorld] Default font not found at {DefaultFontPath}");
        }

        // Also add to the TMP global fallback list in the settings asset.
        var settings = TMP_Settings.instance;
        if (settings != null)
        {
            var so = new SerializedObject(settings);
            var prop = so.FindProperty("m_fallbackFontAssets");
            if (prop != null)
            {
                bool present = false;
                for (int i = 0; i < prop.arraySize; i++)
                    if (prop.GetArrayElementAtIndex(i).objectReferenceValue == fallback) present = true;
                if (!present)
                {
                    prop.InsertArrayElementAtIndex(prop.arraySize);
                    prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = fallback;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(settings);
                    Debug.Log("[PromptWorld] Added ArialUnicode to TMP global fallback list.");
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PromptWorld] FontSetup complete.");
    }
}
