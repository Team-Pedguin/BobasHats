using System.Diagnostics.CodeAnalysis;
using System.Text;
//using System.Text;
using BepInEx.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using UnityEngine.Events;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;
//using Photon.Pun;
using Zorro.Core;
using Zorro.Core.Serizalization;
//using Zorro.Core.Serizalization;
using Object = UnityEngine.Object;

namespace BobaHats;

[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class BobaHatsPatches
{
    private const int Spacer1 = unchecked((int) 0xA48192EF);
    private const int Spacer2 = unchecked((int) 0x5A2B3C4D);
    private static ManualLogSource Logger => Plugin.Instance!.Logger;


    [HarmonyPatch(typeof(SyncPersistentPlayerDataPackage), nameof(SyncPersistentPlayerDataPackage.SerializeData))]
    [HarmonyPostfix]
    public static void SyncPersistentPlayerDataPackageSerializeData(SyncPersistentPlayerDataPackage __instance, BinarySerializer binarySerializer)
    {
        // spacers
        binarySerializer.WriteInt(0);
        binarySerializer.WriteInt(Spacer1);
        binarySerializer.WriteInt(Spacer2);

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

        var customization = Plugin.GetCustomizationSingleton();
        if (customization == null)
        {
            Logger.LogError("Customization component not instantiated yet!");
            return;
        }

        var hats = customization.hats;
        if (hats == null)
        {
            Logger.LogError("No hats found in character customization, cannot set hat.");
            return;
        }

        var currentHat = player.customizationData.currentHat;
        if (currentHat < 0 || currentHat >= hats.Length)
        {
            // custom hats might not be loaded, trigger an event and check again
            Logger.LogWarning($"Invalid hat index {currentHat} for player #{actorNumber}, custom hats may not be loaded yet!");

            Plugin.BroadcastPluginEvent(nameof(Plugin.OnLoadHats));
        }

        // refresh hats variable after loading
        hats = customization.hats;
        if (currentHat < 0 || currentHat >= hats.Length)
        {
            Logger.LogError($"Invalid hat index {currentHat} for player #{actorNumber}, custom hat may be missing!");
            return;
        }

        var hat = hats[currentHat];
        var name = hat?.name ?? "";
        //var json = new JObject(new {hat = name}).ToString(Formatting.None);
        var json = JsonConvert.SerializeObject(new {hat = name}, Formatting.None);
        binarySerializer.WriteString(json, Encoding.UTF8);

        Logger.LogDebug($"Serialized hat for player #{actorNumber}: '{name}'");
    }

    [HarmonyPatch(typeof(SyncPersistentPlayerDataPackage), nameof(SyncPersistentPlayerDataPackage.DeserializeData))]
    [HarmonyPostfix]
    public static void SyncPersistentPlayerDataPackageDeserializeData(SyncPersistentPlayerDataPackage __instance, BinaryDeserializer binaryDeserializer)
    {
        // spacers
        var spacer0 = binaryDeserializer.ReadInt();
        if (spacer0 != 0)
        {
            Logger.LogError($"Missing 1st spacer trailer in SyncPersistentPlayerDataPackage.DeserializeData.");
            return;
        }

        var spacer1 = binaryDeserializer.ReadInt();
        if (spacer1 != Spacer1)
        {
            Logger.LogError($"Missing 1st spacer trailer in SyncPersistentPlayerDataPackage.DeserializeData.");
            return;
        }

        var spacer2 = binaryDeserializer.ReadInt();
        if (spacer2 != Spacer2)
        {
            Logger.LogError($"Missing 2nd spacer trailer in SyncPersistentPlayerDataPackage.DeserializeData.");
            return;
        }

        // hat name from JSON
        try
        {
            var json = binaryDeserializer.ReadString(Encoding.UTF8);
            using var stringReader = new StringReader(json);
            using var jsonTextReader = new JsonTextReader(stringReader);
            var jObj = (JObject) JToken.ReadFrom(jsonTextReader);
            var name = jObj["hat"]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                Logger.LogError("Hat name is null or empty, cannot set hat.");
                return;
            }

            Logger.LogDebug($"Attempting to deserialize hat for player #{__instance.ActorNumber} to '{name}'");

            var customization = Plugin.GetCustomizationSingleton();
            if (customization == null)
            {
                Logger.LogError("Customization component not instantiated yet!");
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
        catch (Exception ex)
        {
            Logger.LogError($"Failed to deserialize hat for player #{__instance.ActorNumber} from JSON: {ex.Message}\n{ex.StackTrace}");
            return;
        }
    }

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


    [HarmonyPatch(typeof(CharacterCustomization), nameof(CharacterCustomization.OnPlayerDataChange))]
    [HarmonyPostfix]
    public static void CharacterCustomizationOnPlayerDataChangePostfix(CharacterCustomization __instance, PersistentPlayerData playerData)
    {
        Logger.LogDebug($"CharacterCustomization.OnPlayerDataChange called with hat index {playerData.customizationData.currentHat}");
        Plugin.BroadcastPluginEvent(nameof(Plugin.OnAddHatsForCharacter), __instance._character);
    }

    [HarmonyPatch(typeof(PersistentPlayerDataService), nameof(PersistentPlayerDataService.OnSyncReceived))]
    [HarmonyPostfix]
    public static void PersistentPlayerDataServiceOnSyncReceivedPostfix(PersistentPlayerDataService __instance, SyncPersistentPlayerDataPackage package)
    {
        Logger.LogDebug($"PersistentPlayerDataService.OnSyncReceived");
    }

    [HarmonyPatch(typeof(PersistentPlayerDataService), nameof(PersistentPlayerDataService.SetPlayerData))]
    [HarmonyPostfix]
    public static void PersistentPlayerDataServiceSetPlayerDataPostfix(PersistentPlayerDataService __instance, Photon.Realtime.Player player, PersistentPlayerData playerData)
    {
        Logger.LogDebug($"PersistentPlayerDataService.SetPlayerData");
    }

    [HarmonyPatch(typeof(PersistentPlayerDataService), nameof(PersistentPlayerDataService.GetPlayerData), typeof(Photon.Realtime.Player))]
    [HarmonyPrefix]
    public static void PersistentPlayerDataServiceGetPlayerDataPrefix(PersistentPlayerDataService __instance, Photon.Realtime.Player player)
    {
        Logger.LogDebug($"PersistentPlayerDataService.SetPlayerData(Player)");
    }

    [HarmonyPatch(typeof(PersistentPlayerDataService), nameof(PersistentPlayerDataService.GetPlayerData), typeof(int))]
    [HarmonyPrefix]
    public static void PersistentPlayerDataServiceGetPlayerDataPrefix(PersistentPlayerDataService __instance, int actorNumber)
    {
        Logger.LogDebug($"PersistentPlayerDataService.SetPlayerData(int)");
    }

    [HarmonyPatch(typeof(CharacterCustomization), nameof(CharacterCustomization.OnPlayerDataChange))]
    [HarmonyPrefix]
    public static void CharacterCustomizationOnPlayerDataChangePrefix(CharacterCustomization __instance, PersistentPlayerData playerData)
    {
        Logger.LogDebug($"CharacterCustomization.OnPlayerDataChange(int)");
        var hatIndex = playerData.customizationData.currentHat;
        var hats = __instance.refs.playerHats;
        if (hats == null || hats.Length == 0)
            return;
        for (var i = 0; i < hats.Length; ++i)
        {
            var hat = hats[i];
            hat.gameObject.SetActive(i == hatIndex);
        }
    }

    [HarmonyPatch(typeof(CharacterCustomization), nameof(CharacterCustomization.SetCustomizationForRef))]
    [HarmonyPrefix]
    public static void CharacterCustomizationSetCustomizationForRefPrefix(CustomizationRefs refs)
    {
        Logger.LogDebug($"CharacterCustomization.SetCustomizationForRef");
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
    [HarmonyPostfix]
    public static void CharacterAwakePostfix(Character __instance)
    {
        Plugin.BroadcastPluginEvent(nameof(Plugin.OnAddHatsForCharacter), __instance);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Start))]
    [HarmonyPostfix]
    public static void CharacterStartPostfix(Character __instance)
    {
        Plugin.BroadcastPluginEvent(nameof(Plugin.OnAddHatsForCharacter), __instance);
    }

    [HarmonyPatch(typeof(PlayerHandler), nameof(PlayerHandler.RegisterCharacter))]
    [HarmonyPostfix]
    public static void PlayerHandlerRegisterCharacterPostfix(PlayerHandler __instance, Character character)
    {
        if (character == null)
        {
            Logger.LogError("PlayerHandler.RegisterCharacter called with null character, cannot add hats.");
            return;
        }

        Logger.LogDebug($"PlayerHandler.RegisterCharacter called for {character.characterName} ({character.photonView.ViewID})");
        Plugin.BroadcastPluginEvent(nameof(Plugin.OnAddHatsForCharacter), character);
    }

    [HarmonyPatch(typeof(CharacterCustomization), nameof(CharacterCustomization.Start))]
    [HarmonyPrefix]
    public static void CharacterCustomizationStartPrefix(CharacterCustomization __instance)
    {
        Logger.LogDebug($"CharacterCustomization.Start called");
        Plugin.BroadcastPluginEvent(nameof(Plugin.OnLoadHats));
        var character = __instance._character;
        if (character == null) return;
        Plugin.BroadcastPluginEvent(nameof(Plugin.OnAddHatsForCharacter), character);
    }

    [HarmonyPatch(typeof(PlayerCustomizationDummy), nameof(PlayerCustomizationDummy.UpdateDummy))]
    [HarmonyPrefix]
    public static void PlayerCustomizationDummyUpdateDummyPrefix(PlayerCustomizationDummy __instance)
    {
        Logger.LogDebug($"PlayerCustomizationDummy.UpdateDummy called");

        Plugin.BroadcastPluginEvent(nameof(Plugin.OnLoadHats));
        var character = Character.localCharacter;
        if (character != null)
            Plugin.BroadcastPluginEvent(nameof(Plugin.OnAddHatsForCharacter), character);

        var pds = GameHandler.GetService<PersistentPlayerDataService>();
        var changedPlayerData = false;
        var customization = Plugin.GetCustomizationSingleton();
        if (customization != null)
        {
            var playerData = pds.GetPlayerData(PhotonNetwork.LocalPlayer);
            var customizationData = playerData.customizationData;

            if (customizationData.currentSkin < 0 || customizationData.currentSkin > customization.skins.Length)
            {
                customizationData.currentSkin = 0;
                changedPlayerData = true;
            }

            if (customizationData.currentOutfit < 0 || customizationData.currentOutfit > customization.fits.Length)
            {
                customizationData.currentOutfit = 0;
                changedPlayerData = true;
            }

            if (customizationData.currentHat < 0 || customizationData.currentHat > customization.hats.Length)
            {
                customizationData.currentHat = 0;
                changedPlayerData = true;
            }

            if (customizationData.currentEyes < 0 || customizationData.currentEyes > customization.eyes.Length || customizationData.currentEyes > __instance.refs.EyeRenderers.Length)
            {
                customizationData.currentEyes = 0;
                changedPlayerData = true;
            }

            if (customizationData.currentAccessory < 0 || customizationData.currentAccessory > customization.accessories.Length)
            {
                customizationData.currentAccessory = 0;
                changedPlayerData = true;
            }

            if (customizationData.currentMouth < 0 || customizationData.currentMouth > customization.mouths.Length)
            {
                customizationData.currentMouth = 0;
                changedPlayerData = true;
            }

            if (changedPlayerData)
                pds.SetPlayerData(PhotonNetwork.LocalPlayer, playerData);
        }
    }

    [HarmonyPatch(typeof(PlayerCustomizationDummy), nameof(PlayerCustomizationDummy.UpdateDummy))]
    [HarmonyFinalizer]
    public static Exception? PlayerCustomizationDummyUpdateDummyFinalizer(PlayerCustomizationDummy __instance, Exception? __exception)
    {
        if (__exception == null) return null;

        if (__exception is IndexOutOfRangeException oob)
        {
            Logger.LogWarning($"PlayerCustomizationDummy.UpdateDummy threw an exception\n{__exception.GetType().FullName}: {__exception.Message}\n{__exception.StackTrace}");
            return null;
        }


        return __exception;
    }
}