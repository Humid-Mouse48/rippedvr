using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LocomotionEditorTools
{
    private const string SettingsPath = "Assets/Settings/LocomotionSettings.asset";
    private const string MaterialFolder = "Assets/Materials/MovementTest";
    private const string ScenePath = "Assets/Scenes/MovementTest.unity";
    private const string MonkeModelPath = "Assets/Models/Player/monke.glb";
    private const string OutputFolder = "Builds";
    private const string OutputPath = "Builds/RIPPED.apk";

    private static readonly string[] LayerNames = { "Ground", "NoGrip", "Slippery", "Bouncy", "Hazard", "Player" };

    [MenuItem("RIPPED/1 - Setup Layers And Settings")]
    public static void SetupLayersAndSettings()
    {
        LocomotionSettings settings = PrepareSettings();
        Selection.activeObject = settings;
    }

    [MenuItem("RIPPED/2 - Build Movement Test Scene")]
    public static void BuildMovementTestScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        CreateMovementTestScene();
    }

    [MenuItem("RIPPED/3 - Build APK")]
    public static void BuildApkFromMenu()
    {
        bool proceed = EditorUtility.DisplayDialog(
            "RIPPED",
            "Before building, confirm that Project Settings > XR Plug-in Management > Android has OpenXR ticked, with the Oculus Touch Controller Profile and Meta Quest Support enabled. Continue?",
            "Build",
            "Cancel");

        if (!proceed || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        BuildApk();
    }

    public static void BuildApkFromCommandLine()
    {
        bool succeeded = BuildApk();
        EditorApplication.Exit(succeeded ? 0 : 1);
    }

    private static bool BuildApk()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            if (!switched)
            {
                Debug.LogError("Could not switch to the Android platform. Install Android Build Support in Unity Hub.");
                return false;
            }

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("RIPPED", "Switched the platform to Android. Run RIPPED > 3 - Build APK once more.", "OK");
            }

            return false;
        }

        CreateMovementTestScene();
        ConfigureAndroidPlayer();
        Directory.CreateDirectory(OutputFolder);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        bool succeeded = summary.result == BuildResult.Succeeded;

        if (succeeded)
        {
            Debug.Log("APK built: " + Path.GetFullPath(OutputPath) + " (" + (summary.totalSize / 1048576f).ToString("0.0") + " MB)");
            if (!Application.isBatchMode)
            {
                EditorUtility.RevealInFinder(OutputPath);
            }
        }
        else
        {
            Debug.LogError("APK build failed with result " + summary.result + " and " + summary.totalErrors + " errors. Check the Console.");
        }

        return succeeded;
    }

    private static void ConfigureAndroidPlayer()
    {
        PlayerSettings.productName = "RIPPED";
        PlayerSettings.companyName = "RIPPED";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.ripped.game");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
        EditorUserBuildSettings.buildAppBundle = false;
    }

    private static void CreateMovementTestScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildSceneContents(true);

        EnsureFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings();
    }

    public static void BuildSceneContents(bool createLight)
    {
        LocomotionSettings settings = PrepareSettings();

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.38f, 0.38f, 0.42f);

        if (createLight)
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.9f);
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        GameObject spawn = new GameObject("SpawnPoint");
        spawn.transform.position = new Vector3(0f, 0.02f, 0f);

        GameObject root = new GameObject("MovementTestArea");
        Transform area = root.transform;

        Color floorColor = new Color(0.25f, 0.25f, 0.28f);
        CreateBlock(area, "FloorWest", new Vector3(-7f, -2f, 10f), new Vector3(16f, 4f, 40f), Vector3.zero, "Ground", "Floor", floorColor);
        CreateBlock(area, "FloorEast", new Vector3(10f, -2f, 10f), new Vector3(10f, 4f, 40f), Vector3.zero, "Ground", "Floor", floorColor);
        CreateBlock(area, "FloorSouthMid", new Vector3(3f, -2f, -3f), new Vector3(4f, 4f, 14f), Vector3.zero, "Ground", "Floor", floorColor);
        CreateBlock(area, "FloorNorthMid", new Vector3(3f, -2f, 19f), new Vector3(4f, 4f, 22f), Vector3.zero, "Ground", "Floor", floorColor);
        CreateBlock(area, "PitBottom", new Vector3(3f, -3.5f, 6f), new Vector3(4f, 1f, 4f), Vector3.zero, "Ground", "Floor", floorColor);

        Color rockColor = new Color(0.45f, 0.3f, 0.2f);
        CreateBlock(area, "SteepRamp", new Vector3(-9f, 1.9f, 8f), new Vector3(4f, 0.4f, 8f), new Vector3(-30f, 0f, 0f), "Ground", "Climbable", rockColor);
        CreateBlock(area, "Ledge", new Vector3(-3f, 0.75f, 8f), new Vector3(4f, 1.5f, 4f), Vector3.zero, "Ground", "Climbable", rockColor);
        CreateBlock(area, "LedgeHigh", new Vector3(-3f, 1.5f, 13f), new Vector3(4f, 3f, 3f), Vector3.zero, "Ground", "Climbable", rockColor);
        CreateBlock(area, "ClimbWall", new Vector3(10f, 1.5f, 20f), new Vector3(6f, 3f, 0.3f), Vector3.zero, "Ground", "Climbable", rockColor);
        CreateBlock(area, "ThinWall", new Vector3(3f, 1.5f, 26f), new Vector3(4f, 3f, 0.05f), Vector3.zero, "Ground", "ThinWall", new Color(0.9f, 0.5f, 0.1f));

        CreateBlock(area, "IcePatch", new Vector3(10f, 0.01f, 4f), new Vector3(8f, 0.02f, 8f), Vector3.zero, "Slippery", "Ice", new Color(0.6f, 0.85f, 1f));
        CreateBlock(area, "BouncePad", new Vector3(10f, 0.05f, 14f), new Vector3(4f, 0.1f, 4f), Vector3.zero, "Bouncy", "Bounce", new Color(0.9f, 0.2f, 0.8f));
        CreateBlock(area, "NoGripWall", new Vector3(10f, 1.5f, 24f), new Vector3(6f, 3f, 0.3f), Vector3.zero, "NoGrip", "NoGrip", new Color(0.85f, 0.9f, 0.95f));

        Color wallColor = new Color(0.35f, 0.35f, 0.4f);
        CreateBlock(area, "CorridorWallA", new Vector3(-12.65f, 1.25f, 20f), new Vector3(0.3f, 2.5f, 8f), Vector3.zero, "Ground", "Wall", wallColor);
        CreateBlock(area, "CorridorWallB", new Vector3(-11.35f, 1.25f, 20f), new Vector3(0.3f, 2.5f, 8f), Vector3.zero, "Ground", "Wall", wallColor);
        CreateBlock(area, "CorridorRoof", new Vector3(-12f, 2.65f, 20f), new Vector3(1.6f, 0.3f, 8f), Vector3.zero, "Ground", "Wall", wallColor);

        GameObject hazard = CreateBlock(area, "HazardZone", new Vector3(-3f, 0.75f, -4f), new Vector3(4f, 1.5f, 4f), Vector3.zero, "Hazard", "Hazard", new Color(0.85f, 0.1f, 0.1f));
        hazard.GetComponent<BoxCollider>().isTrigger = true;

        CreateLabel(area, "RAMP", new Vector3(-9f, 5f, 8f), 0f);
        CreateLabel(area, "LEDGES", new Vector3(-3f, 4.2f, 8f), 0f);
        CreateLabel(area, "PIT", new Vector3(3f, 2.5f, 6f), 0f);
        CreateLabel(area, "ICE", new Vector3(10f, 2.5f, 4f), 0f);
        CreateLabel(area, "BOUNCE PAD", new Vector3(10f, 2.5f, 14f), 0f);
        CreateLabel(area, "CLIMB WALL", new Vector3(10f, 4.2f, 20f), 0f);
        CreateLabel(area, "NO GRIP WALL", new Vector3(10f, 4.2f, 24f), 0f);
        CreateLabel(area, "THIN WALL", new Vector3(3f, 4f, 26f), 0f);
        CreateLabel(area, "TIGHT CORRIDOR", new Vector3(-12f, 3.6f, 20f), 0f);
        CreateLabel(area, "HAZARD", new Vector3(-3f, 2.5f, -4f), 180f);

        CreatePlayerRig(settings, spawn.transform);

        MiningEditorTools.PopulateMiningContent();
    }

    private static void CreatePlayerRig(LocomotionSettings settings, Transform spawn)
    {
        GameObject player = new GameObject("Player");
        player.transform.SetPositionAndRotation(spawn.position, Quaternion.Euler(0f, spawn.eulerAngles.y, 0f));
        player.AddComponent<Rigidbody>();

        GameObject head = new GameObject("Head");
        head.tag = "MainCamera";
        head.transform.SetParent(player.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        Camera camera = head.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 200f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
        head.AddComponent<AudioListener>();
        head.AddComponent<LocomotionDebugOverlay>();

        Transform leftHand = CreateAnchor(player.transform, "LeftHand");
        Transform rightHand = CreateAnchor(player.transform, "RightHand");
        Transform leftVisual = CreateHandVisual(player.transform, "SolvedLeftHand", settings.handRadius, new Color(1f, 0.5f, 0.1f));
        Transform rightVisual = CreateHandVisual(player.transform, "SolvedRightHand", settings.handRadius, new Color(0.1f, 0.7f, 1f));

        AttachMonkeAvatar(player.transform, head.transform, leftVisual, rightVisual, leftHand, rightHand);

        XRPoseDriver poseDriver = player.AddComponent<XRPoseDriver>();
        ArmLocomotion locomotion = player.AddComponent<ArmLocomotion>();

        SerializedObject poseSerialized = new SerializedObject(poseDriver);
        poseSerialized.FindProperty("head").objectReferenceValue = head.transform;
        poseSerialized.FindProperty("leftHand").objectReferenceValue = leftHand;
        poseSerialized.FindProperty("rightHand").objectReferenceValue = rightHand;
        poseSerialized.ApplyModifiedProperties();

        SerializedObject locomotionSerialized = new SerializedObject(locomotion);
        locomotionSerialized.FindProperty("settings").objectReferenceValue = settings;
        locomotionSerialized.FindProperty("headTransform").objectReferenceValue = head.transform;
        locomotionSerialized.FindProperty("leftHandTransform").objectReferenceValue = leftHand;
        locomotionSerialized.FindProperty("rightHandTransform").objectReferenceValue = rightHand;
        locomotionSerialized.FindProperty("leftHandVisual").objectReferenceValue = leftVisual;
        locomotionSerialized.FindProperty("rightHandVisual").objectReferenceValue = rightVisual;
        locomotionSerialized.FindProperty("respawnPoint").objectReferenceValue = spawn;
        locomotionSerialized.ApplyModifiedProperties();
    }

    private static void AttachMonkeAvatar(Transform player, Transform head, Transform leftVisual, Transform rightVisual, Transform leftAnchor, Transform rightAnchor)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(MonkeModelPath);
        if (model == null)
        {
            Debug.LogWarning("monke.glb did not import (is the glTFast package installed?). Keeping the sphere hands.");
            return;
        }

        GameObject body = (GameObject)Object.Instantiate(model, player);
        body.name = "MonkeBody";
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = Vector3.one;

        MonkeAvatar avatar = body.AddComponent<MonkeAvatar>();
        avatar.Configure(body.transform, head, leftVisual, rightVisual, leftAnchor, rightAnchor);
        EditorUtility.SetDirty(avatar);

        MeshRenderer leftRenderer = leftVisual.GetComponent<MeshRenderer>();
        MeshRenderer rightRenderer = rightVisual.GetComponent<MeshRenderer>();
        if (leftRenderer != null)
        {
            leftRenderer.enabled = false;
        }

        if (rightRenderer != null)
        {
            rightRenderer.enabled = false;
        }

        Debug.Log("Monke avatar attached to the player rig.");
    }

    private static Transform CreateAnchor(Transform parent, string anchorName)
    {
        GameObject anchor = new GameObject(anchorName);
        anchor.transform.SetParent(parent, false);
        return anchor.transform;
    }

    private static Transform CreateHandVisual(Transform parent, string visualName, float radius, Color color)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = visualName;
        Object.DestroyImmediate(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(parent, false);
        sphere.transform.localScale = Vector3.one * radius * 2f;
        sphere.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial(visualName, color);
        return sphere.transform;
    }

    public static LocomotionSettings PrepareSettings()
    {
        for (int i = 0; i < LayerNames.Length; i++)
        {
            EnsureLayer(LayerNames[i]);
        }

        EnsureFolder("Assets", "Settings");
        LocomotionSettings settings = AssetDatabase.LoadAssetAtPath<LocomotionSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<LocomotionSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }

        settings.solidMask = LayerMask.GetMask("Ground", "NoGrip", "Slippery", "Bouncy");
        settings.noGripMask = LayerMask.GetMask("NoGrip");
        settings.slipperyMask = LayerMask.GetMask("Slippery");
        settings.bouncyMask = LayerMask.GetMask("Bouncy");
        settings.hazardMask = LayerMask.GetMask("Hazard");

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return settings;
    }

    private static int EnsureLayer(string layerName)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0)
        {
            return existing;
        }

        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                return i;
            }
        }

        Debug.LogError("No free layer slot for " + layerName);
        return -1;
    }

    private static void EnsureFolder(string parent, string folderName)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + folderName))
        {
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }

    private static Material GetMaterial(string materialName, Color color)
    {
        EnsureFolder("Assets", "Materials");
        EnsureFolder("Assets/Materials", "MovementTest");

        string path = MaterialFolder + "/" + materialName + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        material = new Material(shader);
        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static GameObject CreateBlock(Transform parent, string blockName, Vector3 center, Vector3 size, Vector3 euler, string layerName, string materialName, Color color)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = blockName;
        block.transform.SetParent(parent, false);
        block.transform.localPosition = center;
        block.transform.localEulerAngles = euler;
        block.transform.localScale = size;
        block.layer = LayerMask.NameToLayer(layerName);
        block.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial(materialName, color);
        GameObjectUtility.SetStaticEditorFlags(block, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        return block;
    }

    private static void CreateLabel(Transform parent, string text, Vector3 position, float yaw)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        GameObject labelObject = new GameObject("Label " + text);
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.localPosition = position;
        labelObject.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        TextMesh textMesh = labelObject.AddComponent<TextMesh>();
        textMesh.font = font;
        textMesh.text = text;
        textMesh.fontSize = 80;
        textMesh.characterSize = 0.08f;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = Color.white;
        labelObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }

    private static void AddSceneToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        for (int i = 0; i < scenes.Count; i++)
        {
            if (scenes[i].path == ScenePath)
            {
                return;
            }
        }

        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
