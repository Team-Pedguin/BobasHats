using System.Diagnostics.CodeAnalysis;
using BepInEx.Logging;
using Zorro.Core;

namespace BobaHats;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
#pragma warning disable BepInEx002 // yes it does indeed inherit from BaseUnityPlugin (???)
public class Plugin : BaseUnityPlugin
#pragma warning restore BepInEx002
{
    internal new ManualLogSource Logger => base.Logger;

    public static Plugin? Instance { get; private set; }

    [NonSerialized]
    public Hat[]? Hats;

    public void Awake()
    {
        Instance = this;

        Logger.LogInfo($"Plugin v{MyPluginInfo.PLUGIN_VERSION} is starting up.");
        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll(typeof(BobaHatsPatches));
        StartCoroutine(LoadHatsFromBundle());
    }

    private IEnumerator LoadHatsFromBundle()
    {
        Logger.LogInfo("Loading hats from bundle.");

        var asmPath = new Uri(System.Reflection.Assembly.GetExecutingAssembly().Location).LocalPath;
        var directoryName = Path.GetDirectoryName(asmPath)!;
        var path = Path.Combine(directoryName, "bobacustomhats");

        if (!File.Exists(path))
        {
            Logger.LogError($"AssetBundle not found at {path}. Please ensure the file exists.");
            yield break;
        }

        Logger.LogDebug($"Path to AssetBundle: {path}");

        //var createRequest = AssetBundle.LoadFromMemoryAsync(File.ReadAllBytes(path)); // ???
        var createRequest = AssetBundle.LoadFromFileAsync(path);

        yield return createRequest;

        var assetBundle = createRequest.assetBundle;

        Logger.LogInfo("AssetBundle loaded.");

        var allAssetNames = assetBundle.GetAllAssetNames();

#if DEBUG
        foreach (var assetName in allAssetNames)
            Logger.LogDebug($"- {assetName}");
#endif

        var assets = assetBundle.LoadAllAssets();
        foreach (var asset in assets)
            Logger.LogDebug($"Asset: {asset.name} ({asset.GetType()})");

        Hats = assets
            .Where(x => x is GameObject or Texture2D)
            .GroupBy(x => x.name)
            .Where(x => x.Count() == 2)
            .Select(x => new Hat(
                x.Key,
                x.OfType<GameObject>().First(),
                x.OfType<Texture2D>().First()
            ))
            .ToArray();

        Logger.LogInfo($"AssetBundle contains {Hats.Length} hats.");

        Logger.LogInfo("Done!");
    }

    public static CustomizationOption CreateHatOption(string hatName, Texture2D icon)
    {
        var hatOption = ScriptableObject.CreateInstance<CustomizationOption>();
        hatOption.color = Color.white;
        hatOption.name = hatName;
        hatOption.texture = icon;
        hatOption.type = Customization.Type.Hat;
        hatOption.requiredAchievement = ACHIEVEMENTTYPE.NONE;
        return hatOption;
    }
}