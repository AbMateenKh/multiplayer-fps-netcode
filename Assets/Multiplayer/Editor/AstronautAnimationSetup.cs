using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Unity.FPS.Editor
{
    public static class AstronautAnimationSetup
    {
        const string k_CharacterFolder =
            "Assets/Art/Ultimate Space Kit - March 2023/Characters/FBX";
        const string k_SourceModel = k_CharacterFolder + "/Astronaut_FinnTheFrog.fbx";
        const string k_OutputFolder = "Assets/Multiplayer/Art/Characters/Animation";
        const string k_ControllerPath = k_OutputFolder + "/AstronautThirdPerson.controller";
        const string k_PlayerPrefabPath = "Assets/Multiplayer/Prefabs/Player.prefab";

        static readonly string[] k_AstronautModels =
        {
            "Astronaut_BarbaraTheBee.fbx",
            "Astronaut_FernandoTheFlamingo.fbx",
            "Astronaut_FinnTheFrog.fbx",
            "Astronaut_RaeTheRedPanda.fbx",
        };

        static readonly HashSet<string> k_LoopingClips = new(StringComparer.Ordinal)
        {
            "Idle",
            "Idle_Gun",
            "Walk",
            "Walk_Gun",
            "Run",
            "Run_Gun",
            "Jump_Idle",
        };

        // This one-time bootstrap lets the open Editor generate native controller assets
        // after the script compiles without taking focus from another application.
        [InitializeOnLoadMethod]
        static void QueueMissingControllerBuild()
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(k_ControllerPath) != null)
                return;

            EditorApplication.delayCall += BuildAnimationAssets;
        }

        [MenuItem("Tools/Portfolio/Characters/Build Astronaut Animation Controller")]
        public static void BuildAnimationAssets()
        {
            try
            {
                EnsureOutputFolder();
                ConfigureModelClips();

                Dictionary<string, AnimationClip> clips = LoadSourceClips();
                ValidateRequiredClips(clips);

                AnimatorController controller = BuildController(clips);
                AssignControllerToPlayerPrefab(controller);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log(
                    $"[Astronaut Animation] Built {controller.name} with {clips.Count} embedded clips " +
                    "and assigned it to the network player prefab.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        static void EnsureOutputFolder()
        {
            string current = "Assets/Multiplayer/Art/Characters";
            if (!AssetDatabase.IsValidFolder(current + "/Animation"))
            {
                AssetDatabase.CreateFolder(current, "Animation");
            }
        }

        static void ConfigureModelClips()
        {
            foreach (string modelName in k_AstronautModels)
            {
                string path = k_CharacterFolder + "/" + modelName;
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                    throw new InvalidOperationException($"Missing astronaut model importer: {path}");

                ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
                if (clips == null || clips.Length == 0)
                    throw new InvalidOperationException($"No embedded animation clips found in {path}");

                foreach (ModelImporterClipAnimation clip in clips)
                {
                    string normalizedName = NormalizeClipName(
                        string.IsNullOrEmpty(clip.takeName) ? clip.name : clip.takeName);
                    bool loops = k_LoopingClips.Contains(normalizedName);
                    clip.loopTime = loops;
                    clip.loopPose = loops;
                    clip.lockRootRotation = true;
                    clip.lockRootHeightY = true;
                    clip.lockRootPositionXZ = true;
                    clip.keepOriginalOrientation = false;
                    clip.keepOriginalPositionY = false;
                    clip.keepOriginalPositionXZ = false;
                }

                importer.importAnimation = true;
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }

        static Dictionary<string, AnimationClip> LoadSourceClips()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(k_SourceModel);
            Dictionary<string, AnimationClip> clips = assets
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                .GroupBy(clip => NormalizeClipName(clip.name), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            if (clips.Count == 0)
            {
                string importedAssets = string.Join(
                    ", ",
                    assets.Select(asset => $"{asset.GetType().Name}:{asset.name}"));
                throw new InvalidOperationException(
                    $"No usable AnimationClip subassets were loaded from {k_SourceModel}. " +
                    $"Imported assets: {importedAssets}");
            }

            return clips;
        }

        static string NormalizeClipName(string name)
        {
            int separator = name.LastIndexOf('|');
            return separator >= 0 ? name[(separator + 1)..] : name;
        }

        static void ValidateRequiredClips(IReadOnlyDictionary<string, AnimationClip> clips)
        {
            string[] required =
            {
                "Idle_Gun",
                "Walk_Gun",
                "Run_Gun",
                "Jump",
                "Jump_Idle",
                "Jump_Land",
                "Run_Gun_Shoot",
                "HitReact",
                "Death",
            };

            string[] missing = required.Where(name => !clips.ContainsKey(name)).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    "Astronaut FBX is missing required clips: " + string.Join(", ", missing) +
                    ". Available clips: " + string.Join(", ", clips.Keys.OrderBy(name => name)));
            }
        }

        static AnimatorController BuildController(IReadOnlyDictionary<string, AnimationClip> clips)
        {
            AssetDatabase.DeleteAsset(k_ControllerPath);
            AnimatorController controller =
                AnimatorController.CreateAnimatorControllerAtPath(k_ControllerPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            stateMachine.name = "Astronaut Locomotion";

            BlendTree locomotionTree = new()
            {
                name = "Armed Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(locomotionTree, controller);
            locomotionTree.AddChild(clips["Idle_Gun"], 0f);
            locomotionTree.AddChild(clips["Walk_Gun"], 1.8f);
            locomotionTree.AddChild(clips["Run_Gun"], 5.5f);

            AnimatorState locomotion = AddState(stateMachine, "Locomotion", locomotionTree, 0f, 0f);
            AnimatorState jump = AddState(stateMachine, "Jump", clips["Jump"], 240f, 0f);
            AnimatorState airborne = AddState(stateMachine, "Airborne", clips["Jump_Idle"], 480f, 0f);
            AnimatorState land = AddState(stateMachine, "Land", clips["Jump_Land"], 720f, 0f);
            AnimatorState shoot = AddState(stateMachine, "Shoot", clips["Run_Gun_Shoot"], 0f, 120f);
            AnimatorState hit = AddState(stateMachine, "Hit", clips["HitReact"], 240f, 120f);
            AnimatorState death = AddState(stateMachine, "Death", clips["Death"], 480f, 120f);
            death.speed = 0.85f;
            stateMachine.defaultState = locomotion;

            AddConditionTransition(locomotion, jump, "Grounded", AnimatorConditionMode.IfNot, 0f, 0.05f);
            AddExitTransition(jump, airborne, 0.82f, 0.04f);
            AddConditionTransition(airborne, land, "Grounded", AnimatorConditionMode.If, 0f, 0.04f);
            AddExitTransition(land, locomotion, 0.82f, 0.08f);

            AnimatorStateTransition shootTransition = AddAnyStateTrigger(
                stateMachine,
                shoot,
                "Shoot",
                0.03f);
            shootTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            AddExitTransition(shoot, locomotion, 0.88f, 0.08f);

            AnimatorStateTransition hitTransition = AddAnyStateTrigger(
                stateMachine,
                hit,
                "Hit",
                0.03f);
            hitTransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            AddExitTransition(hit, locomotion, 0.9f, 0.08f);

            AnimatorStateTransition deathTransition = stateMachine.AddAnyStateTransition(death);
            ConfigureImmediateTransition(deathTransition, 0.04f);
            deathTransition.AddCondition(AnimatorConditionMode.If, 0f, "Dead");

            AddConditionTransition(death, locomotion, "Dead", AnimatorConditionMode.IfNot, 0f, 0.08f);

            EditorUtility.SetDirty(locomotionTree);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static AnimatorState AddState(
            AnimatorStateMachine stateMachine,
            string name,
            UnityEngine.Motion motion,
            float x,
            float y)
        {
            AnimatorState state = stateMachine.AddState(name, new Vector3(x, y));
            state.motion = motion;
            state.writeDefaultValues = false;
            return state;
        }

        static void AddConditionTransition(
            AnimatorState source,
            AnimatorState destination,
            string parameter,
            AnimatorConditionMode mode,
            float threshold,
            float duration)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            ConfigureImmediateTransition(transition, duration);
            transition.AddCondition(mode, threshold, parameter);
        }

        static AnimatorStateTransition AddAnyStateTrigger(
            AnimatorStateMachine stateMachine,
            AnimatorState destination,
            string trigger,
            float duration)
        {
            AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(destination);
            ConfigureImmediateTransition(transition, duration);
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            return transition;
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

        static void ConfigureImmediateTransition(AnimatorStateTransition transition, float duration)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
            transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        }

        static void AssignControllerToPlayerPrefab(RuntimeAnimatorController controller)
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(k_PlayerPrefabPath);
            try
            {
                AstronautPlayerVisual visual =
                    prefabRoot.GetComponent<AstronautPlayerVisual>();
                if (visual == null)
                {
                    throw new InvalidOperationException(
                        $"{k_PlayerPrefabPath} has no {nameof(AstronautPlayerVisual)} component.");
                }

                visual.AnimationController = controller;
                EditorUtility.SetDirty(visual);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, k_PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }
}
