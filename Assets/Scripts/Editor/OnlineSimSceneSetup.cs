using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates ShooterOnlineSim.unity — headless simulation of the online Q-learning loop.
/// Menu: MGU → Create Online Sim Scene
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

        GameObject lightObj = new GameObject("DirectionalLight");
        Light light         = lightObj.AddComponent<Light>();
        light.type          = LightType.Directional;
        light.intensity     = 1f;
        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject floor     = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name           = "Floor";
        floor.transform.localScale = new Vector3(20f, 1f, 20f);

        const float spacing = 60f;
        CreateEnvironment("Env_BeginnerSim",
                          new Vector3(-spacing, 0f, 0f),
                          SyntheticPlayer.SkillProfile.Naive,
                          PlayerSkillLevel.Beginner, 25);

        CreateEnvironment("Env_IntermediateSim",
                          new Vector3(0f, 0f, 0f),
                          SyntheticPlayer.SkillProfile.Average,
                          PlayerSkillLevel.Intermediate, 25);

        CreateEnvironment("Env_AdvancedSim",
                          new Vector3(spacing, 0f, 0f),
                          SyntheticPlayer.SkillProfile.Expert,
                          PlayerSkillLevel.Advanced, 25);

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
    }

    static void CreateEnvironment(string envName, Vector3 offset,
                                  SyntheticPlayer.SkillProfile synthProfile,
                                  PlayerSkillLevel skillLevel,
                                  int roundsToSimulate)
    {
        GameObject root = new GameObject(envName);
        root.transform.position = offset;

        GameObject targetPf = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/ShooterTarget.prefab");

        // Target manager
        GameObject mgrObj = new GameObject("TargetManager");
        mgrObj.transform.SetParent(root.transform, false);
        ShooterTargetManager mgr  = mgrObj.AddComponent<ShooterTargetManager>();
        mgr.targetPrefab          = targetPf;
        mgr.areaCenter            = offset + new Vector3(0f, 3.5f, 22f);
        mgr.areaSize              = new Vector3(22f, 5f, 20f);
        mgr.poolSize              = 12;
        mgr.spawnDelay            = 3f;

        // Event log
        GameObject logObj         = new GameObject("EventLog");
        logObj.transform.SetParent(root.transform, false);
        ShooterEventLog eventLog  = logObj.AddComponent<ShooterEventLog>();

        // Round manager
        GameObject roundObj           = new GameObject("RoundManager");
        roundObj.transform.SetParent(root.transform, false);
        ShooterRoundManager roundMgr  = roundObj.AddComponent<ShooterRoundManager>();
        roundMgr.targetManager        = mgr;
        roundMgr.roundDuration        = 30f;
        roundMgr.despawnDelay         = 0.1f;
        roundMgr.eventLog             = eventLog;
        roundMgr.trainingMode         = true;
        mgr.roundManager              = roundMgr;
        mgr.eventLog                  = eventLog;

        // Adaptive spawn controller
        GameObject adaptObj         = new GameObject("AdaptiveController");
        adaptObj.transform.SetParent(root.transform, false);
        AdaptiveSpawnController asc = adaptObj.AddComponent<AdaptiveSpawnController>();
        asc.targetManager           = mgr;
        asc.roundManager            = roundMgr;
        asc.eventLog                = eventLog;
        roundMgr.adaptiveController = asc;
        mgr.adaptiveController     = asc;

        // Synthetic player
        GameObject playerObj      = new GameObject("SyntheticPlayer");
        playerObj.transform.SetParent(root.transform, false);
        playerObj.transform.position = offset + new Vector3(0f, 1.4f, 3f);
        SyntheticPlayer synth     = playerObj.AddComponent<SyntheticPlayer>();
        synth.profile             = synthProfile;
        synth.targetManager       = mgr;
        synth.roundManager        = roundMgr;
        synth.eventLog            = eventLog;

        // Online sim controller
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
