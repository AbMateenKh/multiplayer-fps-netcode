using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class WebGLLightingBaker
{
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const string LightingSettingsPath = "Assets/Scenes/MainSceneSettings.lighting";
    const string VolumeProfilePath = "Assets/Art/Generated/SpaceOutpost/SpaceOutpostVolume.asset";
    const string LightingRootName = "08_Lighting";
    const string ProbeGroupName = "Gameplay Light Probes";
    const string ReflectionProbeName = "Outpost Reflection Probe";

    [MenuItem("Tools/Portfolio/Lighting/Prepare WebGL Baked Lighting")]
    public static void PrepareMainScene()
    {
        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        PrepareCurrentSceneForBake(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[WebGL Lighting] Scene prepared for a low-cost hybrid bake.");
    }

    [MenuItem("Tools/Portfolio/Lighting/Bake WebGL Lighting")]
    public static void BakeMainScene()
    {
        PrepareMainScene();
        Lightmapping.bakeCompleted -= OnBakeCompleted;
        Lightmapping.bakeCompleted += OnBakeCompleted;

        if (!Lightmapping.BakeAsync())
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Debug.LogError("[WebGL Lighting] Unity could not start the lighting bake.");
            return;
        }

        Debug.Log("[WebGL Lighting] Bake started. Unity will report the generated texture budget when complete.");
    }

    public static void PrepareCurrentSceneForBake(Scene scene)
    {
        GameObject level = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Level");
        if (level == null)
        {
            throw new InvalidOperationException("MainScene has no Level root to prepare for lightmapping.");
        }

        EnsureSecondaryLightmapUvs(level);
        ConfigureLightingSettings();
        ConfigureStaticRenderers(level);

        Transform lightingRoot = FindOrCreateLightingRoot(level.transform);
        ConfigureLights(lightingRoot);
        ConfigureLightProbes(lightingRoot);
        ConfigureReflectionProbe(lightingRoot);
        ConfigureVolume(lightingRoot);
        ConfigureDynamicSceneRenderers(scene, level);

        Lightmapping.lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static void EnsureSecondaryLightmapUvs(GameObject level)
    {
        HashSet<string> modelPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (MeshFilter filter in level.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;

            string assetPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
            if (assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                modelPaths.Add(assetPath);
        }

        int updated = 0;
        foreach (string assetPath in modelPaths)
        {
            if (AssetImporter.GetAtPath(assetPath) is not ModelImporter importer || importer.generateSecondaryUV)
                continue;

            importer.generateSecondaryUV = true;
            importer.SaveAndReimport();
            updated++;
        }

        Debug.Log($"[WebGL Lighting] Verified lightmap UVs on {modelPaths.Count} FBX assets; reimported {updated}.");
    }

    static void ConfigureLightingSettings()
    {
        LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
        if (settings == null)
        {
            settings = new LightingSettings { name = "MainSceneSettings" };
            AssetDatabase.CreateAsset(settings, LightingSettingsPath);
        }

        settings.bakedGI = true;
        settings.realtimeGI = false;
        settings.realtimeEnvironmentLighting = false;
        settings.lightmapResolution = 6f;
        settings.lightmapMaxSize = 1024;
        settings.lightmapPadding = 2;
        settings.lightmapCompression = LightmapCompression.NormalQuality;
        settings.directionalityMode = LightmapsMode.NonDirectional;
        settings.mixedBakeMode = MixedLightingMode.Subtractive;
        settings.ao = true;
        settings.aoMaxDistance = 2f;
        settings.aoExponentIndirect = 1.2f;
        settings.aoExponentDirect = 0.35f;
        settings.directSampleCount = 32;
        settings.indirectSampleCount = 128;
        settings.environmentSampleCount = 64;
        settings.minBounces = 1;
        settings.maxBounces = 2;
        settings.lightProbeSampleCountMultiplier = 2f;

        EditorUtility.SetDirty(settings);
    }

    static void ConfigureStaticRenderers(GameObject level)
    {
        foreach (Renderer renderer in level.GetComponentsInChildren<Renderer>(true))
        {
            bool distantDecoration = renderer.shadowCastingMode == ShadowCastingMode.Off;
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
            if (distantDecoration)
            {
                flags &= ~StaticEditorFlags.ContributeGI;
                if (renderer is MeshRenderer distantMeshRenderer)
                    distantMeshRenderer.receiveGI = ReceiveGI.LightProbes;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            }
            else
            {
                flags |= StaticEditorFlags.ContributeGI |
                         StaticEditorFlags.BatchingStatic |
                         StaticEditorFlags.OccluderStatic |
                         StaticEditorFlags.OccludeeStatic |
                         StaticEditorFlags.ReflectionProbeStatic;
                if (renderer is MeshRenderer staticMeshRenderer)
                    staticMeshRenderer.receiveGI = ReceiveGI.Lightmaps;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                SetScaleInLightmap(renderer, CalculateLightmapScale(renderer.bounds));
            }

            renderer.reflectionProbeUsage = ReflectionProbeUsage.Simple;
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags);
            EditorUtility.SetDirty(renderer);
        }
    }

    static float CalculateLightmapScale(Bounds bounds)
    {
        float largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largestDimension >= 70f)
            return 0.06f;
        if (largestDimension >= 35f)
            return 0.12f;
        if (largestDimension >= 18f)
            return 0.25f;
        if (largestDimension >= 9f)
            return 0.5f;
        if (largestDimension <= 1f)
            return 0.25f;
        return 0.8f;
    }

    static void SetScaleInLightmap(Renderer renderer, float scale)
    {
        SerializedObject serializedRenderer = new(renderer);
        SerializedProperty property = serializedRenderer.FindProperty("m_ScaleInLightmap");
        if (property == null)
            return;

        property.floatValue = scale;
        serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
    }

    static Transform FindOrCreateLightingRoot(Transform level)
    {
        Transform root = level.Find(LightingRootName);
        if (root != null)
            return root;

        GameObject gameObject = new(LightingRootName);
        gameObject.transform.SetParent(level);
        return gameObject.transform;
    }

    static void ConfigureLights(Transform lightingRoot)
    {
        Light[] lights = lightingRoot.GetComponentsInChildren<Light>(true);
        Light sun = lights.FirstOrDefault(light => light.type == LightType.Directional);
        if (sun != null)
        {
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.shadows = LightShadows.Hard;
            sun.bounceIntensity = 1.15f;
            EditorUtility.SetDirty(sun);
        }

        foreach (Light light in lights)
        {
            if (light == sun)
                continue;

            light.lightmapBakeType = LightmapBakeType.Baked;
            light.shadows = LightShadows.None;
            light.bounceIntensity = 1f;
            EditorUtility.SetDirty(light);
        }
    }

    static void ConfigureLightProbes(Transform lightingRoot)
    {
        Transform existing = lightingRoot.Find(ProbeGroupName);
        GameObject probeObject = existing != null ? existing.gameObject : new GameObject(ProbeGroupName);
        probeObject.transform.SetParent(lightingRoot);
        probeObject.transform.localPosition = Vector3.zero;
        probeObject.transform.localRotation = Quaternion.identity;
        probeObject.transform.localScale = Vector3.one;

        LightProbeGroup group = probeObject.GetComponent<LightProbeGroup>();
        if (group == null)
            group = probeObject.AddComponent<LightProbeGroup>();

        List<Vector3> positions = new();
        float[] heights = { 1.25f, 4.5f };
        for (float x = -42f; x <= 42f; x += 12f)
        {
            for (float z = -32f; z <= 32f; z += 10.5f)
            {
                foreach (float height in heights)
                    positions.Add(new Vector3(x, height, z));
            }
        }

        positions.AddRange(new[]
        {
            new Vector3(0f, 1.25f, 0f),
            new Vector3(0f, 6.5f, 0f),
            new Vector3(-31f, 2.2f, 0f),
            new Vector3(31f, 2.2f, 0f),
            new Vector3(0f, 2.2f, 27f),
            new Vector3(0f, 2.2f, -27f)
        });

        group.probePositions = positions.ToArray();
        EditorUtility.SetDirty(group);
    }

    static void ConfigureReflectionProbe(Transform lightingRoot)
    {
        Transform existing = lightingRoot.Find(ReflectionProbeName);
        GameObject probeObject = existing != null ? existing.gameObject : new GameObject(ReflectionProbeName);
        probeObject.transform.SetParent(lightingRoot);
        probeObject.transform.localPosition = new Vector3(0f, 8f, 0f);
        probeObject.transform.localRotation = Quaternion.identity;

        ReflectionProbe probe = probeObject.GetComponent<ReflectionProbe>();
        if (probe == null)
            probe = probeObject.AddComponent<ReflectionProbe>();

        probe.mode = ReflectionProbeMode.Baked;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.size = new Vector3(92f, 24f, 72f);
        probe.center = Vector3.zero;
        probe.resolution = 64;
        probe.hdr = false;
        probe.intensity = 0.75f;
        probe.boxProjection = false;
        probe.importance = 1;
        EditorUtility.SetDirty(probe);
    }

    static void ConfigureVolume(Transform lightingRoot)
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "SpaceOutpostVolume";
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);
        }

        foreach (VolumeComponent component in profile.components.Where(component => component != null).ToArray())
            UnityEngine.Object.DestroyImmediate(component, true);
        profile.components.Clear();

        Bloom bloom = VolumeProfileFactory.CreateVolumeComponent<Bloom>(profile, true, false);
        bloom.intensity.Override(0.32f);
        bloom.threshold.Override(0.95f);
        bloom.scatter.Override(0.52f);

        Tonemapping tonemapping = VolumeProfileFactory.CreateVolumeComponent<Tonemapping>(profile, true, false);
        tonemapping.mode.Override(TonemappingMode.ACES);

        ColorAdjustments color = VolumeProfileFactory.CreateVolumeComponent<ColorAdjustments>(profile, true, false);
        color.postExposure.Override(0.55f);
        color.contrast.Override(6f);
        color.saturation.Override(16f);

        Vignette vignette = VolumeProfileFactory.CreateVolumeComponent<Vignette>(profile, true, false);
        vignette.intensity.Override(0.08f);
        vignette.smoothness.Override(0.35f);

        Volume volume = lightingRoot.GetComponentsInChildren<Volume>(true)
            .FirstOrDefault(candidate => candidate.isGlobal);
        if (volume == null)
        {
            GameObject volumeObject = new("Space Outpost Global Volume");
            volumeObject.transform.SetParent(lightingRoot);
            volume = volumeObject.AddComponent<Volume>();
        }

        volume.isGlobal = true;
        volume.priority = 20f;
        volume.sharedProfile = profile;
        profile.Reset();
        EditorUtility.SetDirty(profile);
        EditorUtility.SetDirty(volume);
    }

    static void ConfigureDynamicSceneRenderers(Scene scene, GameObject level)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == level)
                continue;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Simple;
                if (renderer is MeshRenderer meshRenderer)
                    meshRenderer.receiveGI = ReceiveGI.LightProbes;
                EditorUtility.SetDirty(renderer);
            }
        }
    }

    static void OnBakeCompleted()
    {
        Lightmapping.bakeCompleted -= OnBakeCompleted;
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        string sceneDirectory = Path.GetDirectoryName(MainScenePath)?.Replace('\\', '/');
        string sceneDataDirectory = $"{sceneDirectory}/{Path.GetFileNameWithoutExtension(MainScenePath)}";
        long bytes = 0;
        int textureCount = 0;
        if (Directory.Exists(sceneDataDirectory))
        {
            foreach (string path in Directory.GetFiles(sceneDataDirectory, "*", SearchOption.AllDirectories))
            {
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                bytes += new FileInfo(path).Length;
                textureCount++;
            }
        }

        Debug.Log($"[WebGL Lighting] Bake complete: {textureCount} generated files, " +
                  $"{bytes / (1024f * 1024f):0.00} MB source footprint. " +
                  "Run a WebGL build to measure final compressed download impact.");
    }
}
