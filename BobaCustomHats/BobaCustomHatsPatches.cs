using System.Diagnostics.CodeAnalysis;
using System.Text;
using BepInEx.Logging;
using Zorro.Core;
using Object = UnityEngine.Object;

namespace BobaCustomHats;

[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class BobaCustomHatsPatches
{
    private static ManualLogSource Logger => Plugin.Instance!.Logger;

    public static bool CreateHatOption(Customization customization, string name, Texture2D icon)
    {
        if (Plugin.Instance == null)
        {
            return false;
        }

        if (Array.Exists(customization.hats, hat => hat.name == name))
        {
            Logger.LogError($"Tried to add {name} a second time.");
            return false;
        }

        var hatOption = ScriptableObject.CreateInstance<CustomizationOption>();
        hatOption.color = Color.white;
        hatOption.name = name;
        hatOption.texture = icon;
        hatOption.type = Customization.Type.Hat;
        hatOption.requiredAchievement = ACHIEVEMENTTYPE.NONE;
        customization.hats = customization.hats.AddToArray(hatOption);

        Logger.LogDebug($"{name} added.");

        return true;
    }

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
            CreateHatOption(customization, hat.Name, hat.Icon);

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

            var meshRenderers = newHat.GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < meshRenderers.Length; i++)
            {
                ref readonly var mr = ref meshRenderers[i];
                mr.material.shader = Shader.Find("W/Character");
            }


            //newHat.transform.Rotate(Vector3.right, 90);
            //newHat.transform.localPosition += new Vector3(0, 0, 3.5f);

            var renderer = newHat.GetComponentInChildren<Renderer>();
            renderer.gameObject.SetActive(false);
            //newHat.SetActive(false);


            newPlayerHats.Add(renderer);
        }

        __instance.refs.playerHats = __instance.refs.playerHats.Concat(newPlayerHats).ToArray();
    }
}