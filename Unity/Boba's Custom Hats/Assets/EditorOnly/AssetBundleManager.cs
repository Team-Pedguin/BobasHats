using System.IO;
using UnityEditor;
using UnityEngine;

namespace BobaCustomHats.EditorOnly;

public static class AssetBundleManager
{
    [MenuItem("Assets/Build AssetBundles")]
    public static void BuildAllAssetBundles()
    {
        BuildAssetBundlesInternal();
    }

    // Method that can be called from command line via Unity's -executeMethod
    public static void BuildAllAssetBundlesCommandLine()
    {
        try
        {
            BuildAssetBundlesInternal();
            Debug.Log("AssetBundle build completed successfully!");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"AssetBundle build failed: {ex.Message}");
            EditorApplication.Exit(1);
        }
        
        EditorApplication.Exit(0);
    }

    private static void BuildAssetBundlesInternal()
    {
        var outputPath = "AssetBundles";
        Directory.CreateDirectory(outputPath);
        
        Debug.Log($"Building AssetBundles to: {Path.GetFullPath(outputPath)}");
        
        var manifest = BuildPipeline.BuildAssetBundles(
            outputPath,
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64
        );

        if (manifest == null)
        {
            throw new System.Exception("AssetBundle build failed - manifest is null");
        }

        Debug.Log("AssetBundle build completed successfully!");
        
        // Log built bundles
        foreach (var bundle in manifest.GetAllAssetBundles())
        {
            Debug.Log($"Built AssetBundle: {bundle}");
        }
    }
}