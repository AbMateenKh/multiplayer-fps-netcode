using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FPS.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class SpaceOutpostArenaBuilder
{
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const string BackupScenePath = "Assets/Scenes/MainScene_PrototypeArena.unity";
    const string GeneratedRoot = "Assets/Art/Generated/SpaceOutpost";
    const string MaterialRoot = GeneratedRoot + "/Materials";
    const string SpaceKitRoot = "Assets/Art/Ultimate Space Kit - March 2023";
    const string EnvironmentFbxRoot = SpaceKitRoot + "/Environment/FBX";
    const string VehicleFbxRoot = SpaceKitRoot + "/Vehicles/FBX";

    static readonly Color Charcoal = Hex("29383B");
    static readonly Color CharcoalLight = Hex("394B4E");
    static readonly Color HabitatWhite = Hex("B9C1BD");
    static readonly Color HabitatDark = Hex("48575A");
    static readonly Color SignalOrange = Hex("F26A2E");
    static readonly Color SignalCyan = Hex("3DD9D1");
    static readonly Color FloraPurple = Hex("4E3657");
    static readonly Color GroundOrange = Hex("A84A2C");

    static Transform s_ArenaRoot;
    static Material s_Ground;
    static Material s_GroundAccent;
    static Material s_GardenDeck;
    static Material s_Habitat;
    static Material s_HabitatDark;
    static Material s_Orange;
    static Material s_Cyan;
    static Material s_Purple;
    static Material s_Atlas;
    static Material s_EmissiveOrange;
    static Material s_EmissiveCyan;

    [MenuItem("Tools/Portfolio/Build Space Outpost Arena")]
    public static void Build()
    {
        if (!File.Exists(MainScenePath))
        {
            Debug.LogError($"[Space Outpost] Missing scene: {MainScenePath}");
            return;
        }

        EnsureDirectories();
        if (!File.Exists(BackupScenePath))
        {
            AssetDatabase.CopyAsset(MainScenePath, BackupScenePath);
        }

        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        RemoveLegacyEnvironment(scene);
        CreateMaterials();

        GameObject level = new GameObject("Level");
        s_ArenaRoot = level.transform;

        BuildTerrainAndBounds();
        BuildCentralHabitat();
        BuildLandingZone();
        BuildXenofloraZone();
        BuildNorthAndSouthLanes();
        BuildSpawnBays();
        BuildEnvironmentalDetails();
        ConfigureGameplayObjects(scene);
        ConfigureLightingAndAtmosphere(scene);

        MarkEnvironmentStatic(level);
        WebGLLightingBaker.PrepareCurrentSceneForBake(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[Space Outpost] MainScene rebuilt successfully. Prototype arena backup saved at " +
                  BackupScenePath);
    }

    static void EnsureDirectories()
    {
        EnsureFolder("Assets/Art", "Generated");
        EnsureFolder("Assets/Art/Generated", "SpaceOutpost");
        EnsureFolder(GeneratedRoot, "Materials");
    }

    static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    static void RemoveLegacyEnvironment(Scene scene)
    {
        string[] obsoleteRoots = { "===== ENEMIES =====", "===== PATHS =====" };
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (obsoleteRoots.Contains(root.name))
            {
                UnityEngine.Object.DestroyImmediate(root);
                continue;
            }

            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform != null && transform.name == "Level")
                {
                    UnityEngine.Object.DestroyImmediate(transform.gameObject);
                    break;
                }
            }
        }

        GameObject oldGenerated = GameObject.Find("SpaceOutpost_Arena");
        if (oldGenerated != null)
        {
            UnityEngine.Object.DestroyImmediate(oldGenerated);
        }
    }

    static void CreateMaterials()
    {
        s_Ground = GetOrCreateLitMaterial("M_Ground_Charcoal", Charcoal, 0.06f);
        s_GroundAccent = GetOrCreateLitMaterial("M_Ground_Orange", GroundOrange, 0.08f);
        s_GardenDeck = GetOrCreateLitMaterial("M_Garden_Deck", Hex("2E4B48"), 0.08f);
        s_Habitat = GetOrCreateLitMaterial("M_Habitat_White", HabitatWhite, 0.22f);
        s_HabitatDark = GetOrCreateLitMaterial("M_Habitat_Dark", HabitatDark, 0.18f);
        s_Orange = GetOrCreateLitMaterial("M_Signal_Orange", SignalOrange, 0.2f);
        s_Cyan = GetOrCreateLitMaterial("M_Signal_Cyan", SignalCyan, 0.16f);
        s_Purple = GetOrCreateLitMaterial("M_Flora_Purple", FloraPurple, 0.1f);
        s_EmissiveOrange = GetOrCreateLitMaterial(
            "M_Emissive_Orange", SignalOrange, 0.25f, SignalOrange * 3.5f);
        s_EmissiveCyan = GetOrCreateLitMaterial(
            "M_Emissive_Cyan", SignalCyan, 0.2f, SignalCyan * 3.5f);

        string atlasPath = MaterialRoot + "/M_SpaceKit_Atlas_URP.mat";
        s_Atlas = AssetDatabase.LoadAssetAtPath<Material>(atlasPath);
        if (s_Atlas == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            s_Atlas = new Material(shader) { name = "M_SpaceKit_Atlas_URP" };
            s_Atlas.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SpaceKitRoot + "/Atlas.png"));
            s_Atlas.SetFloat("_Smoothness", 0.18f);
            s_Atlas.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(s_Atlas, atlasPath);
        }

        s_Atlas.enableInstancing = true;
        EditorUtility.SetDirty(s_Atlas);
    }

    static Material GetOrCreateLitMaterial(
        string name,
        Color color,
        float smoothness,
        Color? emission = null)
    {
        string path = $"{MaterialRoot}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", 0f);
        material.enableInstancing = true;
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    static void BuildTerrainAndBounds()
    {
        Transform terrain = Group("01_Terrain");
        CreateCube("Arena Ground", new Vector3(0f, -0.75f, 0f), new Vector3(96f, 1.5f, 76f),
            s_Ground, terrain);
        CreateCube("Plateau Skirt", new Vector3(0f, -3.25f, 0f), new Vector3(104f, 4f, 84f),
            s_GroundAccent, terrain);
        CreateStrip("Landing Deck", new Vector3(-28f, 0.015f, 0f), new Vector3(36f, 0.06f, 54f),
            s_HabitatDark, terrain);
        CreateStrip("Garden Deck", new Vector3(29f, 0.02f, 0f), new Vector3(34f, 0.07f, 54f),
            s_GardenDeck, terrain);
        CreateStrip("North Transit Deck", new Vector3(0f, 0.03f, 28f), new Vector3(28f, 0.08f, 12f),
            s_Habitat, terrain);
        CreateStrip("South Transit Deck", new Vector3(0f, 0.03f, -28f), new Vector3(28f, 0.08f, 12f),
            s_Habitat, terrain);

        CreateCube("North Boundary", new Vector3(0f, 2.5f, 38f), new Vector3(96f, 6f, 1.2f),
            s_HabitatDark, terrain);
        CreateCube("South Boundary", new Vector3(0f, 2.5f, -38f), new Vector3(96f, 6f, 1.2f),
            s_HabitatDark, terrain);
        CreateCube("East Boundary", new Vector3(48f, 2.5f, 0f), new Vector3(1.2f, 6f, 76f),
            s_HabitatDark, terrain);
        CreateCube("West Boundary", new Vector3(-48f, 2.5f, 0f), new Vector3(1.2f, 6f, 76f),
            s_HabitatDark, terrain);

        CreateStrip("North Cyan Route", new Vector3(0f, 0.03f, 25.5f), new Vector3(70f, 0.05f, 0.35f),
            s_EmissiveCyan, terrain);
        CreateStrip("South Orange Route", new Vector3(0f, 0.03f, -25.5f), new Vector3(70f, 0.05f, 0.35f),
            s_EmissiveOrange, terrain);
        CreateStrip("Central Route", new Vector3(0f, 0.035f, 0f), new Vector3(0.35f, 0.05f, 68f),
            s_Habitat, terrain);
    }

    static void BuildCentralHabitat()
    {
        Transform zone = Group("02_Habitat_Core");
        CreateCylinder("Habitat Platform", new Vector3(0f, 0.25f, 0f), new Vector3(13f, 0.5f, 13f),
            s_HabitatDark, zone, 48);
        CreateCylinder("Habitat Ring", new Vector3(0f, 0.54f, 0f), new Vector3(11.5f, 0.08f, 11.5f),
            s_Orange, zone, 48, false);
        CreateCylinder("Core Cover", new Vector3(0f, 2.35f, 0f), new Vector3(5.2f, 4.2f, 5.2f),
            s_Habitat, zone, 32);

        PlaceModel(EnvironmentFbxRoot + "/GeodesicDome.fbx", "Habitat Dome",
            new Vector3(0f, 4.45f, 0f), Vector3.zero, 12.5f, zone, false);
        PlaceModel(EnvironmentFbxRoot + "/Roof_Radar.fbx", "Habitat Radar",
            new Vector3(0f, 7.2f, 0f), new Vector3(0f, 25f, 0f), 4.5f, zone, false);

        CreateRamp("West Core Ramp", new Vector3(-9.5f, 0.4f, 0f), new Vector3(6f, 0.8f, 5f),
            new Vector3(0f, 0f, -7f), s_Habitat, zone);
        CreateRamp("East Core Ramp", new Vector3(9.5f, 0.4f, 0f), new Vector3(6f, 0.8f, 5f),
            new Vector3(0f, 0f, 7f), s_Habitat, zone);

        CreateCoverWall("North Core Shield", new Vector3(0f, 1.25f, 11f), new Vector3(7f, 2.5f, 0.8f),
            s_Habitat, s_EmissiveCyan, zone);
        CreateCoverWall("South Core Shield", new Vector3(0f, 1.25f, -11f), new Vector3(7f, 2.5f, 0.8f),
            s_Habitat, s_EmissiveOrange, zone);
    }

    static void BuildLandingZone()
    {
        Transform zone = Group("03_Landing_Zone");
        CreateCylinder("Landing Pad", new Vector3(-31f, 0.12f, 0f), new Vector3(12f, 0.24f, 12f),
            s_HabitatDark, zone, 48);
        CreateCylinder("Landing Signal", new Vector3(-31f, 0.26f, 0f), new Vector3(10.2f, 0.05f, 10.2f),
            s_Orange, zone, 48, false);
        PlaceModel(VehicleFbxRoot + "/Spaceship_RaeTheRedPanda.fbx", "Docked Fighter",
            new Vector3(-31f, 0.3f, 0f), new Vector3(0f, 35f, 0f), 17f, zone, false);

        CreateCoverWall("Landing Cover A", new Vector3(-27f, 1.25f, 13f), new Vector3(7f, 2.5f, 1f),
            s_HabitatDark, s_EmissiveOrange, zone);
        CreateCoverWall("Landing Cover B", new Vector3(-37f, 1.25f, -13f), new Vector3(7f, 2.5f, 1f),
            s_HabitatDark, s_EmissiveOrange, zone);
        CreateCrateStack(new Vector3(-41f, 0f, 9f), zone, s_Orange);
        CreateCrateStack(new Vector3(-20f, 0f, -13f), zone, s_Habitat);

        PlaceModel(EnvironmentFbxRoot + "/SolarPanel_Ground.fbx", "Solar Array A",
            new Vector3(-42f, 0f, -23f), new Vector3(0f, 20f, 0f), 7f, zone, false);
        PlaceModel(EnvironmentFbxRoot + "/SolarPanel_Ground.fbx", "Solar Array B",
            new Vector3(-33f, 0f, -25f), new Vector3(0f, -15f, 0f), 7f, zone, false);
        PlaceModel(VehicleFbxRoot + "/Rover_2.fbx", "Outpost Rover",
            new Vector3(-22f, 0f, 20f), new Vector3(0f, 150f, 0f), 7f, zone, false);
    }

    static void BuildXenofloraZone()
    {
        Transform zone = Group("04_Xenoflora_Zone");
        CreateCylinder("Xenoflora Ground", new Vector3(31f, 0.08f, 0f), new Vector3(15f, 0.12f, 16f),
            s_Purple, zone, 40, false);

        CreateRockCover("Rock Cover A", new Vector3(25f, 1.5f, 11f), new Vector3(5f, 3f, 4f), zone);
        CreateRockCover("Rock Cover B", new Vector3(37f, 2f, -9f), new Vector3(6f, 4f, 5f), zone);
        CreateRockCover("Rock Cover C", new Vector3(27f, 1.25f, -15f), new Vector3(4f, 2.5f, 4f), zone);
        CreateRockCover("Rock Cover D", new Vector3(41f, 1.5f, 14f), new Vector3(4f, 3f, 4f), zone);

        PlaceModel(EnvironmentFbxRoot + "/Rock_Large_1.fbx", "Hero Rock A",
            new Vector3(25f, 0f, 11f), new Vector3(0f, 35f, 0f), 6f, zone, false);
        PlaceModel(EnvironmentFbxRoot + "/Rock_Large_2.fbx", "Hero Rock B",
            new Vector3(37f, 0f, -9f), new Vector3(0f, -35f, 0f), 7f, zone, false);
        PlaceModel(EnvironmentFbxRoot + "/Tree_Light_1.fbx", "Luminous Tree A",
            new Vector3(36f, 0f, 18f), new Vector3(0f, 15f, 0f), 8f, zone, false);
        PlaceModel(EnvironmentFbxRoot + "/Tree_Spiral_2.fbx", "Spiral Tree",
            new Vector3(21f, 0f, -21f), new Vector3(0f, -25f, 0f), 8f, zone, false);
        PlaceModel(EnvironmentFbxRoot + "/Tree_Floating_2.fbx", "Floating Tree",
            new Vector3(42f, 0f, -22f), new Vector3(0f, 40f, 0f), 7f, zone, false);

        PlaceFlora("Plant_1", new Vector3(19f, 0f, 14f), 3.2f, zone);
        PlaceFlora("Plant_2", new Vector3(31f, 0f, 21f), 3.4f, zone);
        PlaceFlora("Plant_3", new Vector3(40f, 0f, 3f), 3f, zone);
        PlaceFlora("Bush_1", new Vector3(22f, 0f, -5f), 2.8f, zone);
        PlaceFlora("Bush_3", new Vector3(39f, 0f, -17f), 3f, zone);
    }

    static void BuildNorthAndSouthLanes()
    {
        Transform lanes = Group("05_Combat_Lanes");

        CreateCoverWall("North West Cover", new Vector3(-18f, 1.35f, 24f), new Vector3(8f, 2.7f, 1f),
            s_Habitat, s_EmissiveCyan, lanes);
        CreateCoverWall("North East Cover", new Vector3(17f, 1.35f, 25f), new Vector3(8f, 2.7f, 1f),
            s_HabitatDark, s_EmissiveCyan, lanes);
        CreateCoverWall("South West Cover", new Vector3(-17f, 1.35f, -25f), new Vector3(8f, 2.7f, 1f),
            s_HabitatDark, s_EmissiveOrange, lanes);
        CreateCoverWall("South East Cover", new Vector3(18f, 1.35f, -24f), new Vector3(8f, 2.7f, 1f),
            s_Habitat, s_EmissiveOrange, lanes);

        CreateCube("North Mid Block", new Vector3(0f, 1.4f, 27f), new Vector3(5f, 2.8f, 4f),
            s_HabitatDark, lanes);
        CreateCube("South Mid Block", new Vector3(0f, 1.4f, -27f), new Vector3(5f, 2.8f, 4f),
            s_HabitatDark, lanes);
        AddTrim(new Vector3(0f, 2.65f, 27f), new Vector3(5.1f, 0.18f, 4.1f), s_EmissiveCyan, lanes);
        AddTrim(new Vector3(0f, 2.65f, -27f), new Vector3(5.1f, 0.18f, 4.1f), s_EmissiveOrange, lanes);

        PlaceModel(EnvironmentFbxRoot + "/House_Open.fbx", "North Field Lab",
            new Vector3(-6f, 0f, 31f), new Vector3(0f, 180f, 0f), 12f, lanes, false);
        PlaceModel(EnvironmentFbxRoot + "/House_Long.fbx", "South Field Lab",
            new Vector3(8f, 0f, -31f), Vector3.zero, 13f, lanes, false);
    }

    static void BuildSpawnBays()
    {
        Transform spawns = Group("06_Spawn_Bays");
        BuildSpawnBay("Spawn Alpha", new Vector3(-39f, 0f, -28f), 45f, s_EmissiveOrange, spawns);
        BuildSpawnBay("Spawn Bravo", new Vector3(39f, 0f, 28f), 225f, s_EmissiveCyan, spawns);
        BuildSpawnBay("Spawn Charlie", new Vector3(-39f, 0f, 28f), 135f, s_EmissiveCyan, spawns);
        BuildSpawnBay("Spawn Delta", new Vector3(39f, 0f, -28f), 315f, s_EmissiveOrange, spawns);
    }

    static void BuildSpawnBay(
        string name,
        Vector3 position,
        float rotationY,
        Material accent,
        Transform parent)
    {
        Transform bay = new GameObject(name).transform;
        bay.SetParent(parent);
        bay.position = position;
        bay.rotation = Quaternion.Euler(0f, rotationY, 0f);

        CreateCube("Spawn Pad", Vector3.zero, new Vector3(7f, 0.3f, 7f), s_HabitatDark, bay, true);
        CreateCube("Rear Shield", new Vector3(0f, 2f, -3f), new Vector3(7f, 4f, 0.7f),
            s_Habitat, bay, true);
        CreateCube("Left Shield", new Vector3(-3.15f, 1.4f, -0.5f), new Vector3(0.7f, 2.8f, 5f),
            s_HabitatDark, bay, true);
        CreateCube("Right Shield", new Vector3(3.15f, 1.4f, -0.5f), new Vector3(0.7f, 2.8f, 5f),
            s_HabitatDark, bay, true);
        AddTrim(new Vector3(0f, 0.25f, -2.58f), new Vector3(5.5f, 0.14f, 0.16f), accent, bay, true);
    }

    static void BuildEnvironmentalDetails()
    {
        Transform details = Group("07_Background_And_Details");

        PlaceModel(EnvironmentFbxRoot + "/Building_L.fbx", "Operations Building",
            new Vector3(-12f, 0f, 17f), new Vector3(0f, 90f, 0f), 12f, details, false);
        PlaceModel(EnvironmentFbxRoot + "/House_Cylinder.fbx", "Research Annex",
            new Vector3(14f, 0f, 15f), new Vector3(0f, -35f, 0f), 10f, details, false);
        PlaceModel(EnvironmentFbxRoot + "/Roof_Antenna.fbx", "Perimeter Antenna",
            new Vector3(-43f, 0f, 10f), Vector3.zero, 5f, details, false);

        PlaceModel(EnvironmentFbxRoot + "/Planet_3.fbx", "Distant Planet",
            new Vector3(125f, 70f, 190f), Vector3.zero, 70f, details, false, false);
        PlaceModel(VehicleFbxRoot + "/Spaceship_FinnTheFrog.fbx", "Distant Patrol Ship",
            new Vector3(-70f, 35f, 105f), new Vector3(15f, 130f, -10f), 18f, details, false, false);

        CreateBeacon("Landing Beacon", new Vector3(-45f, 0f, 0f), s_EmissiveOrange, details);
        CreateBeacon("Garden Beacon", new Vector3(45f, 0f, 0f), s_EmissiveCyan, details);
        CreateBeacon("North Beacon", new Vector3(0f, 0f, 35f), s_EmissiveCyan, details);
        CreateBeacon("South Beacon", new Vector3(0f, 0f, -35f), s_EmissiveOrange, details);
    }

    static void ConfigureGameplayObjects(Scene scene)
    {
        PlayerSpawnPoint[] spawnPoints = FindSceneComponents<PlayerSpawnPoint>(scene)
            .OrderBy(point => point.name, StringComparer.Ordinal)
            .ToArray();
        Vector3[] positions =
        {
            new(-39f, 0.35f, -28f),
            new(-39f, 0.35f, 28f),
            new(39f, 0.35f, -28f),
            new(39f, 0.35f, 28f),
        };

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            Vector3 position = positions[i % positions.Length];
            spawnPoints[i].transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation((Vector3.zero - position).normalized, Vector3.up));
        }

        List<MonoBehaviour> pickups = FindSceneComponents<MonoBehaviour>(scene)
            .Where(component => component != null && component.GetType().Name.EndsWith("Pickup"))
            .OrderBy(component => component.name, StringComparer.Ordinal)
            .ToList();
        Vector3[] pickupPositions =
        {
            new(0f, 0.8f, 8f),
            new(0f, 0.8f, -8f),
            new(-27f, 0.8f, 17f),
            new(28f, 0.8f, -18f),
            new(-16f, 0.8f, -25f),
            new(17f, 0.8f, 25f),
        };

        for (int i = 0; i < pickups.Count; i++)
        {
            pickups[i].transform.position = pickupPositions[i % pickupPositions.Length];
        }
    }

    static void ConfigureLightingAndAtmosphere(Scene scene)
    {
        Material skybox = GetOrCreateSkybox();
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.2f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = 128;
        RenderSettings.reflectionIntensity = 0.85f;
        RenderSettings.reflectionBounces = 1;
        RenderSettings.subtractiveShadowColor = Hex("26363F");
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Hex("526C7B");
        RenderSettings.fogDensity = 0.0038f;

        Transform lighting = Group("08_Lighting");
        Light directional = FindSceneComponents<Light>(scene)
            .FirstOrDefault(light => light.type == LightType.Directional);
        if (directional == null)
        {
            GameObject lightObject = new GameObject("Outpost Sun");
            lightObject.transform.SetParent(lighting);
            directional = lightObject.AddComponent<Light>();
            directional.type = LightType.Directional;
        }
        else
        {
            directional.name = "Outpost Sun";
            directional.transform.SetParent(lighting);
        }

        directional.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        directional.useColorTemperature = true;
        directional.colorTemperature = 4900f;
        directional.color = Hex("FFF0D8");
        directional.intensity = 2.25f;
        directional.lightmapBakeType = LightmapBakeType.Mixed;
        directional.shadows = LightShadows.Hard;
        directional.shadowStrength = 0.82f;
        directional.shadowBias = 0.06f;
        directional.shadowNormalBias = 0.35f;
        directional.shadowNearPlane = 0.2f;
        directional.bounceIntensity = 1.15f;
        RenderSettings.sun = directional;

        CreateSpotLight("Landing Accent", new Vector3(-31f, 12f, -2f), SignalOrange, 6f, 23f, 78f,
            new Vector3(12f, 0f, 0f), lighting);
        CreateSpotLight("Habitat Accent", new Vector3(0f, 14f, 0f), SignalCyan, 5.5f, 22f, 72f,
            Vector3.zero, lighting);
        CreateSpotLight("Garden Accent", new Vector3(31f, 12f, 2f), Hex("A979D9"), 5f, 23f, 78f,
            new Vector3(20f, 0f, 0f), lighting);

        GameObject volumeObject = new GameObject("Space Outpost Global Volume");
        volumeObject.transform.SetParent(lighting);
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 20f;

        string profilePath = GeneratedRoot + "/SpaceOutpostVolume.asset";
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
        }

        foreach (VolumeComponent component in profile.components.Where(component => component != null).ToArray())
        {
            UnityEngine.Object.DestroyImmediate(component, true);
        }
        profile.components.Clear();
        Bloom bloom = profile.Add<Bloom>();
        AssetDatabase.AddObjectToAsset(bloom, profile);
        bloom.active = true;
        bloom.intensity.Override(0.38f);
        bloom.threshold.Override(1.05f);
        bloom.scatter.Override(0.55f);

        Tonemapping tonemapping = profile.Add<Tonemapping>();
        AssetDatabase.AddObjectToAsset(tonemapping, profile);
        tonemapping.active = true;
        tonemapping.mode.Override(TonemappingMode.ACES);

        ColorAdjustments color = profile.Add<ColorAdjustments>();
        AssetDatabase.AddObjectToAsset(color, profile);
        color.active = true;
        color.postExposure.Override(0.4f);
        color.contrast.Override(8f);
        color.saturation.Override(10f);

        Vignette vignette = profile.Add<Vignette>();
        AssetDatabase.AddObjectToAsset(vignette, profile);
        vignette.active = true;
        vignette.intensity.Override(0.1f);
        vignette.smoothness.Override(0.4f);

        EditorUtility.SetDirty(profile);
        volume.sharedProfile = profile;
        DynamicGI.UpdateEnvironment();
    }

    static Material GetOrCreateSkybox()
    {
        string path = MaterialRoot + "/M_SpaceOutpost_Skybox.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Skybox/Procedural")) { name = "M_SpaceOutpost_Skybox" };
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_SkyTint", Hex("527D98"));
        material.SetColor("_GroundColor", Hex("1C3038"));
        material.SetFloat("_AtmosphereThickness", 0.82f);
        material.SetFloat("_Exposure", 1.15f);
        material.SetFloat("_SunSize", 0.025f);
        material.SetFloat("_SunSizeConvergence", 7f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Transform Group(string name)
    {
        GameObject group = new GameObject(name);
        group.transform.SetParent(s_ArenaRoot);
        return group.transform;
    }

    static GameObject CreateCube(
        string name,
        Vector3 position,
        Vector3 scale,
        Material material,
        Transform parent,
        bool local = false)
    {
        GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gameObject.name = name;
        gameObject.transform.SetParent(parent, !local);
        if (local)
        {
            gameObject.transform.localPosition = position;
        }
        else
        {
            gameObject.transform.position = position;
        }
        gameObject.transform.localScale = scale;
        ApplyMaterial(gameObject, material);
        return gameObject;
    }

    static GameObject CreateCylinder(
        string name,
        Vector3 position,
        Vector3 scale,
        Material material,
        Transform parent,
        int sides,
        bool collision = true)
    {
        GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        gameObject.name = name;
        gameObject.transform.SetParent(parent);
        gameObject.transform.position = position;
        gameObject.transform.localScale = scale;
        ApplyMaterial(gameObject, material);
        if (!collision)
        {
            UnityEngine.Object.DestroyImmediate(gameObject.GetComponent<Collider>());
        }
        return gameObject;
    }

    static void CreateStrip(
        string name,
        Vector3 position,
        Vector3 scale,
        Material material,
        Transform parent)
    {
        GameObject strip = CreateCube(name, position, scale, material, parent);
        UnityEngine.Object.DestroyImmediate(strip.GetComponent<Collider>());
    }

    static void AddTrim(
        Vector3 position,
        Vector3 scale,
        Material material,
        Transform parent,
        bool local = false)
    {
        GameObject trim = CreateCube("Energy Trim", position, scale, material, parent, local);
        UnityEngine.Object.DestroyImmediate(trim.GetComponent<Collider>());
    }

    static void CreateRamp(
        string name,
        Vector3 position,
        Vector3 scale,
        Vector3 rotation,
        Material material,
        Transform parent)
    {
        GameObject ramp = CreateCube(name, position, scale, material, parent);
        ramp.transform.rotation = Quaternion.Euler(rotation);
    }

    static void CreateCoverWall(
        string name,
        Vector3 position,
        Vector3 scale,
        Material body,
        Material accent,
        Transform parent)
    {
        GameObject wall = CreateCube(name, position, scale, body, parent);
        Vector3 trimPosition = position + new Vector3(0f, scale.y * 0.42f, -(scale.z * 0.51f));
        AddTrim(trimPosition, new Vector3(scale.x * 0.75f, 0.16f, 0.1f), accent, parent);
    }

    static void CreateCrateStack(Vector3 position, Transform parent, Material accent)
    {
        CreateCube("Cargo Crate", position + new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f),
            s_HabitatDark, parent);
        CreateCube("Cargo Crate", position + new Vector3(2.1f, 0.8f, 0.2f), new Vector3(2f, 1.6f, 2f),
            accent, parent);
        CreateCube("Cargo Crate", position + new Vector3(1f, 2.6f, 0.1f), new Vector3(1.8f, 1.4f, 1.8f),
            s_Habitat, parent);
    }

    static void CreateRockCover(string name, Vector3 position, Vector3 scale, Transform parent)
    {
        GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        rock.name = name;
        rock.transform.SetParent(parent);
        rock.transform.position = position;
        rock.transform.localScale = scale;
        rock.transform.rotation = Quaternion.Euler(
            UnityEngine.Random.Range(-10f, 10f),
            UnityEngine.Random.Range(-35f, 35f),
            UnityEngine.Random.Range(-8f, 8f));
        ApplyMaterial(rock, s_Purple);

        UnityEngine.Object.DestroyImmediate(rock.GetComponent<SphereCollider>());
        rock.AddComponent<BoxCollider>();
    }

    static void CreateBeacon(
        string name,
        Vector3 position,
        Material emissive,
        Transform parent)
    {
        CreateCylinder(name + " Base", position + new Vector3(0f, 0.4f, 0f),
            new Vector3(1.2f, 0.4f, 1.2f), s_HabitatDark, parent, 24);
        GameObject pillar = CreateCube(name, position + new Vector3(0f, 3f, 0f),
            new Vector3(0.35f, 5.2f, 0.35f), emissive, parent);
        UnityEngine.Object.DestroyImmediate(pillar.GetComponent<Collider>());
    }

    static void CreateSpotLight(
        string name,
        Vector3 position,
        Color color,
        float intensity,
        float range,
        float spotAngle,
        Vector3 target,
        Transform parent)
    {
        GameObject lightObject = new GameObject(name);
        lightObject.transform.SetParent(parent);
        lightObject.transform.position = position;
        lightObject.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.spotAngle = spotAngle;
        light.innerSpotAngle = spotAngle * 0.72f;
        light.shadows = LightShadows.None;
        light.lightmapBakeType = LightmapBakeType.Baked;
        light.renderMode = LightRenderMode.Auto;
    }

    static void PlaceFlora(string modelName, Vector3 position, float targetSize, Transform parent)
    {
        PlaceModel(EnvironmentFbxRoot + "/" + modelName + ".fbx", modelName,
            position, new Vector3(0f, UnityEngine.Random.Range(0f, 360f), 0f),
            targetSize, parent, false);
    }

    static GameObject PlaceModel(
        string assetPath,
        string name,
        Vector3 position,
        Vector3 rotation,
        float targetSize,
        Transform parent,
        bool addCollider,
        bool castShadows = true)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (model == null)
        {
            Debug.LogWarning($"[Space Outpost] Could not load model: {assetPath}");
            return null;
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
        if (instance == null)
        {
            return null;
        }

        instance.name = name;
        instance.transform.SetParent(parent);
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
        instance.transform.localScale = Vector3.one;

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            renderer.sharedMaterial = s_Atlas;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = castShadows;
        }

        Bounds bounds = CalculateBounds(renderers);
        float largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largestDimension > 0.001f)
        {
            instance.transform.localScale = Vector3.one * (targetSize / largestDimension);
        }

        bounds = CalculateBounds(instance.GetComponentsInChildren<Renderer>(true));
        instance.transform.position += new Vector3(position.x - bounds.center.x, position.y - bounds.min.y,
            position.z - bounds.center.z);

        if (addCollider)
        {
            bounds = CalculateBounds(instance.GetComponentsInChildren<Renderer>(true));
            BoxCollider collider = instance.AddComponent<BoxCollider>();
            collider.center = instance.transform.InverseTransformPoint(bounds.center);
            Vector3 localSize = instance.transform.InverseTransformVector(bounds.size);
            collider.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
        }

        return instance;
    }

    static Bounds CalculateBounds(Renderer[] renderers)
    {
        if (renderers.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.one);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    static void ApplyMaterial(GameObject gameObject, Material material)
    {
        Renderer renderer = gameObject.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    static void MarkEnvironmentStatic(GameObject root)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(
                transform.gameObject,
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic);
        }
    }

    static T[] FindSceneComponents<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .ToArray();
    }

    static Color Hex(string value)
    {
        return ColorUtility.TryParseHtmlString("#" + value, out Color color) ? color : Color.magenta;
    }
}
