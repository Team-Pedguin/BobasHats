using System.Diagnostics.CodeAnalysis;
using System.Text;
using BepInEx.Logging;
using Zorro.Core;
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

        var customization = __instance.GetComponent<Customization>();

        Logger.LogDebug("Adding hat CustomizationOptions.");
        foreach (var hat in Plugin.Instance.Hats)
            Plugin.Instance.CreateHatOption(customization, hat.Name, hat.Icon);

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

        var hatsContainer = __instance.transform.FindChildRecursive("Hat");
        //var hatsContainer = __instance.transform.FindChildRecursive("Head");

        Logger.LogDebug($"Hats container found: {hatsContainer} (inst #{hatsContainer.GetInstanceID()})");

#if DEBUG
        // build hierarchical path to the hats container for logging
        var hatsContainerPath = new StringBuilder();
        var currentTransform = hatsContainer;
        while (currentTransform != null)
        {
            if (hatsContainerPath.Length > 0)
                hatsContainerPath.Insert(0, "/");
            hatsContainerPath.Insert(0, currentTransform.name);
            currentTransform = currentTransform.parent;
        }

        Logger.LogDebug($"Hats container path: {hatsContainerPath}");
#endif

        if (hatsContainer == null)
        {
            Logger.LogError("Hats container not found, cannot instantiate hats.");
            return;
        }

        Logger.LogDebug($"Instantiating hats as children of {hatsContainer}.");
        var newPlayerHats = new List<Renderer>();
        foreach (var hat in Plugin.Instance.Hats)
        {
            if (hat.Prefab == null)
            {
                Logger.LogError($"Hat prefab for '{hat.Name}' is null, skipping instantiation.");
                continue;
            }

            var newHat = Object.Instantiate(hat.Prefab, hatsContainer);
            newHat.name = hat.Prefab.name;

            var meshRenderers = newHat.GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < meshRenderers.Length; i++)
            {
                ref readonly var mr = ref meshRenderers[i];
                mr.material.shader = Shader.Find("W/Character");
            }

            var renderer = newHat.GetComponentInChildren<Renderer>();
            renderer.gameObject.SetActive(false);

            newPlayerHats.Add(renderer);
        }
        __instance.refs.playerHats = __instance.refs.playerHats.Concat(newPlayerHats).ToArray();

        newPlayerHats.Clear();
        var dummy = PassportManager.instance.dummy;
        var dummyHatContainer = dummy.transform.FindChildRecursive("Hat");
        if (dummyHatContainer == null)
        {
            Logger.LogError("Dummy hat container not found, cannot instantiate hats for dummy.");
            return;
        }

        var firstDummyHat = dummy.refs.playerHats.FirstOrDefault();
        if (firstDummyHat == null)
        {
            Logger.LogDebug("Dummy is missing hats - something is wrong, aborting...");
            return;
        }
        var dummyHatLayer = firstDummyHat.gameObject.layer;
        Logger.LogDebug($"Instantiating hats for dummy as children of {dummyHatContainer}.");
        foreach (var hat in Plugin.Instance.Hats)
        {
            if (hat.Prefab == null)
            {
                Logger.LogError($"Hat prefab for '{hat.Name}' is null, skipping instantiation for dummy.");
                continue;
            }

            var newHat = Object.Instantiate(hat.Prefab, dummyHatContainer);
            newHat.name = hat.Prefab.name;
            newHat.SetLayerRecursivly(dummyHatLayer);

            var meshRenderers = newHat.GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < meshRenderers.Length; i++)
            {
                ref readonly var mr = ref meshRenderers[i];
                mr.material.shader = Shader.Find("W/Character");
            }

            var renderer = newHat.GetComponentInChildren<Renderer>();
            renderer.gameObject.SetActive(false);

            newPlayerHats.Add(renderer);
        }

        dummy.refs.playerHats = dummy.refs.playerHats.Concat(newPlayerHats).ToArray();
    }


    /*[HarmonyPatch(typeof(PassportManager), nameof(PassportManager.SetOption))]
    [HarmonyPostfix]
    public static void PassportManagerSetOptionPostfix(PassportManager __instance, CustomizationOption option, int index)
    {
        Logger.LogDebug($"CustomizationOption: {option.name} {option.type} {option.texture} {option.color} #{index}");

        switch (option.type)
        {
            case Customization.Type.Skin:
                var skin = Singleton<Customization>.Instance.skins[index];
                Logger.LogDebug($"Skin #{index}: {skin.name} {skin.texture} {skin.color}");
                break;
            case Customization.Type.Eyes:
                var eyes = Singleton<Customization>.Instance.eyes[index];
                Logger.LogDebug($"Eyes #{index}: {eyes.name} {eyes.texture} {eyes.color}");
                break;
            case Customization.Type.Mouth:
                var mouth = Singleton<Customization>.Instance.mouths[index];
                Logger.LogDebug($"Mouth #{index}: {mouth.name} {mouth.texture} {mouth.color}");
                break;
            case Customization.Type.Accessory:
                var accessory = Singleton<Customization>.Instance.accessories[index];
                Logger.LogDebug($"Accessory #{index}: {accessory.name} {accessory.texture} {accessory.color}");
                break;
            case Customization.Type.Fit:
                var fit = Singleton<Customization>.Instance.fits[index];
                Logger.LogDebug($"Fit #{index}: {fit.name} {fit.texture} {fit.color}");
                break;
            case Customization.Type.Hat:
                var hat = Singleton<Customization>.Instance.hats[index];
                Logger.LogDebug($"Hat #{index}: {hat.name} {hat.texture} {hat.color}");
                break;
            default:
                Logger.LogWarning($"Unknown CustomizationOption type: {option.type}");
                break;
        }
    }*/
}