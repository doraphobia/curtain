using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DuoCurtainFoleyInstaller
{
    private const string FootstepProfilePath = "Assets/Audio/Foley/DefaultFootstepFoley.asset";
    private const string CurtainProfilePath = "Assets/Audio/Foley/CurtainScrollFoley.asset";
    private const string WindowEventProfilePath = "Assets/Audio/Foley/WindowEventFoley.asset";
    private const string FoleyProfileRoot = "Assets/Audio/Foley/UnityCharacterFoleyProfiles";
    private const string DefaultClothingProfilePath = FoleyProfileRoot + "/Clothing_CottonFoley.asset";
    private const string DefaultEquipmentProfilePath = FoleyProfileRoot + "/Equipment_MetallicItemFoley.asset";
    private const string WetClothingProfilePath = FoleyProfileRoot + "/Clothing_WetLayerFoley.asset";
    private const string LayerGlassProfilePath = FoleyProfileRoot + "/Layer_GlassShatteredFoley.asset";
    private const string LayerGrassProfilePath = FoleyProfileRoot + "/Layer_GrassFoliageFoley.asset";
    private const string LayerWaterMovementProfilePath = FoleyProfileRoot + "/Layer_WaterMovementFoley.asset";
    private const string LayerWaterPuddleProfilePath = FoleyProfileRoot + "/Layer_WaterPuddleFoley.asset";
    private const string LayerWoodCreekProfilePath = FoleyProfileRoot + "/Layer_WoodCreekFoley.asset";

    private const string CurtainClipPath = "Assets/art/freesound_community-shower-curtain-fast-105401.mp3";
    private const string KnockClipPath = "Assets/art/universfield-fast-knocking-on-door-352704.mp3";

    [MenuItem("Tools/Duo Curtain/Foley/Install Foley System")]
    public static void InstallFoleySystem()
    {
        EnsureFolders();

        FoleyProfile footstepProfile = LoadOrCreateProfile(FootstepProfilePath);
        FoleyProfile curtainProfile = LoadOrCreateProfile(CurtainProfilePath);
        FoleyProfile windowEventProfile = LoadOrCreateProfile(WindowEventProfilePath);
        FoleyProfile defaultClothingProfile = AssetDatabase.LoadAssetAtPath<FoleyProfile>(DefaultClothingProfilePath);
        FoleyProfile defaultEquipmentProfile = AssetDatabase.LoadAssetAtPath<FoleyProfile>(DefaultEquipmentProfilePath);
        FoleyProfile wetClothingProfile = AssetDatabase.LoadAssetAtPath<FoleyProfile>(WetClothingProfilePath);
        Dictionary<string, FoleyProfile> layerProfiles = LoadLayerProfiles();

        AudioClip curtainClip = AssetDatabase.LoadAssetAtPath<AudioClip>(CurtainClipPath);
        AudioClip knockClip = AssetDatabase.LoadAssetAtPath<AudioClip>(KnockClipPath);

        ConfigureProfileIfEmpty(footstepProfile, "Default", "Footsteps", new AudioClip[0], 0.08f, 1f, true);
        ConfigureProfileIfEmpty(curtainProfile, "Curtain", "Fabric Slide", CompactClips(curtainClip), 0.03f, 1f, false);
        ConfigureProfileIfEmpty(windowEventProfile, "Window", "Window Knock", CompactClips(knockClip), 0.05f, 1f, false);

        int connectedObjects = ConnectSceneObjects(
            footstepProfile,
            curtainProfile,
            windowEventProfile,
            defaultClothingProfile,
            defaultEquipmentProfile,
            wetClothingProfile,
            layerProfiles,
            curtainClip,
            knockClip);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (connectedObjects > 0)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log("[DuoCurtainFoleyInstaller] Foley system installed. Connected scene objects: " + connectedObjects);
    }

    private static int ConnectSceneObjects(
        FoleyProfile footstepProfile,
        FoleyProfile curtainProfile,
        FoleyProfile windowEventProfile,
        FoleyProfile defaultClothingProfile,
        FoleyProfile defaultEquipmentProfile,
        FoleyProfile wetClothingProfile,
        Dictionary<string, FoleyProfile> layerProfiles,
        AudioClip curtainClip,
        AudioClip knockClip)
    {
        int connectedObjects = 0;

        PlayerControl[] playerControls = UnityEngine.Object.FindObjectsByType<PlayerControl>(FindObjectsSortMode.None);
        for (int i = 0; i < playerControls.Length; i++)
        {
            PlayerControl playerControl = playerControls[i];
            if (playerControl == null)
                continue;

            AudioClip[] cursorFootstepClips = CompactClips(playerControl.footstepClips);
            if (!footstepProfile.HasAnyClips() && cursorFootstepClips.Length > 0)
                ConfigureProfileIfEmpty(footstepProfile, "Default", "Footsteps", cursorFootstepClips, playerControl.minSecondsBetweenFootsteps, playerControl.footstepVolume, true);

            Undo.RecordObject(playerControl, "Connect Foley player control");
            FoleyPlayer player = GetOrAddComponent<FoleyPlayer>(playerControl.gameObject);
            FoleyStepClock stepClock = GetOrAddComponent<FoleyStepClock>(playerControl.gameObject);
            FoleySurfaceResolver2D resolver = GetOrAddComponent<FoleySurfaceResolver2D>(playerControl.gameObject);
            FoleyCharacterSfxController characterSfx = GetOrAddComponent<FoleyCharacterSfxController>(playerControl.gameObject);
            FoleyAnimationEventBridge animationBridge = GetOrAddComponent<FoleyAnimationEventBridge>(playerControl.gameObject);
            player.surfaceResolver = resolver;
            playerControl.useUnifiedStepClock = true;
            playerControl.stepClock = stepClock;
            playerControl.syncFootstepSettingsToStepClock = true;
            playerControl.useFoleyProfileForFootsteps = true;
            playerControl.footstepFoleyPlayer = player;
            playerControl.footstepFoleyProfile = footstepProfile;
            stepClock.distancePerStep = Mathf.Max(0.01f, playerControl.worldDistancePerFootstep);
            stepClock.minSecondsBetweenSteps = Mathf.Max(0f, playerControl.minSecondsBetweenFootsteps);
            stepClock.speedForFullCadence = Mathf.Max(0.01f, playerControl.speedForFullFootstepCadence);
            stepClock.runSpeedThreshold = Mathf.Max(0f, playerControl.runSpeedThreshold);
            ConfigureCharacterSfx(characterSfx, player, stepClock, playerControl.transform, defaultClothingProfile, defaultEquipmentProfile, wetClothingProfile);
            animationBridge.stepClock = stepClock;
            animationBridge.foleyPlayer = player;
            animationBridge.footstepProfile = footstepProfile;
            animationBridge.characterSfx = characterSfx;
            animationBridge.eventTransform = playerControl.transform;
            EditorUtility.SetDirty(playerControl);
            EditorUtility.SetDirty(player);
            EditorUtility.SetDirty(stepClock);
            EditorUtility.SetDirty(resolver);
            EditorUtility.SetDirty(characterSfx);
            EditorUtility.SetDirty(animationBridge);
            connectedObjects++;
        }

        HoverScrollColorLerp2D[] scrollTargets = UnityEngine.Object.FindObjectsByType<HoverScrollColorLerp2D>(FindObjectsSortMode.None);
        for (int i = 0; i < scrollTargets.Length; i++)
        {
            HoverScrollColorLerp2D target = scrollTargets[i];
            if (target == null)
                continue;

            Undo.RecordObject(target, "Connect Foley scroll target");
            FoleyPlayer player = GetOrAddComponent<FoleyPlayer>(target.gameObject);
            FoleySurface2D surface = GetOrAddComponent<FoleySurface2D>(target.gameObject);
            surface.surfaceId = "Curtain";
            target.useFoleyProfileForScroll = true;
            target.scrollFoleyPlayer = player;
            target.scrollFoleyProfile = curtainProfile;
            target.scrollFoleySurfaceId = "Curtain";
            if (target.scrollClip == null)
                target.scrollClip = curtainClip;
            EditorUtility.SetDirty(target);
            EditorUtility.SetDirty(player);
            EditorUtility.SetDirty(surface);
            connectedObjects++;
        }

        NightWindowVisitorEventController[] eventControllers = UnityEngine.Object.FindObjectsByType<NightWindowVisitorEventController>(FindObjectsSortMode.None);
        for (int i = 0; i < eventControllers.Length; i++)
        {
            NightWindowVisitorEventController controller = eventControllers[i];
            if (controller == null)
                continue;

            Undo.RecordObject(controller, "Connect Foley event controller");
            FoleyPlayer player = GetOrAddComponent<FoleyPlayer>(controller.gameObject);
            controller.useFoleyProfileForEventTrigger = true;
            controller.eventFoleyPlayer = player;
            controller.eventFoleyProfile = windowEventProfile;
            controller.eventFoleySurfaceId = "Window";
            if (controller.eventTriggerClip == null)
                controller.eventTriggerClip = knockClip;
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(player);
            connectedObjects++;
        }

        FoleySurfaceLayerTrigger2D[] layerTriggers = UnityEngine.Object.FindObjectsByType<FoleySurfaceLayerTrigger2D>(FindObjectsSortMode.None);
        for (int i = 0; i < layerTriggers.Length; i++)
        {
            FoleySurfaceLayerTrigger2D trigger = layerTriggers[i];
            if (trigger == null)
                continue;

            FoleyProfile profile = PickLayerProfile(trigger, layerProfiles);
            if (profile == null)
                continue;

            Undo.RecordObject(trigger, "Connect Foley layer trigger");
            if (trigger.foleyPlayer == null)
                trigger.foleyPlayer = GetOrAddComponent<FoleyPlayer>(trigger.gameObject);
            if (trigger.stepClock == null)
                trigger.stepClock = trigger.GetComponent<FoleyStepClock>();
            trigger.layerProfile = profile;
            if (string.IsNullOrWhiteSpace(trigger.surfaceIdOverride))
                trigger.surfaceIdOverride = profile.defaultSurfaceId;
            EditorUtility.SetDirty(trigger);
            EditorUtility.SetDirty(trigger.foleyPlayer);
            connectedObjects++;
        }

        FoleyWetTrigger2D[] wetTriggers = UnityEngine.Object.FindObjectsByType<FoleyWetTrigger2D>(FindObjectsSortMode.None);
        for (int i = 0; i < wetTriggers.Length; i++)
        {
            FoleyWetTrigger2D trigger = wetTriggers[i];
            if (trigger == null || wetClothingProfile == null)
                continue;

            Undo.RecordObject(trigger, "Connect Foley wet trigger");
            if (trigger.foleyPlayer == null)
                trigger.foleyPlayer = GetOrAddComponent<FoleyPlayer>(trigger.gameObject);
            if (trigger.stepClock == null)
                trigger.stepClock = trigger.GetComponent<FoleyStepClock>();
            trigger.wetProfile = wetClothingProfile;
            if (string.IsNullOrWhiteSpace(trigger.surfaceIdOverride))
                trigger.surfaceIdOverride = "Wet";
            EditorUtility.SetDirty(trigger);
            EditorUtility.SetDirty(trigger.foleyPlayer);
            connectedObjects++;
        }

        EditorUtility.SetDirty(footstepProfile);
        EditorUtility.SetDirty(curtainProfile);
        EditorUtility.SetDirty(windowEventProfile);
        return connectedObjects;
    }

    private static Dictionary<string, FoleyProfile> LoadLayerProfiles()
    {
        Dictionary<string, FoleyProfile> profiles = new Dictionary<string, FoleyProfile>();
        AddLayerProfile(profiles, "Glass", LayerGlassProfilePath);
        AddLayerProfile(profiles, "GlassShattered", LayerGlassProfilePath);
        AddLayerProfile(profiles, "Grass", LayerGrassProfilePath);
        AddLayerProfile(profiles, "GrassFoliage", LayerGrassProfilePath);
        AddLayerProfile(profiles, "WaterMovement", LayerWaterMovementProfilePath);
        AddLayerProfile(profiles, "WaterPuddle", LayerWaterPuddleProfilePath);
        AddLayerProfile(profiles, "Puddle", LayerWaterPuddleProfilePath);
        AddLayerProfile(profiles, "WoodCreek", LayerWoodCreekProfilePath);
        return profiles;
    }

    private static void AddLayerProfile(Dictionary<string, FoleyProfile> profiles, string key, string path)
    {
        FoleyProfile profile = AssetDatabase.LoadAssetAtPath<FoleyProfile>(path);
        if (profile != null)
            profiles[key] = profile;
    }

    private static void ConfigureCharacterSfx(
        FoleyCharacterSfxController characterSfx,
        FoleyPlayer player,
        FoleyStepClock stepClock,
        Transform playFrom,
        FoleyProfile defaultClothingProfile,
        FoleyProfile defaultEquipmentProfile,
        FoleyProfile wetClothingProfile)
    {
        if (characterSfx == null)
            return;

        characterSfx.foleyPlayer = player;
        characterSfx.stepClock = stepClock;
        characterSfx.playFrom = playFrom;

        EnsureSfxSlot(characterSfx, "LowClothing1", defaultClothingProfile, "Low", 0.62f, 0f);
        EnsureSfxSlot(characterSfx, "LowClothing2", defaultClothingProfile, "Low", 0.55f, 0.055f);
        EnsureSfxSlot(characterSfx, "UpperClothing1", defaultClothingProfile, "Upper", 0.5f, 0.025f);
        EnsureSfxSlot(characterSfx, "UpperClothing2", defaultClothingProfile, "Upper", 0.46f, 0.08f);
        EnsureSfxSlot(characterSfx, "LowEquipment1", defaultEquipmentProfile, "Low", 0.58f, 0.015f);
        EnsureSfxSlot(characterSfx, "LowEquipment2", defaultEquipmentProfile, "Low", 0.52f, 0.07f);
        EnsureSfxSlot(characterSfx, "UpperEquipment1", defaultEquipmentProfile, "Upper", 0.56f, 0.035f);
        EnsureSfxSlot(characterSfx, "UpperEquipment2", defaultEquipmentProfile, "Upper", 0.5f, 0.09f);
        EnsureSfxSlot(characterSfx, "WetLayer", wetClothingProfile, "Wet", 0.72f, 0.02f);
    }

    private static void EnsureSfxSlot(
        FoleyCharacterSfxController characterSfx,
        string id,
        FoleyProfile profile,
        string surfaceIdOverride,
        float volume,
        float delay)
    {
        if (characterSfx == null || string.IsNullOrWhiteSpace(id))
            return;

        if (characterSfx.slots == null)
            characterSfx.slots = new List<FoleyCharacterSfxController.SfxSlot>();

        FoleyCharacterSfxController.SfxSlot slot = null;
        for (int i = 0; i < characterSfx.slots.Count; i++)
        {
            FoleyCharacterSfxController.SfxSlot candidate = characterSfx.slots[i];
            if (candidate != null && string.Equals(candidate.id, id, System.StringComparison.OrdinalIgnoreCase))
            {
                slot = candidate;
                break;
            }
        }

        if (slot == null)
        {
            slot = new FoleyCharacterSfxController.SfxSlot { id = id };
            characterSfx.slots.Add(slot);
        }

        if (profile != null)
            slot.profile = profile;

        slot.surfaceIdOverride = surfaceIdOverride;
        slot.volume = volume;
        slot.delay = delay;
        slot.scaleDelayWithStepClock = true;
    }

    private static FoleyProfile PickLayerProfile(FoleySurfaceLayerTrigger2D trigger, Dictionary<string, FoleyProfile> profiles)
    {
        if (trigger == null || profiles == null || profiles.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(trigger.surfaceIdOverride) && profiles.TryGetValue(trigger.surfaceIdOverride.Trim(), out FoleyProfile directProfile))
            return directProfile;

        string objectName = trigger.gameObject.name;
        foreach (KeyValuePair<string, FoleyProfile> pair in profiles)
        {
            if (ContainsIgnoreCase(objectName, pair.Key))
                return pair.Value;
        }

        return null;
    }

    private static bool ContainsIgnoreCase(string text, string value)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(value))
            return false;

        return text.IndexOf(value, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "Audio");
        EnsureFolder("Assets/Audio", "Foley");
    }

    private static void EnsureFolder(string parentPath, string folderName)
    {
        string fullPath = parentPath + "/" + folderName;
        if (!AssetDatabase.IsValidFolder(fullPath))
            AssetDatabase.CreateFolder(parentPath, folderName);
    }

    private static FoleyProfile LoadOrCreateProfile(string assetPath)
    {
        FoleyProfile profile = AssetDatabase.LoadAssetAtPath<FoleyProfile>(assetPath);
        if (profile != null)
            return profile;

        profile = ScriptableObject.CreateInstance<FoleyProfile>();
        AssetDatabase.CreateAsset(profile, assetPath);
        return profile;
    }

    private static void ConfigureProfileIfEmpty(
        FoleyProfile profile,
        string surfaceId,
        string layerName,
        AudioClip[] clips,
        float minSecondsBetweenPlays,
        float volume,
        bool useNuisanceVolume)
    {
        if (profile == null || profile.HasAnyClips())
            return;

        profile.defaultSurfaceId = surfaceId;
        profile.masterVolume = Mathf.Clamp01(volume);
        profile.masterPitchRange = new Vector2(0.96f, 1.04f);
        profile.minSecondsBetweenPlays = Mathf.Max(0f, minSecondsBetweenPlays);
        profile.useNuisanceVolume = useNuisanceVolume;
        profile.nuisanceMinimumVolume = 0.6f;
        profile.nuisanceVolumeDropPerPlay = 0.035f;
        profile.resetNuisanceOnSurfaceChange = true;
        profile.spatialBlend = 0f;

        FoleyProfile.FoleyLayer layer = new FoleyProfile.FoleyLayer
        {
            name = layerName,
            clips = clips,
            volume = 1f,
            pitchRange = new Vector2(0.95f, 1.05f),
            delayRange = Vector2.zero,
            preventImmediateRepeat = true,
            overrideSpatialBlend = false,
            spatialBlend = 0f
        };

        FoleyProfile.SurfaceBank bank = new FoleyProfile.SurfaceBank
        {
            surfaceId = surfaceId,
            layers = new List<FoleyProfile.FoleyLayer> { layer }
        };

        profile.surfaceBanks = new List<FoleyProfile.SurfaceBank> { bank };
        EditorUtility.SetDirty(profile);
    }

    private static AudioClip[] CompactClips(params AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return new AudioClip[0];

        List<AudioClip> compacted = new List<AudioClip>();
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
                compacted.Add(clips[i]);
        }

        return compacted.ToArray();
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component != null)
            return component;

        return Undo.AddComponent<T>(target);
    }
}
