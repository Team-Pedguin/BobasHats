using System.Diagnostics.CodeAnalysis;
//using System.Text;
using BepInEx.Logging;
//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;
//using Photon.Pun;
using Zorro.Core;
//using Zorro.Core.Serizalization;
using Object = UnityEngine.Object;

namespace BobaHats;

[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class BobaHatsPatches
{
    private static ManualLogSource Logger => Plugin.Instance!.Logger;

    //private static bool initialized = false;


    private static Customization GetCustomizationSingleton()
    {
        return Customization.Instance
               ?? throw new InvalidOperationException("Global Customization singleton not found!");
    }

    [HarmonyPatch(typeof(CharacterCustomization), nameof(CharacterCustomization.Awake))]
    [HarmonyPostfix]
    public static void CharacterCustomizationAwakePostfix(CharacterCustomization __instance)
    {
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            Logger.LogError("Plugin instance not loaded yet, cannot instantiate hats!");
            return;
        }

        if (plugin.Hats == null || plugin.Hats.Length == 0)
        {
            Logger.LogError("No hats loaded, skipping instantiation!");
            return;
        }

        //var hatsContainer = __instance.transform.FindChildRecursive("Hat");
        ref var customizationHats = ref __instance.refs.playerHats;
        var hatsContainer = __instance.transform.FindChildRecursive("Hat");

        Logger.LogDebug($"Hats container found: {hatsContainer} (inst #{hatsContainer.GetInstanceID()})");

        if (hatsContainer == null)
        {
            Logger.LogError("Hats container not found, cannot instantiate hats.");
            return;
        }

        var hatNameSet = new HashSet<string>(plugin.Hats.Select(h => h.Name));

        var characterShader = Shader.Find("W/Character");

        Logger.LogDebug($"Instantiating hats as children of {hatsContainer}.");

        if (!customizationHats.Any(x => hatNameSet.Contains(x.name)))
        {
            var hatMat = customizationHats[0]?.GetComponentInChildren<MeshRenderer>(true)?.material;
            var hatMatFloatProps = hatMat?.GetPropertyNames(MaterialPropertyType.Float).ToDictionary(n => n, n => hatMat.GetFloat(n));

            var newPlayerWorldHats = new List<Renderer>(plugin.Hats.Length);
            foreach (var hat in plugin.Hats)
            {
                if (hat.Prefab == null)
                {
                    Logger.LogError($"Hat prefab for '{hat.Name}' is null, skipping instantiation.");
                    continue;
                }

                var newHat = Object.Instantiate(hat.Prefab, hatsContainer);
                newHat.name = hat.Name;
                //newHat.transform.SetParent(hatsContainer);

                var meshRenderers = newHat.GetComponentsInChildren<MeshRenderer>(true);
                for (var i = 0; i < meshRenderers.Length; i++)
                {
                    ref readonly var mr = ref meshRenderers[i];
                    var mat = mr.material;
                    mat.enableInstancing = true;
                    mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    mat.shader = characterShader;
                    if (hatMatFloatProps == null) continue;
                    foreach (var prop in hatMatFloatProps)
                        mat.SetFloat(prop.Key, prop.Value);
                }

                var renderer = newHat.GetComponentInChildren<Renderer>();
                renderer.gameObject.SetActive(false);

                newPlayerWorldHats.Add(renderer);
            }

            customizationHats = customizationHats.Concat(newPlayerWorldHats).ToArray();
            Logger.LogDebug($"Completed adding hats to CharacterCustomization.");
        }

        var dummy = PassportManager.instance.dummy;
        var dummyHatContainer = dummy.transform.FindChildRecursive("Hat");
        if (dummyHatContainer == null)
        {
            Logger.LogError("Dummy hat container not found, cannot instantiate hats for dummy.");
            return;
        }

        ref var dummyHats = ref dummy.refs.playerHats;
        if (!dummyHats.Any(x => hatNameSet.Contains(x.name)))
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

                var newHat = Object.Instantiate(hat.Prefab, dummyHatContainer);
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
                    mat.shader = characterShader;
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

        /*if (customizationHats.Length != dummyHats.Length)
        {
            // pad out whichever side is shorter
            var diff = customizationHats.Length - dummyHats.Length;

            switch (diff)
            {
                case > 0:
                {
                    // customization has more hats
                    Logger.LogError($"Customization has {customizationHats.Length} hats, dummy has {dummyHats.Length} hats. Padding dummy hats.");
                    var firstHat = dummyHats.First();
                    dummyHats = dummyHats.Concat(Enumerable.Repeat<Renderer>(firstHat, diff)).ToArray();
                    break;
                }
                case < 0:
                {
                    // dummy has more hats
                    Logger.LogError($"Dummy has {dummyHats.Length} hats, customization has {customizationHats.Length} hats. Padding customization hats.");
                    var firstHat = customizationHats.First();
                    customizationHats = customizationHats.Concat(Enumerable.Repeat<Renderer>(firstHat, -diff)).ToArray();
                    break;
                }
            }
        }*/

        //var newHatStartIndex = customizationHats.Length;

        // validate same hats on customization and passport dummy by name (for our hats only)
        /*for (var i = newHatStartIndex; i < customizationHats.Length; i++)
        {
            var customizationHat = customizationHats[i];
            var dummyHat = dummyHats[i];
            if (customizationHat == null || dummyHat == null)
            {
                Logger.LogError($"Customization or dummy hat at index {i} is null!");
                continue;
            }

            if (customizationHat.name != dummyHat.name)
                Logger.LogError($"Customization hat '{customizationHat.name}' does not match dummy hat '{dummyHat.name}' at index #{i}");
        }*/

        //var customization = GetCustomizationSingleton();
        //CustomizationOption[]? excessHats = null;
        /*var hatStartIndex = customization.hats.Length;
        if (hatStartIndex < newHatStartIndex)
        {
            Logger.LogError("Customization hats is misaligned, padding with empty options.");
            var missingHats = new CustomizationOption[newHatStartIndex - hatStartIndex];
            for (var i = 0; i < missingHats.Length; i++)
            {
                ref var missingHat = ref missingHats[i];
                missingHat = Plugin.CreateHatOption($"MissingHat{hatStartIndex + i}", Texture2D.whiteTexture);
                missingHat.requiredAchievement = (ACHIEVEMENTTYPE) (-1);
            }

            customization.hats = customization.hats.Concat(missingHats).ToArray();
        }
        else
        {
            if (customization.hats.Length > newHatStartIndex)
            {
                Logger.LogWarning($"Customization hats has more options than expected: {customization.hats.Length} > {newHatStartIndex}");
                excessHats = customization.hats.Skip(newHatStartIndex).ToArray();
            }
        }*/
        var customization = GetCustomizationSingleton();
        if (!customization.hats.Any(x => hatNameSet.Contains(x.name)))
        {
            Logger.LogDebug("Adding hat CustomizationOptions.");

            var newHatOptions = new List<CustomizationOption>(plugin.Hats.Length);
            foreach (var hat in plugin.Hats)
            {
                var hatOption = Plugin.CreateHatOption(hat.Name, hat.Icon);
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
    }

    /*
    [HarmonyPatch(typeof(SyncPersistentPlayerDataPackage), nameof(SyncPersistentPlayerDataPackage.SerializeData))]
    [HarmonyPostfix]
    public static void SyncPersistentPlayerDataPackageSerializeData(SyncPersistentPlayerDataPackage __instance, BinarySerializer binarySerializer)
    {
        // spacers
        binarySerializer.WriteInt(0);
        binarySerializer.WriteInt(0);
        binarySerializer.WriteInt(0);
        binarySerializer.WriteInt(0);

        // hat name
        var playerDataSvc = GameHandler.GetService<PersistentPlayerDataService>();
        if (playerDataSvc == null)
        {
            Logger.LogError("PersistentPlayerDataService is null, cannot set hat.");
            return;
        }

        var actorNumber = __instance.ActorNumber;
        var player = playerDataSvc.GetPlayerData(actorNumber);
        if (player == null)
        {
            if (PhotonNetwork.TryGetPlayer(actorNumber, out var photonPlayer))
                player = playerDataSvc.GetPlayerData(photonPlayer);
        }

        if (player == null)
        {
            Logger.LogError($"Player data for actor number {actorNumber} is null, cannot set hat.");
            return;
        }

        var customization = GetCustomizationSingleton();
        if (customization == null)
        {
            Logger.LogError("Customization component not found, cannot set hat.");
            return;
        }

        var hats = customization.hats;
        if (hats == null)
        {
            Logger.LogError("No hats found in character customization, cannot set hat.");
            return;
        }

        var hat = hats[player.customizationData.currentHat];
        var name = hat?.name ?? "";
        //var json = new JObject(new {hat = name}).ToString(Formatting.None);
        var json = JsonConvert.SerializeObject(new { hat = name }, Formatting.None);
        binarySerializer.WriteString(json, Encoding.UTF8);

        Logger.LogDebug($"Serialized hat for player #{actorNumber}: '{name}'");
    }
    */

    /*
     private static Character? GetCharacterByActorNumber(int actorNumber)
    {
        return Character.AllCharacters.FirstOrDefault(ch
            => ch.photonView != null
               && ch.photonView.Owner != null
               && ch.photonView.Owner.ActorNumber == actorNumber);
    }
    */

    /*
    [HarmonyPatch(typeof(SyncPersistentPlayerDataPackage), nameof(SyncPersistentPlayerDataPackage.DeserializeData))]
    [HarmonyPostfix]
    public static void SyncPersistentPlayerDataPackageDeserializeData(SyncPersistentPlayerDataPackage __instance, BinaryDeserializer binaryDeserializer)
    {
        // spacers
        var spacer = binaryDeserializer.ReadInt();
        if (spacer != 0)
        {
            Logger.LogError($"Missing 1st spacer trailer in SyncPersistentPlayerDataPackage.DeserializeData.");
            return;
        }

        spacer = binaryDeserializer.ReadInt();
        if (spacer != 0)
        {
            Logger.LogError($"Missing 2nd spacer trailer in SyncPersistentPlayerDataPackage.DeserializeData.");
            return;
        }

        spacer = binaryDeserializer.ReadInt();
        if (spacer != 0)
        {
            Logger.LogError($"Missing 3rd spacer trailer in SyncPersistentPlayerDataPackage.DeserializeData.");
            return;
        }

        spacer = binaryDeserializer.ReadInt();
        if (spacer != 0)
        {
            Logger.LogError($"Missing 4th spacer trailer in SyncPersistentPlayerDataPackage.DeserializeData.");
            return;
        }

        // hat name
        var json = binaryDeserializer.ReadString(Encoding.UTF8);
        using var stringReader = new StringReader(json);
        using var jsonTextReader = new JsonTextReader(stringReader);
        var jObj = (JObject)JToken.ReadFrom(jsonTextReader);
        var name = jObj["hat"]?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            Logger.LogError("Hat name is null or empty, cannot set hat.");
            return;
        }

        Logger.LogDebug($"Attempting to deserialize hat for player #{__instance.ActorNumber} to '{name}'");

        var customization = GetCustomizationSingleton();
        if (customization == null)
        {
            Logger.LogError("Customization component not found, cannot set hat.");
            return;
        }

        var hats = customization.hats;
        if (hats == null)
        {
            Logger.LogError("No hats found in character customization, cannot set hat.");
            return;
        }

        var newHatIndex = Array.FindIndex(hats, hat => hat.name == name);
        if (newHatIndex >= 0)
        {
            __instance.Data.customizationData.currentHat = newHatIndex;
            Logger.LogDebug($"Deserialized hat for player #{__instance.ActorNumber} from '{name}' to #{newHatIndex}");
        }
        else
        {
            Logger.LogError($"Hat '{name}' not found in customization hats, cannot set hat for player #{__instance.ActorNumber}");
        }
    }
    */

    /*
     private static Character? GetLocalCharacter()
    {
        return Character.localCharacter
               ?? GetCharacterByActorNumber(PhotonNetwork.LocalPlayer.ActorNumber);
    }
    */

    /*
    [HarmonyPatch(typeof(PersistentPlayerDataService), nameof(PersistentPlayerDataService.OnSyncReceived))]
    [HarmonyFinalizer]
    public static Exception? PersistentPlayerDataServiceOnSyncReceivedFinalizer(PersistentPlayerDataService __instance, SyncPersistentPlayerDataPackage package, Exception? __exception)
    {
        if (__exception != null)
        {
            Logger.LogWarning($"PersistentPlayerDataService.OnSyncReceived threw an exception\n{__exception.GetType().FullName}: {__exception.Message}\n{__exception.StackTrace}");
        }

        return null;
    }
    */
}