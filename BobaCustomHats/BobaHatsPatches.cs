using System.Diagnostics.CodeAnalysis;
using System.Text;
using BepInEx.Logging;
using Photon.Pun;
using Zorro.Core;
using Zorro.Core.Serizalization;
using Object = UnityEngine.Object;

namespace BobaHats;

[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class BobaHatsPatches
{
    private static ManualLogSource Logger => Plugin.Instance!.Logger;

    [HarmonyPatch(typeof(PassportManager), "Awake")]
    [HarmonyPostfix]
    public static void PassportManagerAwakePostfix(PassportManager __instance)
    {
        if (Plugin.Instance?.Hats == null || Plugin.Instance.Hats.Length == 0)
        {
            Logger.LogError("No hats loaded, skipping PassportManager patch.");
            return;
        }

        Logger.LogDebug("Adding hat CustomizationOptions.");
        var options = new List<CustomizationOption>();
        foreach (var hat in Plugin.Instance.Hats)
        {
            var hatOption = Plugin.Instance.CreateHatOption(hat.Name, hat.Icon);
            if (hatOption == null)
            {
                Logger.LogError($"Failed to create CustomizationOption for hat '{hat.Name}'.");
                continue;
            }

            options.Add(hatOption);
        }
        
        var customization = GetCustomizationSingleton();
        if (customization == null)
        {
            Logger.LogError("Customization component not found, cannot add hat options.");
            return;
        }
        customization.hats = customization.hats.Concat(options).ToArray();


        Logger.LogDebug("Done.");
    }

    [HarmonyPatch(typeof(CharacterCustomization), "Awake")]
    [HarmonyPostfix]
    public static void CharacterCustomizationAwakePostfix(CharacterCustomization __instance)
    {
        if (Plugin.Instance?.Hats == null || Plugin.Instance.Hats.Length == 0)
        {
            Logger.LogError("No hats loaded, skipping instantiation.");
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

        var characterShader = Shader.Find("W/Character");
        var dummy = PassportManager.instance.dummy;
        var dummyHatContainer = dummy.transform.FindChildRecursive("Hat");
        ref var dummyHats = ref dummy.refs.playerHats;
        var firstDummyHat = dummyHats.FirstOrDefault();

        var hatMat = customizationHats[0]?.GetComponentInChildren<MeshRenderer>(true)?.material;
        var hatMatFloatProps = hatMat?.GetPropertyNames(MaterialPropertyType.Float).ToDictionary(n => n, n => hatMat.GetFloat(n));

        var dummyHatMat = dummyHats[0]?.GetComponentInChildren<MeshRenderer>(true)?.material;
        var dummyHatMatFloatProps = dummyHatMat?.GetPropertyNames(MaterialPropertyType.Float).ToDictionary(n => n, n => dummyHatMat.GetFloat(n));

        Logger.LogDebug($"Instantiating hats as children of {hatsContainer}.");

        var newPlayerWorldHats = new List<Renderer>();
        foreach (var hat in Plugin.Instance.Hats)
        {
            if (hat.Prefab == null)
            {
                Logger.LogError($"Hat prefab for '{hat.Name}' is null, skipping instantiation.");
                continue;
            }

            var newHat = Object.Instantiate(hat.Prefab, hatsContainer);
            newHat.name = hat.Prefab.name;
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

        if (dummyHatContainer == null)
        {
            Logger.LogError("Dummy hat container not found, cannot instantiate hats for dummy.");
            return;
        }

        if (firstDummyHat == null)
        {
            Logger.LogDebug("Dummy is missing hats - something is wrong, aborting...");
            return;
        }

        var dummyHatLayer = firstDummyHat.gameObject.layer;
        Logger.LogDebug($"Instantiating hats for dummy as children of {dummyHatContainer}.");
        var newPlayerDummyHats = new List<Renderer>();
        foreach (var hat in Plugin.Instance.Hats)
        {
            if (hat.Prefab == null)
            {
                Logger.LogError($"Hat prefab for '{hat.Name}' is null, skipping instantiation for dummy.");
                continue;
            }

            var newHat = Object.Instantiate(hat.Prefab, dummyHatContainer);
            newHat.name = hat.Prefab.name;
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

        var hatIndexStart = customizationHats.Length;
        if (customizationHats.Length != dummyHats.Length)
        {
            // pad out whichever side is shorter
            var diff = customizationHats.Length - dummyHats.Length;
            if (diff > 0)
            {
                // customization has more hats
                Logger.LogError($"Customization has {customizationHats.Length} hats, dummy has {dummyHats.Length} hats. Padding dummy hats with nulls.");
                dummyHats = dummyHats.Concat(Enumerable.Repeat<Renderer>(null!, diff)).ToArray();
            }
            else
            {
                // dummy has more hats
                Logger.LogError($"Dummy has {dummyHats.Length} hats, customization has {customizationHats.Length} hats. Padding customization hats with nulls.");
                customizationHats = customizationHats.Concat(Enumerable.Repeat<Renderer>(null!, -diff)).ToArray();
            }
        }

        customizationHats = customizationHats.Concat(newPlayerWorldHats).ToArray();
        dummyHats = dummyHats.Concat(newPlayerDummyHats).ToArray();

        // validate same hats on customization and passport dummy by name (for our hats only)
        for (var i = hatIndexStart; i < customizationHats.Length; i++)
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
        }

        Logger.LogDebug("Completed adding hats to PassportManager and CharacterCustomization.");
    }


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
        binarySerializer.WriteString(name, Encoding.UTF8);

        Logger.LogDebug($"Attempting to serialize hat for player #{actorNumber}: '{name}'");
    }

    private static Character? GetCharacterByActorNumber(int actorNumber)
    {
        return Character.AllCharacters.FirstOrDefault(ch
            => ch.photonView != null
               && ch.photonView.Owner != null
               && ch.photonView.Owner.ActorNumber == actorNumber);
    }

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
        var name = binaryDeserializer.ReadString(Encoding.UTF8);
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
            __instance.Data.customizationData.currentHat = newHatIndex;
        else
            Logger.LogError($"Hat '{name}' not found in customization hats, cannot set hat for player {__instance.ActorNumber}.");
    }

    [HarmonyPatch(typeof(PassportManager), nameof(PassportManager.SetOption))]
    [HarmonyPostfix]
    public static void PassportManagerSetOptionPostfix(PassportManager __instance, CustomizationOption option, int index)
    {
        Logger.LogDebug($"CustomizationOption: {option.name} {option.type} {option.texture} {option.color} #{index}");

        var customization = GetCustomizationSingleton();
        if (customization == null)
        {
            Logger.LogError("Customization component not found, cannot validate options.");
            return;
        }

        switch (option.type)
        {
            case Customization.Type.Skin:
                var skin = customization.skins[index];
                //Logger.LogDebug($"Skin #{index}: {skin.name} {skin.texture} {skin.color}");
                break;
            case Customization.Type.Eyes:
                var eyes = customization.eyes[index];
                //Logger.LogDebug($"Eyes #{index}: {eyes.name} {eyes.texture} {eyes.color}");
                break;
            case Customization.Type.Mouth:
                var mouth = customization.mouths[index];
                //Logger.LogDebug($"Mouth #{index}: {mouth.name} {mouth.texture} {mouth.color}");
                break;
            case Customization.Type.Accessory:
                var accessory = customization.accessories[index];
                //Logger.LogDebug($"Accessory #{index}: {accessory.name} {accessory.texture} {accessory.color}");
                break;
            case Customization.Type.Fit:
                var fit = customization.fits[index];
                //Logger.LogDebug($"Fit #{index}: {fit.name} {fit.texture} {fit.color}");
                break;
            case Customization.Type.Hat:
                var hat = customization.hats[index];
                //Logger.LogDebug($"Hat #{index}: {hat.name} {hat.texture} {hat.color}");
                var dummy = PassportManager.instance.dummy;
                var character = GetLocalCharacter();
                if (character == null)
                {
                    Logger.LogError("Character is null, cannot validate hats.");
                    return;
                }

                var charCustomization = character.refs.customization;
                ref var customizationHats = ref charCustomization.refs.playerHats;
                ref var dummyHats = ref dummy.refs.playerHats;
                if (customizationHats.Length != dummyHats.Length)
                {
                    // pad out whichever side is shorter
                    var diff = customizationHats.Length - dummyHats.Length;
                    if (diff > 0)
                    {
                        // customization has more hats
                        Logger.LogError($"Customization has {customizationHats.Length} hats, dummy has {dummyHats.Length} hats. Padding dummy hats with nulls.");
                        dummyHats = dummyHats.Concat(Enumerable.Repeat<Renderer>(null!, diff)).ToArray();
                    }
                    else
                    {
                        // dummy has more hats
                        Logger.LogError($"Dummy has {dummyHats.Length} hats, customization has {customizationHats.Length} hats. Padding customization hats with nulls.");
                        customizationHats = customizationHats.Concat(Enumerable.Repeat<Renderer>(null!, -diff)).ToArray();
                    }
                }

                // find hats with the same name in customization and dummy hats with different indices
                var mismatchedHats = new List<(string, int, int)>();
                for (var i = 0; i < customizationHats.Length; i++)
                {
                    var customizationHat = customizationHats[i];
                    if (customizationHat == null) continue;

                    for (var j = 0; j < dummyHats.Length; j++)
                    {
                        var dummyHat = dummyHats[j];
                        if (dummyHat == null) continue;

                        if (customizationHat.name == dummyHat.name && i != j)
                        {
                            mismatchedHats.Add((customizationHat.name, i, j));
                            break;
                        }
                    }
                }

                // report mismatched hats
                if (mismatchedHats.Count > 0)
                {
                    Logger.LogError($"Found {mismatchedHats.Count} mismatched hats:");
                    foreach (var (hatName, customIndex, dummyIndex) in mismatchedHats)
                    {
                        Logger.LogError($"- Hat '{hatName}' at index #{customIndex} does not match passport dummy index #{dummyIndex}");
                    }
                }

                break;
            default:
                //Logger.LogWarning($"Unknown CustomizationOption type: {option.type}");
                break;
        }
    }

    private static Customization GetCustomizationSingleton()
    {
        return Customization.Instance
               ?? throw new InvalidOperationException("Global Customization singleton not found!");
    }

    private static Character? GetLocalCharacter()
    {
        return Character.localCharacter
               ?? GetCharacterByActorNumber(PhotonNetwork.LocalPlayer.ActorNumber);
    }
}