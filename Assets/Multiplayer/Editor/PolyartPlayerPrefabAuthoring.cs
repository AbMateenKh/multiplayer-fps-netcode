using System;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Unity.FPS.Editor
{
    public static class PolyartPlayerPrefabAuthoring
    {
        const string PlayerPrefabPath = "Assets/Multiplayer/Prefabs/Player.prefab";
        const string CharacterPrefabPath =
            "Assets/SciFiWarriorPBRHPPolyart/Prefabs/PolyartCharacter.prefab";
        const string CharacterRootName = "CharacterVisual";
        static readonly Vector3 CharacterLocalPosition = new(0f, 0.35f, 0f);
        static readonly Vector3 CharacterLocalScale = Vector3.one * 0.75f;

        [MenuItem("Tools/Portfolio/Characters/Author Polyart Network Player Prefab")]
        public static void AuthorPlayerPrefab()
        {
            AnimatorController controller = PolyartAnimationSetup.BuildAnimationAssets();
            GameObject characterPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
            if (characterPrefab == null)
                throw new InvalidOperationException("Missing Polyart character prefab.");

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                RemoveExistingPresentation(root);
                CleanLegacyPresentationObjects(root);

                GameObject character = (GameObject)PrefabUtility.InstantiatePrefab(
                    characterPrefab,
                    root.transform);
                character.name = CharacterRootName;
                character.transform.SetLocalPositionAndRotation(
                    CharacterLocalPosition,
                    Quaternion.identity);
                character.transform.localScale = CharacterLocalScale;

                Animator animator = character.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                    throw new InvalidOperationException(
                        "PolyartCharacter must contain a valid Humanoid Animator and Avatar.");

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                Transform chest = FindRequiredChild(character.transform, "Chest");
                Transform rifle = FindRequiredChild(character.transform, "AssaultRifle");

                NetworkPlayerPresentation presentation =
                    root.AddComponent<NetworkPlayerPresentation>();
                presentation.CharacterRoot = character;
                presentation.CharacterAnimator = animator;
                presentation.AimTorso = chest;
                presentation.CharacterWeapon = rifle;

                ConfigureNetworkTransform(root);
                ValidateNetworkHierarchy(root, character);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[Polyart Authoring] Player.prefab now contains the Polyart character, " +
                "rifle, Humanoid Animator, controller, and network presentation references.");
        }

        [MenuItem("Tools/Portfolio/Characters/Clean Player Prefab Hierarchy")]
        public static void CleanPlayerPrefabHierarchy()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                CleanLegacyPresentationObjects(root);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                "[Polyart Authoring] Removed obsolete debug presentation objects " +
                "and normalized the retained jetpack VFX hierarchy.");
        }

        static void RemoveExistingPresentation(GameObject root)
        {
            Transform existingCharacter = root.transform.Find(CharacterRootName);
            if (existingCharacter != null)
            {
                UnityEngine.Object.DestroyImmediate(existingCharacter.gameObject);
            }

            AstronautPlayerVisual astronaut = root.GetComponent<AstronautPlayerVisual>();
            if (astronaut != null)
            {
                UnityEngine.Object.DestroyImmediate(astronaut);
            }

            NetworkPlayerPresentation existing = root.GetComponent<NetworkPlayerPresentation>();
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing);
            }
        }

        static void CleanLegacyPresentationObjects(GameObject root)
        {
            DestroyChild(root.transform, "Main Camera/Capsule (1)");
            DestroyChild(root.transform, "ShadowProjector");

            Transform jetpackVfx = root.transform.Find("Capsule");
            if (jetpackVfx == null)
                return;

            jetpackVfx.name = "JetpackVFX";
            RenameChild(jetpackVfx, "VFX_JetpackTrail_left", "JetpackTrailLeft");
            RenameChild(jetpackVfx, "VFX_JetpackTrail_right", "JetpackTrailRight");
        }

        static void ConfigureNetworkTransform(GameObject root)
        {
            NetworkTransform networkTransform = root.GetComponent<NetworkTransform>();
            if (networkTransform == null)
                throw new InvalidOperationException("Player root must contain NetworkTransform.");

            // Use one rate-aware interpolation layer for remote motion. A second
            // visual-child smoother causes repeated acceleration as snapshots arrive.
            networkTransform.PositionInterpolationType =
                NetworkTransform.InterpolationTypes.SmoothDampening;
            networkTransform.PositionLerpSmoothing = false;
            networkTransform.PositionMaxInterpolationTime = 0.06f;
            networkTransform.RotationInterpolationType =
                NetworkTransform.InterpolationTypes.Lerp;
            networkTransform.RotationLerpSmoothing = true;
            networkTransform.RotationMaxInterpolationTime = 0.04f;
            networkTransform.Interpolate = true;
            networkTransform.UseUnreliableDeltas = true;
        }

        static void DestroyChild(Transform root, string path)
        {
            Transform child = root.Find(path);
            if (child != null)
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        static void RenameChild(Transform root, string currentName, string newName)
        {
            Transform child = root.Find(currentName);
            if (child != null)
            {
                child.name = newName;
            }
        }

        static Transform FindRequiredChild(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                    return child;
            }

            throw new InvalidOperationException(
                $"Polyart character hierarchy is missing required child '{name}'.");
        }

        static void ValidateNetworkHierarchy(GameObject playerRoot, GameObject character)
        {
            if (playerRoot.GetComponent<NetworkObject>() == null)
                throw new InvalidOperationException("Player root must contain NetworkObject.");

            if (character.GetComponentInChildren<NetworkObject>(true) != null)
                throw new InvalidOperationException(
                    "The authored character must not contain a nested NetworkObject.");

            if (character.GetComponentInChildren<NetworkTransform>(true) != null)
                throw new InvalidOperationException(
                    "The authored character must not contain a nested NetworkTransform.");
        }
    }
}
