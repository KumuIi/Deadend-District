#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Run via Deadend District → Generate Weapon Presets.
/// Creates (or overwrites) 5 preset assets in Assets/Data/WeaponPresets/.
/// After generating, assign a preset to any WeaponSO and right-click → Apply Preset.
/// </summary>
public static class WeaponPresetGenerator
{
    private const string OutputPath = "Assets/Data/WeaponPresets";

    [MenuItem("Deadend District/Generate Weapon Presets")]
    public static void GenerateAll()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");
        if (!AssetDatabase.IsValidFolder(OutputPath))
            AssetDatabase.CreateFolder("Assets/Data", "WeaponPresets");

        CreatePreset("Pistol",     PistolRecoil(),     PistolFeel(),     "Single-handed sidearm. Aggressive muzzle rise, snappy return, high hip sway.");
        CreatePreset("SMG",        SMGRecoil(),        SMGFeel(),        "Compact full-auto. Moderate kick, fast settle, light body.");
        CreatePreset("Rifle",      RifleRecoil(),      RifleFeel(),      "Standard assault rifle. Balanced kick and control.");
        CreatePreset("HeavyRifle", HeavyRifleRecoil(), HeavyRifleFeel(), "Battle rifle / DMR. Slower rate, heavier kick per shot, more mass.");
        CreatePreset("MG",         MGRecoil(),         MGFeel(),         "Machine gun. Low per-shot kick but sustained climb; heavy body.");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[WeaponPresetGenerator] 5 presets written to {OutputPath}/");
    }

    // ── Asset creation ─────────────────────────────────────────────────────

    private static void CreatePreset(string name, WeaponRecoilData recoil, WeaponFeelData feel, string desc)
    {
        string path = $"{OutputPath}/{name}Preset.asset";
        WeaponPresetSO preset = AssetDatabase.LoadAssetAtPath<WeaponPresetSO>(path);
        if (preset == null)
        {
            preset = ScriptableObject.CreateInstance<WeaponPresetSO>();
            AssetDatabase.CreateAsset(preset, path);
        }
        preset.presetName  = name;
        preset.description = desc;
        preset.recoil      = recoil;
        preset.feel        = feel;
        EditorUtility.SetDirty(preset);
    }

    // ── Recoil presets ─────────────────────────────────────────────────────

    private static WeaponRecoilData PistolRecoil() => new WeaponRecoilData
    {
        kickUp = 4f,   kickHoriz = 1.2f, kickRoll = 0.8f,
        adsKickUp = 1.5f, adsKickHoriz = 0.3f, adsKickRoll = 0.2f,
        targetDecaySpeed = 12f, currentFollowSpeed = 22f,
        maxVertical = 20f, maxHoriz = 6f, maxRoll = 4f,
        modelKickPitch = 18f, modelKickYawRandom = 2f, modelKickRollRandom = 1.5f,
        modelKickBack = 0.008f, modelKickFollowSpeed = 30f, modelKickReturnSpeed = 10f,
        adsModelKickMultiplier = 0.35f,
        modelKickMaxPitch = 30f, modelKickMaxYaw = 8f, modelKickMaxRoll = 6f, modelKickBackMax = 0.04f,
        horizontalPattern = AnimationCurve.Constant(0, 100, 0),
        verticalPattern   = AnimationCurve.Constant(0, 100, 0),
        patternResetDelay = 0.3f, randomHorizScale = 1f,
    };

    private static WeaponRecoilData SMGRecoil() => new WeaponRecoilData
    {
        kickUp = 2.5f, kickHoriz = 0.8f, kickRoll = 0.5f,
        adsKickUp = 0.9f, adsKickHoriz = 0.2f, adsKickRoll = 0.15f,
        targetDecaySpeed = 11f, currentFollowSpeed = 20f,
        maxVertical = 15f, maxHoriz = 5f, maxRoll = 3f,
        modelKickPitch = 10f, modelKickYawRandom = 1.2f, modelKickRollRandom = 0.8f,
        modelKickBack = 0.005f, modelKickFollowSpeed = 28f, modelKickReturnSpeed = 9f,
        adsModelKickMultiplier = 0.4f,
        modelKickMaxPitch = 20f, modelKickMaxYaw = 6f, modelKickMaxRoll = 4f, modelKickBackMax = 0.03f,
        horizontalPattern = AnimationCurve.Constant(0, 100, 0),
        verticalPattern   = AnimationCurve.Constant(0, 100, 0),
        patternResetDelay = 0.25f, randomHorizScale = 1f,
    };

    private static WeaponRecoilData RifleRecoil() => new WeaponRecoilData
    {
        kickUp = 2f,   kickHoriz = 0.5f, kickRoll = 0.3f,
        adsKickUp = 0.8f, adsKickHoriz = 0.15f, adsKickRoll = 0.1f,
        targetDecaySpeed = 10f, currentFollowSpeed = 20f,
        maxVertical = 15f, maxHoriz = 5f, maxRoll = 3f,
        modelKickPitch = 6f, modelKickYawRandom = 0.8f, modelKickRollRandom = 0.4f,
        modelKickBack = 0.004f, modelKickFollowSpeed = 25f, modelKickReturnSpeed = 8f,
        adsModelKickMultiplier = 0.4f,
        modelKickMaxPitch = 15f, modelKickMaxYaw = 4f, modelKickMaxRoll = 3f, modelKickBackMax = 0.025f,
        horizontalPattern = AnimationCurve.Constant(0, 100, 0),
        verticalPattern   = AnimationCurve.Constant(0, 100, 0),
        patternResetDelay = 0.25f, randomHorizScale = 0.8f,
    };

    private static WeaponRecoilData HeavyRifleRecoil() => new WeaponRecoilData
    {
        kickUp = 3.5f, kickHoriz = 0.4f, kickRoll = 0.6f,
        adsKickUp = 1.2f, adsKickHoriz = 0.12f, adsKickRoll = 0.15f,
        targetDecaySpeed = 8f, currentFollowSpeed = 18f,
        maxVertical = 18f, maxHoriz = 4f, maxRoll = 4f,
        modelKickPitch = 9f, modelKickYawRandom = 0.6f, modelKickRollRandom = 0.6f,
        modelKickBack = 0.008f, modelKickFollowSpeed = 22f, modelKickReturnSpeed = 7f,
        adsModelKickMultiplier = 0.45f,
        modelKickMaxPitch = 20f, modelKickMaxYaw = 4f, modelKickMaxRoll = 4f, modelKickBackMax = 0.035f,
        horizontalPattern = AnimationCurve.Constant(0, 100, 0),
        verticalPattern   = AnimationCurve.Constant(0, 100, 0),
        patternResetDelay = 0.3f, randomHorizScale = 0.6f,
    };

    private static WeaponRecoilData MGRecoil() => new WeaponRecoilData
    {
        kickUp = 1.5f, kickHoriz = 0.3f, kickRoll = 0.2f,
        adsKickUp = 0.6f, adsKickHoriz = 0.1f, adsKickRoll = 0.05f,
        targetDecaySpeed = 6f, currentFollowSpeed = 16f,
        maxVertical = 12f, maxHoriz = 3f, maxRoll = 2f,
        modelKickPitch = 4f, modelKickYawRandom = 0.4f, modelKickRollRandom = 0.3f,
        modelKickBack = 0.003f, modelKickFollowSpeed = 20f, modelKickReturnSpeed = 6f,
        adsModelKickMultiplier = 0.5f,
        modelKickMaxPitch = 12f, modelKickMaxYaw = 3f, modelKickMaxRoll = 2f, modelKickBackMax = 0.02f,
        horizontalPattern = AnimationCurve.Constant(0, 100, 0),
        verticalPattern   = AnimationCurve.Constant(0, 100, 0),
        patternResetDelay = 0.2f, randomHorizScale = 0.7f,
    };

    // ── Feel presets ────────────────────────────────────────────────────────

    private static WeaponFeelData PistolFeel() => new WeaponFeelData
    {
        hipRestRotationOffset = new Vector3(-5f, 0f, 0f),
        adsRestRotationOffset = Vector3.zero,
        swayAmount = 0.06f, swaySmooth = 9f, swayMaxDelta = 0.08f,
        tiltAmount = 5f, tiltSmooth = 9f, adsTiltMultiplier = 0.15f,
        breatheAmplitudeY = 0.002f, breatheAmplitudeX = 0.001f, breatheFrequency = 0.9f,
        adsBreathScale = 0.35f,
        walkBobSpeedThreshold = 0.5f,
        walkBobFrequency = 2.4f, walkBobAmplitudeY = 0.007f, walkBobAmplitudeX = 0.004f,
        sprintBobFrequency = 3.5f, sprintBobAmplitudeY = 0.014f, sprintBobAmplitudeX = 0.007f,
        sprintTiltZ = 6f, sprintTiltSmooth = 7f,
        airborneRiseAmount = 0.05f, airborneRiseSmooth = 0.12f, airborneReturnSmooth = 0.07f,
        landSlamAmount = 0.03f, landRecoverSmooth = 0.05f, landVelocityThresh = -3f,
        stepNudgeAmount = 0.004f, stepNudgeSmooth = 11f,
        leanGunTiltAmount = 6f,
        adsAimLagAmount = 0.3f, adsAimLagCatchup = 6f, adsAimLagMax = 2.5f,
        adsInertiaAmount = 0.002f, adsInertiaSmooth = 0.08f, adsInertiaMaxDelta = 0.012f,
        masterIntensity = 1f, returnSmooth = 14f,
    };

    private static WeaponFeelData SMGFeel() => new WeaponFeelData
    {
        hipRestRotationOffset = new Vector3(-2f, 0f, 0f),
        adsRestRotationOffset = Vector3.zero,
        swayAmount = 0.05f, swaySmooth = 8f, swayMaxDelta = 0.07f,
        tiltAmount = 4.5f, tiltSmooth = 8f, adsTiltMultiplier = 0.18f,
        breatheAmplitudeY = 0.0018f, breatheAmplitudeX = 0.001f, breatheFrequency = 0.85f,
        adsBreathScale = 0.3f,
        walkBobSpeedThreshold = 0.5f,
        walkBobFrequency = 2.3f, walkBobAmplitudeY = 0.007f, walkBobAmplitudeX = 0.0035f,
        sprintBobFrequency = 3.3f, sprintBobAmplitudeY = 0.013f, sprintBobAmplitudeX = 0.006f,
        sprintTiltZ = 5.5f, sprintTiltSmooth = 6f,
        airborneRiseAmount = 0.04f, airborneRiseSmooth = 0.14f, airborneReturnSmooth = 0.08f,
        landSlamAmount = 0.025f, landRecoverSmooth = 0.06f, landVelocityThresh = -3f,
        stepNudgeAmount = 0.003f, stepNudgeSmooth = 10f,
        leanGunTiltAmount = 5.5f,
        adsAimLagAmount = 0.35f, adsAimLagCatchup = 5.5f, adsAimLagMax = 2.8f,
        adsInertiaAmount = 0.0025f, adsInertiaSmooth = 0.09f, adsInertiaMaxDelta = 0.013f,
        masterIntensity = 1f, returnSmooth = 12f,
    };

    private static WeaponFeelData RifleFeel() => new WeaponFeelData
    {
        hipRestRotationOffset = new Vector3(-1f, 0f, 0f),
        adsRestRotationOffset = Vector3.zero,
        swayAmount = 0.04f, swaySmooth = 8f, swayMaxDelta = 0.06f,
        tiltAmount = 4f, tiltSmooth = 8f, adsTiltMultiplier = 0.2f,
        breatheAmplitudeY = 0.0015f, breatheAmplitudeX = 0.0008f, breatheFrequency = 0.8f,
        adsBreathScale = 0.3f,
        walkBobSpeedThreshold = 0.5f,
        walkBobFrequency = 2.2f, walkBobAmplitudeY = 0.006f, walkBobAmplitudeX = 0.003f,
        sprintBobFrequency = 3.2f, sprintBobAmplitudeY = 0.012f, sprintBobAmplitudeX = 0.006f,
        sprintTiltZ = 5f, sprintTiltSmooth = 6f,
        airborneRiseAmount = 0.04f, airborneRiseSmooth = 0.15f, airborneReturnSmooth = 0.08f,
        landSlamAmount = 0.025f, landRecoverSmooth = 0.06f, landVelocityThresh = -3f,
        stepNudgeAmount = 0.003f, stepNudgeSmooth = 10f,
        leanGunTiltAmount = 5f,
        adsAimLagAmount = 0.4f, adsAimLagCatchup = 5f, adsAimLagMax = 3f,
        adsInertiaAmount = 0.003f, adsInertiaSmooth = 0.1f, adsInertiaMaxDelta = 0.015f,
        masterIntensity = 1f, returnSmooth = 12f,
    };

    private static WeaponFeelData HeavyRifleFeel() => new WeaponFeelData
    {
        hipRestRotationOffset = new Vector3(-1.5f, 0f, 0f),
        adsRestRotationOffset = Vector3.zero,
        swayAmount = 0.03f, swaySmooth = 7f, swayMaxDelta = 0.05f,
        tiltAmount = 3.5f, tiltSmooth = 7f, adsTiltMultiplier = 0.25f,
        breatheAmplitudeY = 0.001f, breatheAmplitudeX = 0.0006f, breatheFrequency = 0.7f,
        adsBreathScale = 0.25f,
        walkBobSpeedThreshold = 0.5f,
        walkBobFrequency = 1.9f, walkBobAmplitudeY = 0.005f, walkBobAmplitudeX = 0.0025f,
        sprintBobFrequency = 2.8f, sprintBobAmplitudeY = 0.01f, sprintBobAmplitudeX = 0.005f,
        sprintTiltZ = 4.5f, sprintTiltSmooth = 5f,
        airborneRiseAmount = 0.035f, airborneRiseSmooth = 0.18f, airborneReturnSmooth = 0.09f,
        landSlamAmount = 0.03f, landRecoverSmooth = 0.07f, landVelocityThresh = -3f,
        stepNudgeAmount = 0.002f, stepNudgeSmooth = 9f,
        leanGunTiltAmount = 4.5f,
        adsAimLagAmount = 0.45f, adsAimLagCatchup = 4.5f, adsAimLagMax = 3.5f,
        adsInertiaAmount = 0.0035f, adsInertiaSmooth = 0.12f, adsInertiaMaxDelta = 0.018f,
        masterIntensity = 1f, returnSmooth = 10f,
    };

    private static WeaponFeelData MGFeel() => new WeaponFeelData
    {
        hipRestRotationOffset = new Vector3(-1f, 0f, 0f),
        adsRestRotationOffset = Vector3.zero,
        swayAmount = 0.02f, swaySmooth = 6f, swayMaxDelta = 0.04f,
        tiltAmount = 3f, tiltSmooth = 6f, adsTiltMultiplier = 0.3f,
        breatheAmplitudeY = 0.001f, breatheAmplitudeX = 0.0005f, breatheFrequency = 0.65f,
        adsBreathScale = 0.2f,
        walkBobSpeedThreshold = 0.5f,
        walkBobFrequency = 1.8f, walkBobAmplitudeY = 0.005f, walkBobAmplitudeX = 0.002f,
        sprintBobFrequency = 2.5f, sprintBobAmplitudeY = 0.009f, sprintBobAmplitudeX = 0.004f,
        sprintTiltZ = 4f, sprintTiltSmooth = 4.5f,
        airborneRiseAmount = 0.03f, airborneRiseSmooth = 0.2f, airborneReturnSmooth = 0.1f,
        landSlamAmount = 0.035f, landRecoverSmooth = 0.08f, landVelocityThresh = -3f,
        stepNudgeAmount = 0.002f, stepNudgeSmooth = 8f,
        leanGunTiltAmount = 4f,
        adsAimLagAmount = 0.5f, adsAimLagCatchup = 4f, adsAimLagMax = 4f,
        adsInertiaAmount = 0.004f, adsInertiaSmooth = 0.14f, adsInertiaMaxDelta = 0.02f,
        masterIntensity = 1f, returnSmooth = 9f,
    };
}
#endif
