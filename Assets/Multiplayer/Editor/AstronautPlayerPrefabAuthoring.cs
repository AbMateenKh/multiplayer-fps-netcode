using System;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Unity.FPS.Editor
{
    public static class AstronautPlayerPrefabAuthoring
    {
        const string k_PlayerPrefabPath = "Assets/Multiplayer/Prefabs/Player.prefab";
        const string k_CharacterModelPath =
            "Assets/Art/Ultimate Space Kit - March 2023/Characters/FBX/" +
            "Astronaut_RaeTheRedPanda.fbx";
        const string k_ControllerPath =
            "Assets/Multiplayer/Art/Characters/Animation/AstronautThirdPerson.controller";
        const string k_MaterialPath =
            "Assets/Multiplayer/Art/Characters/AstronautAtlas.mat";
        const string k_CharacterRootName = "CharacterVisual";

        [InitializeOnLoadMethod]
        static void QueuePrefabMigration()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_PlayerPrefabPath);
            AstronautPlayerVisual visual = prefab != null
                ? prefab.GetComponent<AstronautPlayerVisual>()
                : null;
            if (visual == null || visual.CharacterRoot != null)
                return;

            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    AuthorPlayerPrefab();
                }
            };
        }

        [MenuItem("Tools/Portfolio/Characters/Author Complete Player Prefab")]
        public static void AuthorPlayerPrefab()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(k_CharacterModelPath);
            RuntimeAnimatorController controller =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(k_ControllerPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
            AuthorPlayerPrefab(model, controller, material);
        }

        public static void AuthorPlayerPrefab(
            GameObject model,
            RuntimeAnimatorController controller,
            Material material)
        {
            if (model == null)
                throw new InvalidOperationException($"Missing character model: {k_CharacterModelPath}");
            if (controller == null)
                throw new InvalidOperationException($"Missing animation controller: {k_ControllerPath}");

            GameObject root = PrefabUtility.LoadPrefabContents(k_PlayerPrefabPath);
            try
            {
                AstronautPlayerVisual visual = root.GetComponent<AstronautPlayerVisual>();
                if (visual == null)
                {
                    visual = root.AddComponent<AstronautPlayerVisual>();
                }

                Transform existing = root.transform.Find(k_CharacterRootName);
                if (existing != null)
                {
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);
                }

                GameObject character =
                    PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
                if (character == null)
                    throw new InvalidOperationException("Could not add the astronaut model to Player.prefab.");

                character.name = k_CharacterRootName;
                // Player, camera, and first-person weapon all use local +Z as forward.
                character.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                character.transform.localScale = Vector3.one * 0.62f;

                Animator animator = character.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = character.AddComponent<Animator>();
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                Transform packageWeapon = FindDescendant(character.transform, "Pistol");
                if (packageWeapon == null)
                    throw new InvalidOperationException("The authored astronaut has no package Pistol mesh.");

                if (material != null)
                {
                    foreach (Renderer renderer in character.GetComponentsInChildren<Renderer>(true))
                    {
                        // The pistol has its own package material. Applying the astronaut
                        // atlas to it makes the held weapon look broken.
                        if (renderer.transform == packageWeapon ||
                            renderer.transform.IsChildOf(packageWeapon))
                            continue;

                        Material[] materials = renderer.sharedMaterials;
                        Array.Fill(materials, material);
                        renderer.sharedMaterials = materials;
                    }
                }

                visual.CharacterRoot = character;
                visual.CharacterAnimator = animator;
                visual.AimTorso = FindDescendant(character.transform, "Torso");
                visual.CharacterWeapon = packageWeapon;
                if (visual.AimTorso == null)
                    throw new InvalidOperationException("The authored astronaut has no Torso bone.");

                EditorUtility.SetDirty(animator);
                EditorUtility.SetDirty(visual);
                PrefabUtility.SaveAsPrefabAsset(root, k_PlayerPrefabPath);
                AssetDatabase.SaveAssets();

                Debug.Log(
                    "[Astronaut Authoring] Player.prefab now contains its model, Animator, " +
                    "controller, material, and aim-bone references. Runtime model creation is disabled.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Transform FindDescendant(Transform root, string targetName)
        {
            foreach (Transform child in root)
            {
                if (child.name == targetName)
                    return child;

                Transform match = FindDescendant(child, targetName);
                if (match != null)
                    return match;
            }

            return null;
        }
    }
}
