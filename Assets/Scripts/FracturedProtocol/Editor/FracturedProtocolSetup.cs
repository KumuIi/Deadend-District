#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using FracturedProtocol.Combat.Controllers;
using FracturedProtocol.Combat.FireBehaviors;
using FracturedProtocol.Combat.Items;
using FracturedProtocol.Combat.Registry;
using FracturedProtocol.Combat.Stats;
using FracturedProtocol.Combat.UI;

namespace FracturedProtocol.Combat.Editor
{
    /// <summary>
    /// Setup wizard at Tools/Fractured Protocol/Setup.
    /// Creates all sample assets and test scenes in one click. Safe to re-run.
    /// </summary>
    public sealed class FracturedProtocolSetup : EditorWindow
    {
        private const string SampleDataPath = "Assets/FracturedProtocol/_Generated/SampleData";
        private const string ResourcesPath  = "Assets/Resources/FracturedProtocol";
        private const string ScenesPath     = "Assets/FracturedProtocol/_Generated/Scenes";
        private const string MaterialsPath  = "Assets/FracturedProtocol/_Generated/Materials";
        private const string AnimationsPath = "Assets/FracturedProtocol/_Generated/Animations";

        [MenuItem("Tools/Fractured Protocol/Setup")]
        public static void OpenWindow()
        {
            FracturedProtocolSetup win = GetWindow<FracturedProtocolSetup>("FP Setup");
            win.minSize = new Vector2(320, 180);
            win.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Fractured Protocol — Project Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates sample assets in _Generated/SampleData/ and test scenes.\n" +
                "Safe to re-run — existing assets are reused, not overwritten.",
                MessageType.Info);
            EditorGUILayout.Space(8);

            if (GUILayout.Button("Run Setup", GUILayout.Height(36)))
                RunSetup();
        }

        // ─── Main entry point ──────────────────────────────────────────────────

        private static void RunSetup()
        {
            EnsureFolder(SampleDataPath);
            EnsureFolder(ResourcesPath);
            EnsureFolder(ScenesPath);
            EnsureFolder(MaterialsPath);
            EnsureFolder(AnimationsPath);

            // ── Ammo ──────────────────────────────────────────────────────────
            AmmoSO ammo762 = GetOrCreate<AmmoSO>(SampleDataPath, "Ammo_762x54R_FMJ");
            ammo762.displayName         = "7.62x54mmR FMJ";
            ammo762.gridSize            = Vector2Int.one;
            ammo762.damage              = 80f;
            ammo762.penetration         = 30f;
            ammo762.muzzleVelocity      = 865f;
            ammo762.dropFactor          = 0.003f;
            ammo762.fragmentationChance = 0.05f;
            EditorUtility.SetDirty(ammo762);

            AmmoSO ammo9mm = GetOrCreate<AmmoSO>(SampleDataPath, "Ammo_9x19_FMJ");
            ammo9mm.displayName         = "9x19mm FMJ";
            ammo9mm.gridSize            = Vector2Int.one;
            ammo9mm.damage              = 45f;
            ammo9mm.penetration         = 12f;
            ammo9mm.muzzleVelocity      = 370f;
            ammo9mm.dropFactor          = 0.005f;
            ammo9mm.fragmentationChance = 0.02f;
            EditorUtility.SetDirty(ammo9mm);

            // ── Magazines ─────────────────────────────────────────────────────
            MagazineSO mosinMag = GetOrCreate<MagazineSO>(SampleDataPath, "Mosin_Magazine_5rd");
            mosinMag.displayName    = "Mosin 5-Round Mag";
            mosinMag.gridSize       = Vector2Int.one;
            mosinMag.capacity       = 5;
            mosinMag.compatibleAmmo = new List<AmmoSO> { ammo762 };
            EditorUtility.SetDirty(mosinMag);

            MagazineSO pistolMag = GetOrCreate<MagazineSO>(SampleDataPath, "Pistol_Magazine_8rd");
            pistolMag.displayName    = "Pistol 8-Round Mag";
            pistolMag.gridSize       = Vector2Int.one;
            pistolMag.capacity       = 8;
            pistolMag.compatibleAmmo = new List<AmmoSO> { ammo9mm };
            EditorUtility.SetDirty(pistolMag);

            // ── Attachments ───────────────────────────────────────────────────
            AttachmentSO suppressor = GetOrCreate<AttachmentSO>(SampleDataPath, "MosinMuzzle_Suppressor");
            suppressor.displayName = "Mosin Muzzle Suppressor";
            suppressor.gridSize    = new Vector2Int(1, 1);
            suppressor.slotType    = AttachmentSlotType.Muzzle;
            suppressor.modifiers   = new List<StatModifier>
            {
                new StatModifier
                {
                    statType  = StatType.Spread,
                    operation = ModifierOperation.Multiplicative,
                    value     = 0.8f,
                }
            };
            EditorUtility.SetDirty(suppressor);

            // ── Fire Behaviors ────────────────────────────────────────────────
            HitscanFireSO hitscan = GetOrCreate<HitscanFireSO>(SampleDataPath, "FireBehavior_Hitscan");
            EditorUtility.SetDirty(hitscan);

            // ── Animator assets ───────────────────────────────────────────────
            AnimatorController baseController = BuildArmsAnimatorController();
            AnimatorOverrideController mosinOverride  = BuildOverrideController(baseController, "Mosin_ArmsOverride");
            AnimatorOverrideController pistolOverride = BuildOverrideController(baseController, "Pistol_ArmsOverride");

            // ── Weapons ───────────────────────────────────────────────────────
            WeaponSO mosin = GetOrCreate<WeaponSO>(SampleDataPath, "Mosin_Weapon");
            mosin.displayName       = "Mosin-Nagant";
            mosin.gridSize          = new Vector2Int(4, 1);
            mosin.baseSpread        = 0.5f;
            mosin.fireRate          = 30f;
            mosin.recoilPattern     = new Vector2(0.5f, 2.5f);
            mosin.fireBehavior      = hitscan;
            mosin.animatorOverride  = mosinOverride;
            mosin.acceptedMagazines = new List<MagazineSO> { mosinMag };
            mosin.attachmentSlots   = new List<WeaponSlot>
            {
                new WeaponSlot
                {
                    slotType = AttachmentSlotType.Muzzle,
                    compatibleAttachments = new List<AttachmentSO> { suppressor },
                }
            };
            EditorUtility.SetDirty(mosin);

            WeaponSO pistol = GetOrCreate<WeaponSO>(SampleDataPath, "Pistol_Weapon");
            pistol.displayName       = "Pistol";
            pistol.gridSize          = new Vector2Int(2, 1);
            pistol.baseSpread        = 1.5f;
            pistol.fireRate          = 400f;
            pistol.recoilPattern     = new Vector2(0.3f, 1.2f);
            pistol.fireBehavior      = hitscan;
            pistol.animatorOverride  = pistolOverride;
            pistol.acceptedMagazines = new List<MagazineSO> { pistolMag };
            pistol.attachmentSlots   = new List<WeaponSlot>();
            EditorUtility.SetDirty(pistol);

            // ── Backpacks ─────────────────────────────────────────────────────
            BackpackSO playerPack = GetOrCreate<BackpackSO>(SampleDataPath, "Backpack_Player_6x4");
            playerPack.displayName    = "Player Backpack";
            playerPack.gridSize       = new Vector2Int(2, 1);
            playerPack.gridDimensions = new Vector2Int(6, 4);
            EditorUtility.SetDirty(playerPack);

            BackpackSO smallPack = GetOrCreate<BackpackSO>(SampleDataPath, "Backpack_Small_4x4");
            smallPack.displayName    = "Small Backpack";
            smallPack.gridSize       = new Vector2Int(2, 1);
            smallPack.gridDimensions = new Vector2Int(4, 4);
            EditorUtility.SetDirty(smallPack);

            // ── Item Registry ─────────────────────────────────────────────────
            ItemRegistry registry = GetOrCreate<ItemRegistry>(ResourcesPath, "ItemRegistry");
            registry.RefreshFromAssets();
            EditorUtility.SetDirty(registry);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ── Test Scenes ───────────────────────────────────────────────────
            BuildPhase2Scene(mosin, mosinMag);
            BuildPhase3Scene(mosin, pistol, mosinMag, baseController);
            BuildPhase4Scene(pistol, pistolMag, baseController);
            BuildPhase5Scene(mosin, mosinMag);

            Debug.Log("[FracturedProtocol] Setup complete. Sample assets in _Generated/SampleData/");
        }

        // ─── Animator builders ─────────────────────────────────────────────────

        private static AnimatorController BuildArmsAnimatorController()
        {
            string path = AnimationsPath + "/Arms_Base.controller";
            AnimatorController? existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null) return existing;

            AnimationClip idleClip    = GetOrCreateClip("Arms_Idle");
            AnimationClip fireClip    = GetOrCreateClip("Arms_Fire");
            AnimationClip reloadClip  = GetOrCreateClip("Arms_Reload");
            AnimationClip inspectClip = GetOrCreateClip("Arms_Inspect");
            AnimationClip aimClip     = GetOrCreateClip("Arms_Aim");

            AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);

            ctrl.AddParameter("Fire",       AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Reload",     AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Inspect",    AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("IsAiming",   AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("EmptyClick", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;

            AnimatorState idle    = sm.AddState("Idle");
            AnimatorState fire    = sm.AddState("Fire");
            AnimatorState reload  = sm.AddState("Reload");
            AnimatorState inspect = sm.AddState("Inspect");
            AnimatorState aim     = sm.AddState("Aim");

            idle.motion    = idleClip;
            fire.motion    = fireClip;
            reload.motion  = reloadClip;
            inspect.motion = inspectClip;
            aim.motion     = aimClip;
            sm.defaultState = idle;

            AddTriggerTransition(idle, fire,  "Fire");
            AddExitTimeTransition(fire, idle);
            AddBoolTransition(idle, aim,  "IsAiming", true);
            AddBoolTransition(aim,  idle, "IsAiming", false);
            AddAnyTriggerTransition(sm, reload,  "Reload");
            AddExitTimeTransition(reload, idle);
            AddAnyTriggerTransition(sm, inspect, "Inspect");
            AddExitTimeTransition(inspect, idle);

            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        private static AnimatorOverrideController BuildOverrideController(AnimatorController baseCtrl, string name)
        {
            string path = AnimationsPath + "/" + name + ".overrideController";
            AnimatorOverrideController? existing = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (existing != null) return existing;

            AnimatorOverrideController oc = new AnimatorOverrideController(baseCtrl);
            AssetDatabase.CreateAsset(oc, path);
            return oc;
        }

        private static AnimationClip GetOrCreateClip(string clipName)
        {
            string path = AnimationsPath + "/" + clipName + ".anim";
            AnimationClip? existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) return existing;

            AnimationClip clip = new AnimationClip();
            clip.name = clipName;
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // ─── Transition helpers ────────────────────────────────────────────────

        private static void AddTriggerTransition(AnimatorState from, AnimatorState to, string trigger)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.AddCondition(AnimatorConditionMode.If, 0, trigger);
            t.hasExitTime = false;
            t.duration    = 0f;
        }

        private static void AddBoolTransition(AnimatorState from, AnimatorState to, string param, bool value)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, param);
            t.hasExitTime = false;
            t.duration    = 0f;
        }

        private static void AddExitTimeTransition(AnimatorState from, AnimatorState to)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime    = 1f;
            t.duration    = 0f;
        }

        private static void AddAnyTriggerTransition(AnimatorStateMachine sm, AnimatorState to, string trigger)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(to);
            t.AddCondition(AnimatorConditionMode.If, 0, trigger);
            t.hasExitTime = false;
            t.duration    = 0f;
        }

        // ─── Scene builders ────────────────────────────────────────────────────

        private static void BuildPhase2Scene(WeaponSO mosin, MagazineSO mosinMag)
        {
            string scenePath = ScenesPath + "/Phase2_FiringTest.unity";
            Scene prev  = EditorSceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);

            AddDirectionalLight();
            AddCamera(new Vector3(0f, 2.5f, -4f), Quaternion.Euler(12f, 0f, 0f));
            AddGroundPlane();

            GameObject player   = MakePlayerCapsule();
            GameObject muzzleGO = MakeMuzzlePoint(player);

            WeaponController wc = player.AddComponent<WeaponController>();
            SerializedObject wcSO = new SerializedObject(wc);
            wcSO.FindProperty("debugWeapon").objectReferenceValue  = mosin;
            wcSO.FindProperty("debugMagazine").objectReferenceValue = mosinMag;
            wcSO.FindProperty("muzzlePoint").objectReferenceValue  = muzzleGO.transform;
            wcSO.ApplyModifiedPropertiesWithoutUndo();

            AddTargetRow(new float[] { 5f, 8f, 11f, 14f, 17f, 20f });

            EditorSceneManager.SaveScene(scene, scenePath);
            EditorSceneManager.SetActiveScene(prev);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log("[FracturedProtocol] Phase 2 scene saved.");
        }

        private static void BuildPhase3Scene(WeaponSO mosin, WeaponSO pistol,
            MagazineSO mosinMag, AnimatorController baseController)
        {
            string scenePath = ScenesPath + "/Phase3_AnimatorSwap.unity";
            Scene prev  = EditorSceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);

            AddDirectionalLight();
            AddCamera(new Vector3(0f, 2.5f, -4f), Quaternion.Euler(12f, 0f, 0f));
            AddGroundPlane();

            GameObject player   = MakePlayerCapsule();
            GameObject muzzleGO = MakeMuzzlePoint(player);

            Animator animator = player.AddComponent<Animator>();
            animator.runtimeAnimatorController = baseController;

            AddGunModelPlaceholder(muzzleGO);

            WeaponController wc = player.AddComponent<WeaponController>();
            SerializedObject wcSO = new SerializedObject(wc);
            wcSO.FindProperty("debugWeapon").objectReferenceValue   = mosin;
            wcSO.FindProperty("debugWeapon2").objectReferenceValue  = pistol;
            wcSO.FindProperty("debugMagazine").objectReferenceValue = mosinMag;
            wcSO.FindProperty("muzzlePoint").objectReferenceValue   = muzzleGO.transform;
            wcSO.FindProperty("armsAnimator").objectReferenceValue  = animator;
            wcSO.ApplyModifiedPropertiesWithoutUndo();

            AddTargetRow(new float[] { 5f, 10f, 15f });

            EditorSceneManager.SaveScene(scene, scenePath);
            EditorSceneManager.SetActiveScene(prev);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log("[FracturedProtocol] Phase 3 scene saved.");
        }

        private static void BuildPhase4Scene(WeaponSO pistol, MagazineSO pistolMag,
            AnimatorController baseController)
        {
            string scenePath = ScenesPath + "/Phase4_BloomCrosshair.unity";
            Scene prev  = EditorSceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);

            AddDirectionalLight();
            AddCamera(new Vector3(0f, 2.5f, -4f), Quaternion.Euler(12f, 0f, 0f));
            AddGroundPlane();

            GameObject player   = MakePlayerCapsule();
            GameObject muzzleGO = MakeMuzzlePoint(player);

            Animator animator = player.AddComponent<Animator>();
            animator.runtimeAnimatorController = baseController;
            AddGunModelPlaceholder(muzzleGO);

            WeaponController wc = player.AddComponent<WeaponController>();
            SerializedObject wcSO = new SerializedObject(wc);
            wcSO.FindProperty("debugWeapon").objectReferenceValue   = pistol;
            wcSO.FindProperty("debugMagazine").objectReferenceValue = pistolMag;
            wcSO.FindProperty("muzzlePoint").objectReferenceValue   = muzzleGO.transform;
            wcSO.FindProperty("armsAnimator").objectReferenceValue  = animator;
            wcSO.ApplyModifiedPropertiesWithoutUndo();

            // Crosshair canvas
            WeaponController wcRef = wc;
            BuildCrosshairCanvas(wcRef);

            AddTargetRow(new float[] { 5f, 10f, 15f });

            EditorSceneManager.SaveScene(scene, scenePath);
            EditorSceneManager.SetActiveScene(prev);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log("[FracturedProtocol] Phase 4 scene saved.");
        }

        private static void BuildPhase5Scene(WeaponSO mosin, MagazineSO mosinMag)
        {
            string scenePath = ScenesPath + "/Phase5_MagazineReload.unity";
            Scene prev  = EditorSceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);

            AddDirectionalLight();
            AddCamera(new Vector3(0f, 2.5f, -4f), Quaternion.Euler(12f, 0f, 0f));
            AddGroundPlane();

            GameObject player   = MakePlayerCapsule();
            GameObject muzzleGO = MakeMuzzlePoint(player);
            AddGunModelPlaceholder(muzzleGO);

            WeaponController wc = player.AddComponent<WeaponController>();
            SerializedObject wcSO = new SerializedObject(wc);
            wcSO.FindProperty("debugWeapon").objectReferenceValue   = mosin;
            wcSO.FindProperty("debugMagazine").objectReferenceValue = mosinMag;
            wcSO.FindProperty("muzzlePoint").objectReferenceValue   = muzzleGO.transform;
            wcSO.ApplyModifiedPropertiesWithoutUndo();

            BuildCrosshairCanvas(wc);
            AddTargetRow(new float[] { 5f, 10f, 15f });

            // Instructions label in scene hierarchy as a reminder
            new GameObject("[Controls] LMB=Fire  R=Reload  Watch Console");

            EditorSceneManager.SaveScene(scene, scenePath);
            EditorSceneManager.SetActiveScene(prev);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log("[FracturedProtocol] Phase 5 scene saved.");
        }

        // ─── Scene construction helpers ────────────────────────────────────────

        private static void AddDirectionalLight()
        {
            GameObject go = new GameObject("Directional Light");
            Light l = go.AddComponent<Light>();
            l.type      = LightType.Directional;
            l.intensity = 1f;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void AddCamera(Vector3 pos, Quaternion rot)
        {
            GameObject go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            go.transform.position = pos;
            go.transform.rotation = rot;
        }

        private static void AddGroundPlane()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Ground";
            go.transform.localScale = new Vector3(3f, 1f, 6f);
        }

        private static GameObject MakePlayerCapsule()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Player";
            go.transform.position = Vector3.zero;
            return go;
        }

        private static GameObject MakeMuzzlePoint(GameObject parent)
        {
            GameObject go = new GameObject("MuzzlePoint");
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = new Vector3(0f, 0.5f, 0.6f);
            go.transform.localRotation = Quaternion.identity;
            return go;
        }

        private static void AddGunModelPlaceholder(GameObject muzzleParent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "GunModel_Placeholder";
            go.transform.SetParent(muzzleParent.transform);
            go.transform.localPosition = new Vector3(0f, 0f, 0.25f);
            go.transform.localScale    = new Vector3(0.08f, 0.08f, 0.5f);
            go.transform.localRotation = Quaternion.identity;
        }

        private static void AddTargetRow(float[] distances)
        {
            Material mat = GetOrCreateTargetMaterial();
            for (int i = 0; i < distances.Length; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = string.Format("Target_{0:D2}", i + 1);
                cube.transform.position = new Vector3(0f, 0.5f, distances[i]);
                cube.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        private static void BuildCrosshairCanvas(WeaponController wc)
        {
            GameObject canvasGO = new GameObject("CrosshairCanvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            GameObject centerGO = new GameObject("CrosshairCenter");
            centerGO.transform.SetParent(canvasGO.transform, false);
            RectTransform centerRT = centerGO.AddComponent<RectTransform>();
            centerRT.anchorMin        = new Vector2(0.5f, 0.5f);
            centerRT.anchorMax        = new Vector2(0.5f, 0.5f);
            centerRT.anchoredPosition = Vector2.zero;
            centerRT.sizeDelta        = Vector2.zero;

            CrosshairUI crosshair = centerGO.AddComponent<CrosshairUI>();

            RectTransform top    = MakeCrosshairTick(centerGO, "Tick_Top",    new Vector2(3f, 14f), new Vector2(0f,   20f));
            RectTransform bottom = MakeCrosshairTick(centerGO, "Tick_Bottom", new Vector2(3f, 14f), new Vector2(0f,  -20f));
            RectTransform left   = MakeCrosshairTick(centerGO, "Tick_Left",   new Vector2(14f, 3f), new Vector2(-20f,  0f));
            RectTransform right  = MakeCrosshairTick(centerGO, "Tick_Right",  new Vector2(14f, 3f), new Vector2( 20f,  0f));

            SerializedObject so = new SerializedObject(crosshair);
            so.FindProperty("_top").objectReferenceValue              = top;
            so.FindProperty("_bottom").objectReferenceValue           = bottom;
            so.FindProperty("_left").objectReferenceValue             = left;
            so.FindProperty("_right").objectReferenceValue            = right;
            so.FindProperty("_weaponController").objectReferenceValue = wc;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ─── Asset helpers ─────────────────────────────────────────────────────

        private static RectTransform MakeCrosshairTick(
            GameObject parent, string tickName, Vector2 size, Vector2 anchoredPos)
        {
            GameObject go = new GameObject(tickName);
            go.transform.SetParent(parent.transform, false);
            Image img = go.AddComponent<Image>();
            img.color = Color.white;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta        = size;
            rt.anchoredPosition = anchoredPos;
            return rt;
        }

        private static Material GetOrCreateTargetMaterial()
        {
            string matPath = MaterialsPath + "/Target_Red.mat";
            Material? existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing != null) return existing;

            Shader? shader = Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Standard");
            Material mat = shader != null
                ? new Material(shader)
                : new Material(Shader.Find("Hidden/InternalErrorShader")!);
            mat.color = new Color(0.8f, 0.12f, 0.12f);
            AssetDatabase.CreateAsset(mat, matPath);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts   = path.Split('/');
            string   current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static T GetOrCreate<T>(string folder, string assetName) where T : ScriptableObject
        {
            string fullPath = string.Format("{0}/{1}.asset", folder, assetName);
            T?     existing = AssetDatabase.LoadAssetAtPath<T>(fullPath);
            if (existing != null) return existing;

            T instance = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(instance, fullPath);
            return instance;
        }
    }
}
