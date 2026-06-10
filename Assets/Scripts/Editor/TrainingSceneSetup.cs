using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates the offline training scene (no VR, headless-friendly).
/// Menu: MGU → Create Training Scene
///
/// Runs three parallel environments (Beginner / Intermediate / Advanced synthetic players),
/// each using the same Q-learning AdaptiveSpawnController as live gameplay.
/// After training (200 rounds per env), each saves a base profile that seeds new real players.
///
/// To run: open ShooterTraining.unity and press Play (no Python needed).
/// Base profiles appear in: Application.persistentDataPath/shooter_profiles/_base_*.json
/// </summary>
public static class TrainingSceneSetup
{
    [MenuItem("MGU/Create Training Scene")]
    public static void CreateTrainingScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        ShooterSceneSetup.EnsureFolder("Assets", "Scenes");
        ShooterSceneSetup.EnsureFolder("Assets", "Prefabs");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject lightObj = new GameObject("DirectionalLight");
        Light light = lightObj.AddComponent<Light>();
        light.type      = LightType.Directional;
        light.intensity = 1f;
        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(20f, 1f, 20f);

        // Three envs side by side, each with its own synthetic player + adaptive agent
        const float spacing = 60f;
        CreateEnvironment("Env_Beginner",     new Vector3(-spacing, 0f, 0f),
                          SyntheticPlayer.SkillProfile.Naive,   PlayerSkillLevel.Beginner,   200);
        CreateEnvironment("Env_Intermediate", new Vector3(0f,       0f, 0f),
                          SyntheticPlayer.SkillProfile.Average, PlayerSkillLevel.Intermediate, 200);
        CreateEnvironment("Env_Advanced",     new Vector3(spacing,  0f, 0f),
                          SyntheticPlayer.SkillProfile.Expert,  PlayerSkillLevel.Advanced,   200);

        GameObject camObj = new GameObject("MainCamera");
        camObj.AddComponent<Camera>();
        camObj.transform.position = new Vector3(0f, 30f, -20f);
        camObj.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
        camObj.tag = "MainCamera";

        const string scenePath = "Assets/Scenes/ShooterTraining.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        ShooterSceneSetup.AddSceneToBuildSettings(scenePath);
        AssetDatabase.SaveAssets();

        Debug.Log("[Training] Scene created → " + scenePath);
        Debug.Log("[Training] Press Play to run training (no Python required).");
        Debug.Log("[Training] Base profiles save to: " +
                  System.IO.Path.Combine(Application.persistentDataPath, "shooter_profiles"));
    }

    static void CreateEnvironment(string name, Vector3 offset,
                                  SyntheticPlayer.SkillProfile synthProfile,
                                  PlayerSkillLevel skillLevel,
                                  int roundsToTrain)
    {
        GameObject root = new GameObject(name);
        root.transform.position = offset;

        GameObject targetPf = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/ShooterTarget.prefab");

        // Target manager
        GameObject mgrObj = new GameObject("TargetManager");
        mgrObj.transform.SetParent(root.transform, false);
        ShooterTargetManager mgr = mgrObj.AddComponent<ShooterTargetManager>();
        mgr.targetPrefab         = targetPf;
        mgr.areaCenter           = offset + new Vector3(0f, 3.5f, 22f);
        mgr.areaSize             = new Vector3(22f, 5f, 20f);
        mgr.stationaryCount      = 4;
        mgr.movingCount          = 4;
        mgr.erraticCount         = 4;
        mgr.maxConcurrentTargets = 6;   // start at medium; agent will tune
        mgr.minSpawnInterval     = 1.2f;
        mgr.maxSpawnInterval     = 2.5f;
        mgr.respawnDelay         = 5f;

        // Event log
        GameObject logObj = new GameObject("EventLog");
        logObj.transform.SetParent(root.transform, false);
        ShooterEventLog eventLog = logObj.AddComponent<ShooterEventLog>();

        // Round manager (training mode = no session check, no disk writes)
        GameObject roundObj = new GameObject("RoundManager");
        roundObj.transform.SetParent(root.transform, false);
        ShooterRoundManager roundMgr = roundObj.AddComponent<ShooterRoundManager>();
        roundMgr.targetManager = mgr;
        roundMgr.roundDuration = 30f;
        roundMgr.despawnDelay  = 0.1f; // fast cleanup between rounds
        roundMgr.eventLog      = eventLog;
        roundMgr.trainingMode  = true;
        mgr.roundManager       = roundMgr;

        // Adaptive spawn controller (same as live gameplay)
        GameObject adaptObj = new GameObject("AdaptiveController");
        adaptObj.transform.SetParent(root.transform, false);
        AdaptiveSpawnController adaptive = adaptObj.AddComponent<AdaptiveSpawnController>();
        adaptive.targetManager = mgr;
        adaptive.roundManager  = roundMgr;
        adaptive.eventLog      = eventLog;

        roundMgr.adaptiveController = adaptive;

        // Synthetic player
        GameObject playerObj = new GameObject("SyntheticPlayer");
        playerObj.transform.SetParent(root.transform, false);
        playerObj.transform.position = offset + new Vector3(0f, 1.4f, 3f);
        SyntheticPlayer synth = playerObj.AddComponent<SyntheticPlayer>();
        synth.profile       = synthProfile;
        synth.targetManager = mgr;
        synth.roundManager  = roundMgr;
        synth.eventLog      = eventLog;

        // Training controller (drives the round loop, saves base profile when done)
        GameObject ctrlObj = new GameObject("TrainingController");
        ctrlObj.transform.SetParent(root.transform, false);
        TrainingRoundController ctrl = ctrlObj.AddComponent<TrainingRoundController>();
        ctrl.adaptiveController = adaptive;
        ctrl.roundManager       = roundMgr;
        ctrl.targetManager      = mgr;
        ctrl.eventLog           = eventLog;
        ctrl.syntheticPlayer    = synth;
        ctrl.skillLevel         = skillLevel;
        ctrl.roundsToTrain      = roundsToTrain;
        ctrl.trainingTimeScale  = 6f;
        ctrl.interRoundDelay    = 0.05f;

        EditorUtility.SetDirty(root);
    }
}
