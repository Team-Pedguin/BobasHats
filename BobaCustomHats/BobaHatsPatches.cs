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

        //var hatsContainer = __instance.transform.FindChildRecursive("Hat");
        var customization = Character.localCharacter.refs.customization;
        var hatsContainer = customization.transform.FindChildRecursive("Hat");

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

        newPlayerHats.Clear();
        var dummy = PassportManager.instance.dummy;
        var dummyHatContainer = dummy.transform.FindChildRecursive("Hat");
        if (dummyHatContainer == null)
        {
            Logger.LogError("Dummy hat container not found, cannot instantiate hats for dummy.");
            return;
        }

        ref var dummyHats = ref dummy.refs.playerHats;
        var firstDummyHat = dummyHats.FirstOrDefault();
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

        ref var customizationHats = ref customization.refs.playerHats;
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

        customizationHats = customizationHats.Concat(newPlayerHats).ToArray();
        dummyHats = dummyHats.Concat(newPlayerHats).ToArray();
        
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


    [HarmonyPatch(typeof(PassportManager), nameof(PassportManager.SetOption))]
    [HarmonyPostfix]
    public static void PassportManagerSetOptionPostfix(PassportManager __instance, CustomizationOption option, int index)
    {
        Logger.LogDebug($"CustomizationOption: {option.name} {option.type} {option.texture} {option.color} #{index}");

        switch (option.type)
        {
            case Customization.Type.Skin:
                var skin = Singleton<Customization>.Instance.skins[index];
                //Logger.LogDebug($"Skin #{index}: {skin.name} {skin.texture} {skin.color}");
                break;
            case Customization.Type.Eyes:
                var eyes = Singleton<Customization>.Instance.eyes[index];
                //Logger.LogDebug($"Eyes #{index}: {eyes.name} {eyes.texture} {eyes.color}");
                break;
            case Customization.Type.Mouth:
                var mouth = Singleton<Customization>.Instance.mouths[index];
                //Logger.LogDebug($"Mouth #{index}: {mouth.name} {mouth.texture} {mouth.color}");
                break;
            case Customization.Type.Accessory:
                var accessory = Singleton<Customization>.Instance.accessories[index];
                //Logger.LogDebug($"Accessory #{index}: {accessory.name} {accessory.texture} {accessory.color}");
                break;
            case Customization.Type.Fit:
                var fit = Singleton<Customization>.Instance.fits[index];
                //Logger.LogDebug($"Fit #{index}: {fit.name} {fit.texture} {fit.color}");
                break;
            case Customization.Type.Hat:
                var hat = Singleton<Customization>.Instance.hats[index];
                //Logger.LogDebug($"Hat #{index}: {hat.name} {hat.texture} {hat.color}");
                var dummy = PassportManager.instance.dummy;
                var customization = Character.localCharacter.refs.customization;
                ref var customizationHats = ref customization.refs.playerHats;
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
                var mismatchedHats = new List<(string,int,int)>();
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
}