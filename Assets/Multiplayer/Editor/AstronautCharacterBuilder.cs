using System.Collections.Generic;
using System.Linq;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.FPS.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class AstronautCharacterBuilder
{
    const string KitRoot = "Assets/Art/Ultimate Space Kit - March 2023";
    const string CharacterFbxRoot = KitRoot + "/Characters/FBX";
    const string GeneratedRoot = "Assets/Multiplayer/Art/Characters";
    const string MaterialPath = GeneratedRoot + "/AstronautAtlas.mat";
    const string SuitMaterialPath = GeneratedRoot + "/AstronautSuit.mat";
    const string GloveMaterialPath = GeneratedRoot + "/AstronautGlove.mat";
    const string AccentMaterialPath = GeneratedRoot + "/AstronautAccent.mat";
    const string ControllerPath = GeneratedRoot + "/Astronaut.controller";
    const string ArmsMeshPath = GeneratedRoot + "/AstronautArms.asset";
    const string ViewModelPath = GeneratedRoot + "/AstronautViewModel.prefab";
    const string BlasterPrefabPath = "Assets/FPS/Prefabs/Weapons/Weapon_Blaster.prefab";

    static readonly string[] CharacterPaths =
    {
        CharacterFbxRoot + "/Astronaut_RaeTheRedPanda.fbx",
        CharacterFbxRoot + "/Astronaut_FinnTheFrog.fbx",
        CharacterFbxRoot + "/Astronaut_FernandoTheFlamingo.fbx",
        CharacterFbxRoot + "/Astronaut_BarbaraTheBee.fbx"
    };

    [MenuItem("Tools/Portfolio/Build Astronaut Characters")]
    public static void Build()
    {
        EnsureFolder("Assets/Multiplayer/Art");
        EnsureFolder(GeneratedRoot);

        Material material = BuildMaterial();
        Material suitMaterial = BuildSolidMaterial(
            SuitMaterialPath,
            new Color32(224, 218, 205, 255),
            0.18f);
        Material gloveMaterial = BuildSolidMaterial(
            GloveMaterialPath,
            new Color32(34, 39, 43, 255),
            0.24f);
        Material accentMaterial = BuildSolidMaterial(
            AccentMaterialPath,
            new Color32(239, 111, 42, 255),
            0.22f);
        AssetDatabase.DeleteAsset(ControllerPath);
        GameObject viewModel =
            BuildViewModel(suitMaterial, gloveMaterial, accentMaterial);
        AstronautPlayerPrefabAuthoring.AuthorPlayerPrefab();
        ConfigureBlasterPrefab(viewModel);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Astronaut Builder] Characters, animations, arms, and pulse blaster visuals built.");
    }

    static Material BuildMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader)
            {
                name = "AstronautAtlas"
            };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(
            KitRoot + "/Characters/Blends/Atlas.png");
        material.SetTexture("_BaseMap", atlas);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.18f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material BuildSolidMaterial(string path, Color color, float smoothness)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    static AnimatorController BuildAnimatorController()
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

        Dictionary<string, AnimationClip> clips = AssetDatabase
            .LoadAllAssetsAtPath(CharacterPaths[0])
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__"))
            .ToDictionary(clip => clip.name, clip => clip);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState idle = AddState(stateMachine, "Idle_Gun", clips);
        AnimatorState walk = AddState(stateMachine, "Walk_Gun", clips);
        AnimatorState run = AddState(stateMachine, "Run_Gun", clips);
        AnimatorState jump = AddState(stateMachine, "Jump_Idle", clips);
        AnimatorState shoot = AddState(stateMachine, "Weapon", clips);
        AnimatorState hit = AddState(stateMachine, "HitReact", clips);
        AnimatorState death = AddState(stateMachine, "Death", clips);
        stateMachine.defaultState = idle;

        AddConditionTransition(idle, walk, "Speed", AnimatorConditionMode.Greater, 0.15f);
        AddConditionTransition(walk, idle, "Speed", AnimatorConditionMode.Less, 0.15f);
        AddConditionTransition(walk, run, "Speed", AnimatorConditionMode.Greater, 5f);
        AddConditionTransition(run, walk, "Speed", AnimatorConditionMode.Less, 5f);

        AnimatorStateTransition jumpTransition = stateMachine.AddAnyStateTransition(jump);
        ConfigureTransition(jumpTransition);
        jumpTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
        AnimatorStateTransition landTransition = jump.AddTransition(idle);
        ConfigureTransition(landTransition);
        landTransition.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

        AddTriggerTransition(stateMachine, shoot, "Shoot");
        AddExitTransition(shoot, idle, 0.88f);
        AddTriggerTransition(stateMachine, hit, "Hit");
        AddExitTransition(hit, idle, 0.88f);

        AnimatorStateTransition deathTransition = stateMachine.AddAnyStateTransition(death);
        ConfigureTransition(deathTransition, 0.08f);
        deathTransition.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
        AnimatorStateTransition reviveTransition = death.AddTransition(idle);
        ConfigureTransition(reviveTransition, 0.08f);
        reviveTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

        EditorUtility.SetDirty(controller);
        return controller;
    }

    static AnimatorState AddState(
        AnimatorStateMachine stateMachine,
        string clipName,
        IReadOnlyDictionary<string, AnimationClip> clips)
    {
        AnimatorState state = stateMachine.AddState(clipName);
        if (clips.TryGetValue(clipName, out AnimationClip clip))
        {
            state.motion = clip;
        }
        else
        {
            Debug.LogWarning($"[Astronaut Builder] Missing animation clip: {clipName}");
        }

        return state;
    }

    static void AddConditionTransition(
        AnimatorState source,
        AnimatorState destination,
        string parameter,
        AnimatorConditionMode mode,
        float threshold)
    {
        AnimatorStateTransition transition = source.AddTransition(destination);
        ConfigureTransition(transition);
        transition.AddCondition(mode, threshold, parameter);
    }

    static void AddTriggerTransition(
        AnimatorStateMachine stateMachine,
        AnimatorState destination,
        string parameter)
    {
        AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(destination);
        ConfigureTransition(transition, 0.06f);
        transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
    }

    static void AddExitTransition(AnimatorState source, AnimatorState destination, float exitTime)
    {
        AnimatorStateTransition transition = source.AddTransition(destination);
        transition.hasExitTime = true;
        transition.exitTime = exitTime;
        transition.hasFixedDuration = true;
        transition.duration = 0.08f;
    }

    static void ConfigureTransition(AnimatorStateTransition transition, float duration = 0.12f)
    {
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = duration;
        transition.canTransitionToSelf = false;
    }

    static GameObject BuildViewModel(
        Material suitMaterial,
        Material gloveMaterial,
        Material accentMaterial)
    {
        AssetDatabase.DeleteAsset(ViewModelPath);
        AssetDatabase.DeleteAsset(ArmsMeshPath);

        GameObject instance = new("AstronautViewModel");
        CreateLimb(
            instance.transform,
            "RightForearm",
            PrimitiveType.Capsule,
            new Vector3(0.27f, -0.23f, 0.32f),
            new Vector3(58f, 0f, -18f),
            new Vector3(0.1f, 0.24f, 0.1f),
            suitMaterial);
        CreateLimb(
            instance.transform,
            "LeftForearm",
            PrimitiveType.Capsule,
            new Vector3(-0.06f, -0.22f, 0.34f),
            new Vector3(62f, 0f, 20f),
            new Vector3(0.1f, 0.23f, 0.1f),
            suitMaterial);
        CreateLimb(
            instance.transform,
            "RightGlove",
            PrimitiveType.Capsule,
            new Vector3(0.18f, -0.08f, 0.48f),
            new Vector3(75f, 0f, -8f),
            new Vector3(0.105f, 0.075f, 0.12f),
            gloveMaterial);
        CreateLimb(
            instance.transform,
            "LeftGlove",
            PrimitiveType.Capsule,
            new Vector3(0.02f, -0.08f, 0.5f),
            new Vector3(75f, 0f, 12f),
            new Vector3(0.1f, 0.07f, 0.115f),
            gloveMaterial);
        CreateLimb(
            instance.transform,
            "RightCuff",
            PrimitiveType.Cylinder,
            new Vector3(0.24f, -0.16f, 0.4f),
            new Vector3(58f, 0f, -18f),
            new Vector3(0.115f, 0.035f, 0.115f),
            accentMaterial);
        CreateLimb(
            instance.transform,
            "LeftCuff",
            PrimitiveType.Cylinder,
            new Vector3(-0.01f, -0.16f, 0.42f),
            new Vector3(62f, 0f, 20f),
            new Vector3(0.115f, 0.035f, 0.115f),
            accentMaterial);

        instance.AddComponent<AstronautWeaponViewModel>();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, ViewModelPath);
        Object.DestroyImmediate(instance);
        return prefab;
    }

    static void CreateLimb(
        Transform parent,
        string name,
        PrimitiveType primitive,
        Vector3 position,
        Vector3 rotation,
        Vector3 scale,
        Material material)
    {
        GameObject limb = GameObject.CreatePrimitive(primitive);
        limb.name = name;
        limb.transform.SetParent(parent, false);
        limb.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(rotation));
        limb.transform.localScale = scale;
        Object.DestroyImmediate(limb.GetComponent<Collider>());
        Renderer renderer = limb.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        ConfigureViewModelRenderer(renderer);
    }

    static void ConfigureViewModelRenderer(Renderer renderer)
    {
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static void ConfigureBlasterPrefab(GameObject viewModelPrefab)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BlasterPrefabPath);
        try
        {
            Transform legacyVisual = FindDeepChild(root.transform, "WeaponMesh_Pistol");
            if (legacyVisual != null)
            {
                legacyVisual.gameObject.SetActive(true);
            }

            Transform gunRoot = FindDeepChild(root.transform, "GunRoot");
            Transform existing = FindDeepChild(root.transform, "AstronautViewModel");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            GameObject viewModel =
                PrefabUtility.InstantiatePrefab(viewModelPrefab, gunRoot) as GameObject;
            viewModel.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            viewModel.transform.localScale = Vector3.one;

            WeaponController weapon = root.GetComponent<WeaponController>();
            weapon.WeaponName = "Pulse Blaster";
            EditorUtility.SetDirty(weapon);
            PrefabUtility.SaveAsPrefabAsset(root, BlasterPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        string folderName = path.Substring(slash + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}
