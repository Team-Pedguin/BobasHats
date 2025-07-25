using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BepInEx.Logging;
using Photon.Pun;
using UnityEngine.SceneManagement;
using Zorro.Core;
using Object = UnityEngine.Object;

namespace BobaHats;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
#pragma warning disable BepInEx002 // yes it does indeed inherit from BaseUnityPlugin (???)
public class Plugin : BaseUnityPlugin
#pragma warning restore BepInEx002
{
    private static readonly Lazy<Shader> LazyCharacterShader = new(Shader.Find("W/Character"));
    public static Shader CharacterShader => LazyCharacterShader.Value;
    internal new ManualLogSource Logger => base.Logger;

    public static Plugin? Instance { get; private set; }

    [NonSerialized]
    public Hat[]? Hats;

    [NonSerialized]
    public HashSet<string>? HatNames;

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


        HatNames = new HashSet<string>(Hats.Select(h => h.Name));

        Logger.LogInfo($"AssetBundle contains {Hats.Length} hats.");

        for (;;)
        {
            var failed = false;
            try
            {
                OnLoadHats();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load hats: {ex.Message}\n{ex.StackTrace}");
                failed = true;
            }

            yield return failed ? new WaitForSecondsRealtime(12f) : new WaitForSecondsRealtime(3f);

            if (Application.exitCancellationToken.IsCancellationRequested)
                break;
        }
    }

    internal static Customization? GetCustomizationSingleton()
    {
        return Customization.Instance;
    }

    internal static Character? GetCharacterByActorNumber(int actorNumber)
    {
        return Character.AllCharacters.FirstOrDefault(ch
            => ch.photonView != null
               && ch.photonView.Owner != null
               && ch.photonView.Owner.ActorNumber == actorNumber);
    }

    internal static Character? GetLocalCharacter()
    {
        return Character.localCharacter
               ?? GetCharacterByActorNumber(PhotonNetwork.LocalPlayer.ActorNumber);
    }

    public void OnLoadHats()
    {
        var plugin = Instance;
        if (plugin == null)
        {
            Logger.LogError("Plugin instance not loaded yet, cannot instantiate hats!");
            return;
        }

        if (plugin.Hats == null || plugin.Hats.Length == 0 || HatNames == null || HatNames.Count == 0)
        {
            Logger.LogError("No hats loaded, skipping instantiation!");
            return;
        }

        var customization = GetCustomizationSingleton();
        if (customization == null)
        {
            Logger.LogError("Customization component not instantiated yet!");
            return;
        }

        if (!customization.hats.Any(x => HatNames.Contains(x.name)))
        {
            Logger.LogDebug("Adding hat CustomizationOptions.");

            var newHatOptions = new List<CustomizationOption>(plugin.Hats.Length);
            foreach (var hat in plugin.Hats)
            {
                var hatOption = CreateHatOption(hat.Name, hat.Icon);
                if (hatOption == null)
                {
                    Logger.LogError($"Failed to create CustomizationOption for hat '{hat.Name}'.");
                    continue;
                }

                newHatOptions.Add(hatOption);
            }

            customization.hats = customization.hats.Concat(newHatOptions).ToArray();
            Logger.LogDebug($"Completed adding hats to Customization Options.");
        }

        var dummy = PassportManager.instance.dummy;
        var dummyHatContainer = dummy.transform.FindChildRecursive("Hat");
        if (dummyHatContainer == null)
        {
            Logger.LogError("Dummy hat container not found, cannot instantiate hats for dummy.");
            return;
        }

        ref var dummyHats = ref dummy.refs.playerHats;
        if (!dummyHats.Any(x => HatNames.Contains(x.name)))
        {
            var firstDummyHat = dummyHats.FirstOrDefault();

            var dummyHatMat = dummyHats[0]?.GetComponentInChildren<MeshRenderer>(true)?.material;
            var dummyHatMatFloatProps = dummyHatMat?.GetPropertyNames(MaterialPropertyType.Float).ToDictionary(n => n, n => dummyHatMat.GetFloat(n));


            if (firstDummyHat == null)
            {
                Logger.LogDebug("Dummy is missing hats - something is wrong, aborting...");
                return;
            }

            var dummyHatLayer = firstDummyHat.gameObject.layer;
            Logger.LogDebug($"Instantiating hats for dummy as children of {dummyHatContainer}.");
            var newPlayerDummyHats = new List<Renderer>(plugin.Hats.Length);
            foreach (var hat in plugin.Hats)
            {
                if (hat.Prefab == null)
                {
                    Logger.LogError($"Hat prefab for '{hat.Name}' is null, skipping instantiation for dummy.");
                    continue;
                }

                var newHat = Instantiate(hat.Prefab, dummyHatContainer);
                newHat.name = hat.Name;
                //newHat.transform.SetParent(dummyHatContainer);
                newHat.SetLayerRecursivly(dummyHatLayer);

                var meshRenderers = newHat.GetComponentsInChildren<MeshRenderer>(true);
                for (var i = 0; i < meshRenderers.Length; i++)
                {
                    ref readonly var mr = ref meshRenderers[i];
                    var mat = mr.material;
                    mat.enableInstancing = true;
                    mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    mat.shader = CharacterShader;
                    if (dummyHatMatFloatProps == null) continue;
                    foreach (var prop in dummyHatMatFloatProps)
                        mat.SetFloat(prop.Key, prop.Value);
                }

                var renderer = newHat.GetComponentInChildren<Renderer>();
                renderer.gameObject.SetActive(false);

                newPlayerDummyHats.Add(renderer);
            }

            dummyHats = dummyHats.Concat(newPlayerDummyHats).ToArray();
            Logger.LogDebug($"Completed adding hats to Passport dummy.");
        }


        var character = GetLocalCharacter();
        AddHatsForCharacter(character);

        var playerDataSvc = GameHandler.GetService<PersistentPlayerDataService>();
        if (playerDataSvc == null)
        {
            Logger.LogError("PersistentPlayerDataService is null, cannot set hat.");
            return;
        }
    }

    private void AddHatsForCharacter(Character? character)
    {
        var plugin = Instance;
        if (plugin == null || HatNames == null || plugin.Hats == null || plugin.Hats.Length == 0)
        {
            Logger.LogError("Plugin instance or hats not loaded yet, cannot instantiate hats!");
            return; // Plugin instance not loaded yet, cannot instantiate hats
        }

        if (character == null)
        {
            Logger.LogError("Local character not found, cannot instantiate hats!");
            return;
        }

        var characterRefs = character.refs;
        if (characterRefs == null)
        {
            Logger.LogError($"Character #{character.photonView.Owner.ActorNumber} '{character.name}' is missing refs!");
            return;
        }
        var charCustomization = characterRefs.customization;
        if (charCustomization == null)
        {
            Logger.LogError($"Character #{character.photonView.Owner.ActorNumber} '{character.name}' is missing a customization component!");
            return;
        }
        ref var customizationHats = ref charCustomization.refs.playerHats;
        if (customizationHats == null)
        {
            Logger.LogError($"Character #{character.photonView.Owner.ActorNumber} '{character.name}' is missing hats on the customization component!");
            return;
        }
        if (customizationHats.Any(x => HatNames.Contains(x.name)))
        {
            Logger.LogDebug($"Character #{character.photonView.Owner.ActorNumber} '{character.name}' already has hats, skipping.");
            return;
        }

        Logger.LogDebug($"Adding hats to Character #{character.photonView.Owner.ActorNumber} '{character.name}'");

        var hatsContainer = charCustomization.transform.FindChildRecursive("Hat");
        Logger.LogDebug($"Hats container found: {hatsContainer} (inst #{hatsContainer.GetInstanceID()})");

        if (hatsContainer == null)
        {
            Logger.LogError("Hats container not found, cannot instantiate hats.");
            return;
        }

        Logger.LogDebug($"Instantiating hats as children of {hatsContainer} (inst #{hatsContainer.GetInstanceID()})");

        var hatMat = customizationHats[0]?.GetComponentInChildren<MeshRenderer>(true)?.material;
        var hatMatFloatProps = hatMat?.GetPropertyNames(MaterialPropertyType.Float)
            .ToDictionary(n => n, n => hatMat.GetFloat(n));

        var newPlayerWorldHats = new List<Renderer>(plugin.Hats.Length);
        foreach (var hat in plugin.Hats)
        {
            if (hat.Prefab == null)
            {
                Logger.LogError($"Hat prefab for '{hat.Name}' is null, skipping instantiation.");
                continue;
            }

            var newHat = Instantiate(hat.Prefab, hatsContainer);
            newHat.name = hat.Name;
            //newHat.transform.SetParent(hatsContainer);

            var meshRenderers = newHat.GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < meshRenderers.Length; i++)
            {
                ref readonly var mr = ref meshRenderers[i];
                var mat = mr.material;
                mat.enableInstancing = true;
                mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
                mat.shader = CharacterShader;
                if (hatMatFloatProps == null) continue;
                foreach (var prop in hatMatFloatProps)
                    mat.SetFloat(prop.Key, prop.Value);
            }

            var renderer = newHat.GetComponentInChildren<Renderer>();
            renderer.gameObject.SetActive(false);

            newPlayerWorldHats.Add(renderer);
        }

        customizationHats = customizationHats.Concat(newPlayerWorldHats).ToArray();
        Logger.LogDebug($"Completed adding hats to Character #{character.photonView.Owner.ActorNumber} '{character.name}'");
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

    public static void OnAddHatsForCharacter(Character character)
    {
        if (character == null)
        {
            Instance?.Logger.LogError("OnAddHatsForCharacter called for null character.");
            return;
        }

        Instance?.Logger.LogDebug($"OnAddHatsForCharacter called for character #{character.photonView.Owner.ActorNumber} '{character.name}'");
        Instance?.AddHatsForCharacter(character);
    }

    public static void BroadcastPluginEvent(string message)
    {
        var myPlugin = Instance;
        foreach (var plugin in Resources.FindObjectsOfTypeAll<BaseUnityPlugin>())
        {
            var t = plugin.GetType();
            var method = t.GetMethod(message, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance, null, CallingConventions.Any, [], null);
            if (method == null) continue;
            myPlugin?.Logger.LogDebug($"Calling {method.Name} on plugin {t.FullName}");
            method.Invoke(method.IsStatic ? null : plugin, []);
        }
    }

    public static void BroadcastPluginEvent(string message, params object[] args)
    {
        if (args == null) throw new ArgumentNullException(nameof(args));
        var myPlugin = Instance;
        foreach (var plugin in Resources.FindObjectsOfTypeAll<BaseUnityPlugin>())
        {
            var t = plugin.GetType();
            var method = t.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.Name == message && m is MethodInfo mi && HasCompatibleParameters(mi, args))
                .Cast<MethodInfo>()
                .FirstOrDefault();
            if (method == null) continue;
            myPlugin?.Logger.LogDebug($"Calling {method.Name} on plugin {t.FullName}");
            method.Invoke(method.IsStatic ? null : plugin, args);
        }
    }

    private static bool HasCompatibleParameters(MethodInfo mi, object[] args)
    {
        var pi = mi.GetParameters();
        if (pi.Length != args.Length)
            return false;
        for (var i = 0; i < args.Length; i++)
        {
            var p = pi[i];
            if (!IsCompatibleParameter(p.ParameterType, args[i]))
                return false;
        }
        return true;
    }
    
    private static bool IsCompatibleParameter(Type paramType, object? arg)
    {
        if (arg == null)
            return !paramType.IsValueType || Nullable.GetUnderlyingType(paramType) != null;
        return paramType.IsInstanceOfType(arg)
               || (paramType.IsGenericType
                   && paramType.GetGenericTypeDefinition() == typeof(Nullable<>) 
                   && Nullable.GetUnderlyingType(paramType)!.IsInstanceOfType(arg));
    }
}