using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates ShooterOnlineSim.unity — a headless simulation of the online Q-learning loop.
/// Menu: MGU → Create Online Sim Scene
///
/// Runs three parallel environments (Beginner / Intermediate / Advanced), each starting
/// from the trained base profile exactly as a new real player would, then adapting over
/// 25 simulated rounds. Profile saves happen every round — identical to live gameplay.
///
/// Workflow:
///   1. MGU → Create Training Scene, press Play → produces _base_*.json files.
///   2. MGU → Create Online Sim Scene, press Play → simulates what a player experiences.
///   3. Check the console or shooter_sim_reports/ for per-round adaptation data.
/// </summary>
public static class OnlineSimSceneSetup
{
    [MenuItem("MGU/Create Online Sim Scene")]
    public static void CreateOnlineSimScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        ShooterSceneSetup.EnsureFolder("Assets", "Scenes");
        ShooterSceneSetup.EnsureFolder("Assets", "Prefabs");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Lighting
        GameObject lightObj = new GameObject("DirectionalLight");
        Light light         = lightObj.AddComponent<Light>();
        light.type          = LightType.Directional;
        light.intensity     = 1f;
        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Shared floor (visual reference only)
        GameObject floor     = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name           = "Floor";
        floor.transform.localScale = new Vector3(20f, 1f, 20f);

        // Three environments in parallel — one per skill tier
        const float spacing = 60f;
        CreateEnvironment("Env_BeginnerSim",
                          new Vector3(-spacing, 0f, 0f),
                          SyntheticPlayer.SkillProfile.Naive,
                          PlayerSkillLevel.Beginner,
                          25);

        CreateEnvironment("Env_IntermediateSim",
                          new Vector3(0f, 0f, 0f),
                          SyntheticPlayer.SkillProfile.Average,
                          PlayerSkillLevel.Intermediate,
                          25);

        CreateEnvironment("Env_AdvancedSim",
                          new Vector3(spacing, 0f, 0f),
                          SyntheticPlayer.SkillProfile.Expert,
                          PlayerSkillLevel.Advanced,
                          25);

        // Overview camera
        GameObject camObj = new GameObject("MainCamera");
        camObj.AddComponent<Camera>();
        camObj.transform.position = new Vector3(0f, 30f, -20f);
        camObj.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
        camObj.tag = "MainCamera";

        const string scenePath = "Assets/Scenes/ShooterOnlineSim.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        ShooterSceneSetup.AddSceneToBuildSettings(scenePath);
        AssetDatabase.SaveAssets();

        Debug.Log("[OnlineSim] Scene created → " + scenePath);
        Debug.Log("[OnlineSim] Run ShooterTraining.unity first to build base profiles, " +
                  "then press Play here to simulate online adaptation.");
        Debug.Log("[OnlineSim] Session reports → " +
                  System.IO.Path.Combine(Application.persistentDataPath, "shooter_sim_reports"));
    }

    // ─────────────────────────────────────────────────────────────────────────

    static void CreateEnvironment(string envName, Vector3 offset,
                                  SyntheticPlayer.SkillProfile synthProfile,
                                  PlayerSkillLevel skillLevel,
                                  int roundsToSimulate)
    {
        GameObject root = new GameObject(envName);
        root.transform.position = offset;

        GameObject targetPf = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/ShooterTarget.prefab");

        // ── Target manager ────────────────────────────────────────────────────
        GameObject mgrObj         = new GameObject("TargetManager");
        mgrObj.transform.SetParent(root.transform, false);
        ShooterTargetManager mgr  = mgrObj.AddComponent<ShooterTargetManager>();
        mgr.targetPrefab          = targetPf;
        mgr.areaCenter            = offset + new Vector3(0f, 3.5f, 22f);
        mgr.areaSize              = new Vector3(22f, 5f, 20f);
        mgr.stationaryCount       = 4;
        mgr.movingCount           = 4;
        mgr.erraticCount          = 4;
        mgr.maxConcurrentTargets  = 6;   // AdaptiveSpawnController will tune this
        mgr.minSpawnInterval      = 1.2f;
        mgr.maxSpawnInterval      = 2.5f;
        mgr.respawnDelay          = 5f;

        // ── Event log ─────────────────────────────────────────────────────────
        GameObject logObj         = new GameObject("EventLog");
        logObj.transform.SetParent(root.transform, false);
        ShooterEventLog eventLog  = logObj.AddComponent<ShooterEventLog>();

        // ── Round manager (trainingMode=true: skips session checks & report writes) ─
        GameObject roundObj           = new GameObject("RoundManager");
        roundObj.transform.SetParent(root.transform, false);
        ShooterRoundManager roundMgr  = roundObj.AddComponent<ShooterRoundManager>();
        roundMgr.targetManager        = mgr;
        roundMgr.roundDuration        = 30f;
        roundMgr.despawnDelay         = 0.1f;  // fast cleanup between rounds
        roundMgr.eventLog             = eventLog;
        roundMgr.trainingMode         = true;   // avoids session / disk-report overhead
        mgr.roundManager              = roundMgr;

        // ── Adaptive spawn controller (same as in live gameplay) ──────────────
        GameObject adaptObj         = new GameObject("AdaptiveController");
        adaptObj.transform.SetParent(root.transform, false);
        AdaptiveSpawnController asc = adaptObj.AddComponent<AdaptiveSpawnController>();
        asc.targetManager           = mgr;
        asc.roundManager            = roundMgr;
        asc.eventLog                = eventLog;

        roundMgr.adaptiveController = asc;

        // ── Synthetic player ──────────────────────────────────────────────────
        GameObject playerObj      = new GameObject("SyntheticPlayer");
        playerObj.transform.SetParent(root.transform, false);
        playerObj.transform.position = offset + new Vector3(0f, 1.4f, 3f);
        SyntheticPlayer synth     = playerObj.AddComponent<SyntheticPlayer>();
        synth.profile             = synthProfile;
        synth.targetManager       = mgr;
        synth.roundManager        = roundMgr;
        synth.eventLog            = eventLog;

        // ── Online sim controller (drives the per-session round loop) ─────────
        GameObject ctrlObj        = new GameObject("OnlineSimController");
        ctrlObj.transform.SetParent(root.transform, false);
        OnlineSimController ctrl  = ctrlObj.AddComponent<OnlineSimController>();
        ctrl.adaptiveController   = asc;
        ctrl.roundManager         = roundMgr;
        ctrl.eventLog             = eventLog;
        ctrl.skillLevel           = skillLevel;
        ctrl.roundsToSimulate     = roundsToSimulate;
        ctrl.simulationTimeScale  = 8f;
        ctrl.interRoundDelay      = 0.05f;

        EditorUtility.SetDirty(root);
    }
}
