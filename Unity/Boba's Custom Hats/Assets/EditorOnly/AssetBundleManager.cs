using System.IO;
using UnityEditor;

namespace BobaCustomHats.EditorOnly;

public static class AssetBundleManager
{
    [MenuItem("Assets/Build AssetBundles")]
    public static void BuildAllAssetBundles()
    {
        Directory.CreateDirectory("AssetBundles");
        BuildPipeline.BuildAssetBundles("AssetBundles",
            BuildAssetBundleOptions.None,
            BuildTarget.NoTarget
        );
    }
}