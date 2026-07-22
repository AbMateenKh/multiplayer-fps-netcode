using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Unity.FPS.Editor
{
    public static class PolyartAnimationSetup
    {
        public const string ControllerPath =
            "Assets/Multiplayer/Art/Characters/Animation/PolyartNetwork.controller";

        const string UpperBodyMaskPath =
            "Assets/Multiplayer/Art/Characters/Animation/PolyartUpperBody.mask";
        const string WeaponClipFolder =
            "Assets/Multiplayer/Art/Characters/Animation/WeaponRig";
        const string SourceMaskPath =
            "Assets/SciFiWarriorPBRHPPolyart/Animations/AvatarMask.mask";
        const string AnimationFolder =
            "Assets/SciFiWarriorPBRHPPolyart/Animations";

        static readonly Dictionary<string, string> ClipFiles = new()
        {
            ["Idle"] = "Idle_Shoot_ar.fbx",
            ["WalkForward"] = "WalkFront_Shoot_ar.fbx",
            ["WalkBackward"] = "WalkBack_Shoot_ar.fbx",
            ["WalkLeft"] = "WalkLeft_Shoot_ar.fbx",
            ["WalkRight"] = "WalkRight_Shoot_ar.fbx",
            ["RunForward"] = "Run_gunMiddle_AR.fbx",
            ["Jump"] = "Jump.fbx",
            ["Shoot"] = "Shoot_SingleShot_AR.fbx",
            ["Reload"] = "Reload.fbx",
            ["Death"] = "Die.fbx",
        };

        static readonly HashSet<string> LoopingClips = new(StringComparer.Ordinal)
        {
            "Idle",
            "WalkForward",
            "WalkBackward",
            "WalkLeft",
            "WalkRight",
            "RunForward",
        };

        [MenuItem("Tools/Portfolio/Characters/Build Polyart Network Animator")]
        public static AnimatorController BuildAnimationAssets()
        {
            EnsureOutputFolder();
            ConfigureImporters();

            Dictionary<string, AnimationClip> clips = ClipFiles.ToDictionary(
                pair => pair.Key,
                pair => LoadClip(AnimationFolder + "/" + pair.Value),
                StringComparer.Ordinal);
            Dictionary<string, AnimationClip> weaponClips = ClipFiles.Keys.ToDictionary(
                key => key,
                key => AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    WeaponClipFolder + "/" + key + "_WeaponRig.anim") ??
                    throw new InvalidOperationException(
                        $"Missing authored Polyart weapon-rig clip for '{key}'."),
                StringComparer.Ordinal);

            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller =
                AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Reload", AnimatorControllerParameterType.Trigger);

            BuildBaseLayer(controller, clips);
            BuildActionLayer(controller, clips, BuildUpperBodyMask());
            BuildWeaponRigLayer(controller, weaponClips);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        static void BuildBaseLayer(
            AnimatorController controller,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            machine.name = "Full Body";

            BlendTree locomotionTree = new()
            {
                name = "Directional Armed Locomotion",
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(locomotionTree, controller);

            locomotionTree.AddChild(clips["Idle"], Vector2.zero);
            locomotionTree.AddChild(clips["WalkForward"], new Vector2(0f, 0.45f));
            locomotionTree.AddChild(clips["RunForward"], new Vector2(0f, 1f));
            locomotionTree.AddChild(clips["WalkBackward"], new Vector2(0f, -0.7f));
            locomotionTree.AddChild(clips["WalkLeft"], new Vector2(-0.7f, 0f));
            locomotionTree.AddChild(clips["WalkRight"], new Vector2(0.7f, 0f));

            AnimatorState locomotion = AddState(machine, "Locomotion", locomotionTree);
            AnimatorState jump = AddState(machine, "Jump", clips["Jump"]);
            AnimatorState death = AddState(machine, "Death", clips["Death"]);
            death.speed = 0.9f;
            machine.defaultState = locomotion;

            AnimatorStateTransition jumpTransition = machine.AddAnyStateTransition(jump);
            ConfigureImmediateTransition(jumpTransition, 0.06f);
            jumpTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            jumpTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

            AnimatorStateTransition landTransition = jump.AddTransition(locomotion);
            ConfigureImmediateTransition(landTransition, 0.08f);
            landTransition.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            AnimatorStateTransition deathTransition = machine.AddAnyStateTransition(death);
            ConfigureImmediateTransition(deathTransition, 0.05f);
            deathTransition.AddCondition(AnimatorConditionMode.If, 0f, "Dead");

            AnimatorStateTransition respawnTransition = death.AddTransition(locomotion);
            ConfigureImmediateTransition(respawnTransition, 0.1f);
            respawnTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
        }

        static void BuildActionLayer(
            AnimatorController controller,
            IReadOnlyDictionary<string, AnimationClip> clips,
            AvatarMask mask)
        {
            AnimatorStateMachine machine = new() { name = "Upper Body Actions" };
            AssetDatabase.AddObjectToAsset(machine, controller);

            AnimatorControllerLayer layer = new()
            {
                name = "Upper Body Actions",
                defaultWeight = 1f,
                avatarMask = mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = machine,
            };

            AnimatorState ready = AddState(machine, "Ready", null);
            AnimatorState shoot = AddState(machine, "Shoot", clips["Shoot"]);
            AnimatorState reload = AddState(machine, "Reload", clips["Reload"]);
            shoot.speed = 1.35f;
            machine.defaultState = ready;

            AnimatorStateTransition shootTransition = machine.AddAnyStateTransition(shoot);
            ConfigureImmediateTransition(shootTransition, 0.025f);
            shootTransition.AddCondition(AnimatorConditionMode.If, 0f, "Shoot");
            shootTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            AddExitTransition(shoot, ready, 0.88f, 0.05f);

            AnimatorStateTransition reloadTransition = machine.AddAnyStateTransition(reload);
            ConfigureImmediateTransition(reloadTransition, 0.05f);
            reloadTransition.AddCondition(AnimatorConditionMode.If, 0f, "Reload");
            reloadTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            AddExitTransition(reload, ready, 0.96f, 0.08f);

            AnimatorControllerLayer[] layers = controller.layers;
            Array.Resize(ref layers, layers.Length + 1);
            layers[^1] = layer;
            controller.layers = layers;
        }

        static void BuildWeaponRigLayer(
            AnimatorController controller,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            AnimatorStateMachine machine = new() { name = "Weapon Rig" };
            AssetDatabase.AddObjectToAsset(machine, controller);

            BlendTree locomotionTree = new()
            {
                name = "Weapon Rig Locomotion",
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(locomotionTree, controller);

            locomotionTree.AddChild(clips["Idle"], Vector2.zero);
            locomotionTree.AddChild(clips["WalkForward"], new Vector2(0f, 0.45f));
            locomotionTree.AddChild(clips["RunForward"], new Vector2(0f, 1f));
            locomotionTree.AddChild(clips["WalkBackward"], new Vector2(0f, -0.7f));
            locomotionTree.AddChild(clips["WalkLeft"], new Vector2(-0.7f, 0f));
            locomotionTree.AddChild(clips["WalkRight"], new Vector2(0.7f, 0f));

            AnimatorState locomotion = AddState(machine, "Locomotion", locomotionTree);
            AnimatorState jump = AddState(machine, "Jump", clips["Jump"]);
            AnimatorState death = AddState(machine, "Death", clips["Death"]);
            AnimatorState shoot = AddState(machine, "Shoot", clips["Shoot"]);
            AnimatorState reload = AddState(machine, "Reload", clips["Reload"]);
            shoot.speed = 1.35f;
            death.speed = 0.9f;
            machine.defaultState = locomotion;

            AnimatorStateTransition jumpTransition = machine.AddAnyStateTransition(jump);
            ConfigureImmediateTransition(jumpTransition, 0.06f);
            jumpTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            jumpTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

            AnimatorStateTransition landTransition = jump.AddTransition(locomotion);
            ConfigureImmediateTransition(landTransition, 0.08f);
            landTransition.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            AnimatorStateTransition deathTransition = machine.AddAnyStateTransition(death);
            ConfigureImmediateTransition(deathTransition, 0.05f);
            deathTransition.AddCondition(AnimatorConditionMode.If, 0f, "Dead");

            AnimatorStateTransition respawnTransition = death.AddTransition(locomotion);
            ConfigureImmediateTransition(respawnTransition, 0.1f);
            respawnTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

            AnimatorStateTransition shootTransition = machine.AddAnyStateTransition(shoot);
            ConfigureImmediateTransition(shootTransition, 0.025f);
            shootTransition.AddCondition(AnimatorConditionMode.If, 0f, "Shoot");
            shootTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            AddExitTransition(shoot, locomotion, 0.88f, 0.05f);

            AnimatorStateTransition reloadTransition = machine.AddAnyStateTransition(reload);
            ConfigureImmediateTransition(reloadTransition, 0.05f);
            reloadTransition.AddCondition(AnimatorConditionMode.If, 0f, "Reload");
            reloadTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            AddExitTransition(reload, locomotion, 0.96f, 0.08f);

            AnimatorControllerLayer layer = new()
            {
                name = "Weapon Rig",
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = machine,
            };

            AnimatorControllerLayer[] layers = controller.layers;
            Array.Resize(ref layers, layers.Length + 1);
            layers[^1] = layer;
            controller.layers = layers;
        }

        static AvatarMask BuildUpperBodyMask()
        {
            AssetDatabase.DeleteAsset(UpperBodyMaskPath);
            AvatarMask source = AssetDatabase.LoadAssetAtPath<AvatarMask>(SourceMaskPath);
            if (source == null)
                throw new InvalidOperationException("Missing Polyart source AvatarMask.");

            AvatarMask mask = UnityEngine.Object.Instantiate(source);
            mask.name = "PolyartUpperBody";

            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);

            for (int i = 0; i < mask.transformCount; i++)
            {
                string path = mask.GetTransformPath(i);
                bool lowerBody = path.StartsWith("Hips/UpperLeg_", StringComparison.Ordinal);
                if (lowerBody)
                {
                    mask.SetTransformActive(i, false);
                }
            }

            AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            return mask;
        }

        static void ConfigureImporters()
        {
            foreach ((string key, string file) in ClipFiles)
            {
                string path = AnimationFolder + "/" + file;
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                    throw new InvalidOperationException("Missing animation importer: " + path);

                ModelImporterClipAnimation[] animations = importer.defaultClipAnimations;
                if (animations == null || animations.Length == 0)
                    throw new InvalidOperationException("No animation clip found in: " + path);

                bool loops = LoopingClips.Contains(key);
                foreach (ModelImporterClipAnimation animation in animations)
                {
                    animation.loopTime = loops;
                    animation.loopPose = loops;
                    animation.lockRootRotation = true;
                    animation.lockRootHeightY = true;
                    animation.lockRootPositionXZ = true;
                    animation.keepOriginalOrientation = true;
                    animation.keepOriginalPositionY = true;
                    animation.keepOriginalPositionXZ = true;
                }

                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar = null;
                importer.importAnimation = true;
                importer.clipAnimations = animations;
                importer.SaveAndReimport();
            }
        }

        static AnimationClip LoadClip(string path)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate =>
                    !candidate.name.StartsWith("__preview__", StringComparison.Ordinal));

            return clip != null
                ? clip
                : throw new InvalidOperationException("No usable clip found in: " + path);
        }

        static AnimatorState AddState(
            AnimatorStateMachine machine,
            string name,
            UnityEngine.Motion motion)
        {
            AnimatorState state = machine.AddState(name);
            state.motion = motion;
            state.writeDefaultValues = false;
            return state;
        }

        static void AddExitTransition(
            AnimatorState source,
            AnimatorState destination,
            float exitTime,
            float duration)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
        }

        static void ConfigureImmediateTransition(
            AnimatorStateTransition transition,
            float duration)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
        }

        static void EnsureOutputFolder()
        {
            const string parent = "Assets/Multiplayer/Art/Characters";
            if (!AssetDatabase.IsValidFolder(parent + "/Animation"))
            {
                AssetDatabase.CreateFolder(parent, "Animation");
            }
        }

    }
}
