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

                GameObject character = (GameObject)PrefabUtility.InstantiatePrefab(
                    characterPrefab,
                    root.transform);
                character.name = CharacterRootName;
                character.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                character.transform.localScale = Vector3.one;

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
