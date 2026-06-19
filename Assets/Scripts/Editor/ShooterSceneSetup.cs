using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click creation of the Shooter scene (shooting range).
///
///   MGU ▸ Create Shooter Scene
///
/// Builds the entire scene from scratch — floor, walls, targets,
/// gun, player, HUD, and all wiring.
/// </summary>
public static class ShooterSceneSetup
{
    // ── Arena constants ─────────────────────────────────────────────────────────
    const float ARENA_W = 30f;  // X
    const float ARENA_L = 50f;  // Z
    const float ARENA_H = 8f;   // wall height
    const float WALL_THICKNESS = 0.5f;

    // ── Target area — the zone in front of the player where targets move ────────
    static readonly Vector3 TARGET_AREA_CENTER = new Vector3(0f, 3.5f, 22f);
    static readonly Vector3 TARGET_AREA_SIZE   = new Vector3(22f, 5f, 20f);

    [MenuItem("MGU/Create Shooter Scene")]
    public static void CreateShooterScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EnsureFolder("Assets", "Scenes");
        EnsureFolder("Assets", "Materials");
        EnsureFolder("Assets", "Prefabs");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateLighting();
        CreateArena();

        // Scene roots — lobby shown first, range hidden until player is chosen
        GameObject lobbyRoot = new GameObject("SessionLobby");
        GameObject rangeRoot = new GameObject("ShootingRange");
        rangeRoot.SetActive(false);

        OVRCameraRig rig = CreatePlayer(out GameObject rightControllerVisual);
        GameObject   gun = CreateGun(rig);
        gun.SetActive(false);

        GameObject bulletPf = CreateBulletPrefab();
        GameObject targetPf = CreateTargetPrefab();

        ShooterButton rangeButton = null;
        GameObject barrierGo = CreateBarrierAndButton(out rangeButton);
        barrierGo.transform.SetParent(rangeRoot.transform, true);

        GameObject mgrObj   = CreateTargetManager(targetPf);
        mgrObj.transform.SetParent(rangeRoot.transform, true);

        GameObject roundObj = CreateRoundManager();
        roundObj.transform.SetParent(rangeRoot.transform, true);

        GameObject hudObj = CreateHUD(rig);

        PlayerSelectUI selectUI = null;
        CreateSessionLobby(lobbyRoot.transform, rig, out selectUI);

        // Rig interaction systems
        ShooterInteractSystem interact = rig.gameObject.GetComponent<ShooterInteractSystem>();
        if (interact == null) interact = rig.gameObject.AddComponent<ShooterInteractSystem>();
        interact.enabled = false;

        VRPointerSystem pointer = rig.gameObject.GetComponent<VRPointerSystem>();
        if (pointer == null) pointer = rig.gameObject.AddComponent<VRPointerSystem>();

        GameObject sessionObj = new GameObject("PlayerSessionManager");
        PlayerSessionManager sessionMgr = sessionObj.AddComponent<PlayerSessionManager>();

        // Get components
        ShooterGun           gunComp  = gun.GetComponent<ShooterGun>();
        ShooterTargetManager tgtMgr   = mgrObj.GetComponent<ShooterTargetManager>();
        ShooterRoundManager  roundMgr = roundObj.GetComponent<ShooterRoundManager>();
        ShooterHUD           hud      = hudObj.GetComponent<ShooterHUD>();
        ShooterPlayerController player = rig.GetComponent<ShooterPlayerController>();

        // ── Wire gameplay ─────────────────────────────────────────────────────
        gunComp.bulletPrefab = bulletPf;
        gunComp.roundManager = roundMgr;

        tgtMgr.targetPrefab = targetPf;
        tgtMgr.roundManager = roundMgr;

        roundMgr.targetManager = tgtMgr;
        roundMgr.hud           = hud;
        roundMgr.startButton   = rangeButton;

        // ── Wire ML / Adaptive system ───────────────────────────────────────
        GameObject mlObj = new GameObject("MLAdaptive");
        mlObj.transform.SetParent(rangeRoot.transform, true);

        ShooterEventLog eventLog = mlObj.AddComponent<ShooterEventLog>();
        AdaptiveSpawnController adaptive = mlObj.AddComponent<AdaptiveSpawnController>();
        adaptive.targetManager = tgtMgr;
        adaptive.roundManager  = roundMgr;
        adaptive.eventLog      = eventLog;

        roundMgr.eventLog           = eventLog;
        roundMgr.adaptiveController = adaptive;
        tgtMgr.adaptiveController   = adaptive;
        tgtMgr.eventLog             = eventLog;

        // Skill prompt (scene root, not child of lobby — must stay visible independently)
        GameObject promptObj = new GameObject("SkillPrompt");
        SkillPromptUI skillPrompt = promptObj.AddComponent<SkillPromptUI>();
        CreateSkillPromptUI(promptObj, skillPrompt, rig);

        // Difficulty feedback panel (5 shootable buttons shown after each round)
        DifficultyFeedbackUI feedbackUI = CreateDifficultyFeedbackUI(rangeRoot.transform);
        roundMgr.feedbackUI = feedbackUI;

        // ── Wire session / lobby ──────────────────────────────────────────────
        if (selectUI != null)
        {
            selectUI.sessionManager = sessionMgr;
            EditorUtility.SetDirty(selectUI.gameObject);
        }

        sessionMgr.lobbyRoot             = lobbyRoot;
        sessionMgr.rangeRoot             = rangeRoot;
        sessionMgr.gunObject             = gun;
        sessionMgr.rightControllerVisual = rightControllerVisual;
        sessionMgr.pointerSystem         = pointer;
        sessionMgr.interactSystem        = interact;
        sessionMgr.gun                   = gunComp;
        sessionMgr.playerController      = player;
        sessionMgr.hud                   = hud;
        sessionMgr.rangeStartButton      = rangeButton;
        sessionMgr.adaptiveController    = adaptive;
        sessionMgr.skillPromptUI         = skillPrompt;

        EditorUtility.SetDirty(gun);
        EditorUtility.SetDirty(mgrObj);
        EditorUtility.SetDirty(roundObj);
        EditorUtility.SetDirty(hudObj);
        EditorUtility.SetDirty(sessionObj);
        EditorUtility.SetDirty(lobbyRoot);
        EditorUtility.SetDirty(rangeRoot);
        EditorUtility.SetDirty(mlObj);
        EditorUtility.SetDirty(rig.gameObject);

        // Save
        const string scenePath = "Assets/Scenes/Shooter.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        AddSceneToBuildSettings(scenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[Shooter] Scene created → " + scenePath);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Lighting
    // ═════════════════════════════════════════════════════════════════════════════

    static void CreateLighting()
    {
        GameObject lightObj = new GameObject("Directional Light");
        Light light = lightObj.AddComponent<Light>();
        light.type      = LightType.Directional;
        light.intensity = 1.4f;
        light.shadows   = LightShadows.Soft;
        lightObj.transform.SetPositionAndRotation(
            new Vector3(0f, 10f, 0f), Quaternion.Euler(50f, -30f, 0f));

        // Ambient boost so indoors isn't too dark
        RenderSettings.ambientMode      = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight     = new Color(0.35f, 0.35f, 0.4f);
        RenderSettings.ambientIntensity = 1f;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Arena (floor + 4 walls + ceiling)
    // ═════════════════════════════════════════════════════════════════════════════

    static void CreateArena()
    {
        Material floorMat  = GetOrCreateMat("Assets/Materials/ShooterFloor.mat",
                                            new Color(0.18f, 0.18f, 0.22f), 0.1f, 0.4f);
        Material wallMat   = GetOrCreateMat("Assets/Materials/ShooterWall.mat",
                                            new Color(0.25f, 0.25f, 0.30f), 0.0f, 0.3f);
        Material ceilMat   = GetOrCreateMat("Assets/Materials/ShooterCeiling.mat",
                                            new Color(0.12f, 0.12f, 0.15f), 0.0f, 0.2f);

        // Floor
        MakeArenaBox("Floor", new Vector3(0f, -0.25f, ARENA_L / 2f),
                new Vector3(ARENA_W, 0.5f, ARENA_L), floorMat);

        // Ceiling
        MakeArenaBox("Ceiling", new Vector3(0f, ARENA_H + 0.25f, ARENA_L / 2f),
                new Vector3(ARENA_W, 0.5f, ARENA_L), ceilMat);

        // Back wall (behind player)
        MakeArenaBox("WallBack", new Vector3(0f, ARENA_H / 2f, -WALL_THICKNESS / 2f),
                new Vector3(ARENA_W, ARENA_H, WALL_THICKNESS), wallMat);

        // Front wall (far end)
        MakeArenaBox("WallFront", new Vector3(0f, ARENA_H / 2f, ARENA_L + WALL_THICKNESS / 2f),
                new Vector3(ARENA_W, ARENA_H, WALL_THICKNESS), wallMat);

        // Left wall
        MakeArenaBox("WallLeft", new Vector3(-ARENA_W / 2f - WALL_THICKNESS / 2f, ARENA_H / 2f, ARENA_L / 2f),
                new Vector3(WALL_THICKNESS, ARENA_H, ARENA_L + WALL_THICKNESS * 2f), wallMat);

        // Right wall
        MakeArenaBox("WallRight", new Vector3(ARENA_W / 2f + WALL_THICKNESS / 2f, ARENA_H / 2f, ARENA_L / 2f),
                new Vector3(WALL_THICKNESS, ARENA_H, ARENA_L + WALL_THICKNESS * 2f), wallMat);

        // Separator line on floor (decorative range marker)
        MakeArenaBox("RangeLine", new Vector3(0f, 0.01f, 8f),
                new Vector3(ARENA_W - 2f, 0.02f, 0.15f),
                GetOrCreateMat("Assets/Materials/ShooterLineYellow.mat",
                               new Color(1f, 0.85f, 0.1f), 0f, 0.8f));
    }

    static GameObject MakeArenaBox(string name, Vector3 pos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position   = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.isStatic = true;
        EnsureSolidCollider(go);
        return go;
    }

    /// <summary>Primitives ship with a BoxCollider; ensure it is solid for bullet SphereCasts.</summary>
    static void EnsureSolidCollider(GameObject go)
    {
        if (go == null) return;
        Collider col = go.GetComponent<Collider>();
        if (col == null)
            col = go.AddComponent<BoxCollider>();
        col.isTrigger = false;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Player
    // ═════════════════════════════════════════════════════════════════════════════

    static OVRCameraRig CreatePlayer(out GameObject rightControllerVisual)
    {
        rightControllerVisual = null;
        const string rigPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
        if (prefab == null)
        {
            Debug.LogError("[Shooter] OVRCameraRig prefab not found.");
            return null;
        }

        GameObject go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        go.transform.position = new Vector3(0f, 0f, 3f); // a few metres from back wall

        OVRCameraRig rig = go.GetComponent<OVRCameraRig>();
        OVRManager mgr   = go.GetComponent<OVRManager>()
                         ?? go.GetComponentInChildren<OVRManager>();
        if (mgr != null)
        {
            mgr.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            EditorUtility.SetDirty(mgr);
        }

        // CharacterController — matched to the Seal scene's tuned settings
        CharacterController cc = go.GetComponent<CharacterController>();
        if (cc == null) cc = go.AddComponent<CharacterController>();
        cc.height          = 1.8f;
        cc.radius          = 0.3f;
        cc.center          = new Vector3(0f, 0.9f, 0f);
        cc.slopeLimit      = 45f;
        cc.stepOffset      = 0.4f;
        cc.skinWidth       = 0.02f;
        cc.minMoveDistance = 0.001f;

        // ShooterPlayerController
        go.AddComponent<ShooterPlayerController>();

        // Both controller visuals — gun replaces right hand once gameplay begins
        AddControllerPrefab(rig.leftControllerAnchor,  1, "LeftController");
        rightControllerVisual = AddControllerPrefab(rig.rightControllerAnchor, 2, "RightController");

        EditorUtility.SetDirty(go);
        return rig;
    }

    static GameObject AddControllerPrefab(Transform anchor, int controllerInt, string goName)
    {
        if (anchor == null) return null;

        const string prefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRControllerPrefab.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("[Shooter] OVRControllerPrefab not found — add controller visual manually.");
            return null;
        }

        GameObject inst = PrefabUtility.InstantiatePrefab(prefab, anchor) as GameObject;
        inst.name = goName;
        inst.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        Component helper = inst.GetComponent("OVRControllerHelper");
        if (helper != null)
        {
            var so   = new SerializedObject(helper);
            var prop = so.FindProperty("m_controller");
            if (prop != null)
            {
                prop.intValue = controllerInt;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        EditorUtility.SetDirty(inst);
        return inst;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Gun — pistol3 model parented to right controller
    // ═════════════════════════════════════════════════════════════════════════════

    const string PISTOL3_FBX = "Assets/Low Poly Guns/Models/Guns/pistol3/pistol3.fbx";
    const string PISTOL3_MAT = "Assets/Low Poly Guns/Models/Guns/pistol3/pistol3.mat";

    static GameObject CreateGun(OVRCameraRig rig)
    {
        if (rig == null) return null;
        Transform anchor = rig.rightControllerAnchor;
        if (anchor == null) return null;

        // Remove old gun only — RightController is kept for the player-select lobby
        Transform existing = anchor.Find("Gun");
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        // ── Gun root ──────────────────────────────────────────────────────────
        // These offsets seat a typical low-poly pistol nicely in the right hand.
        // Tweak localPosition / localRotation in the Inspector if needed.
        GameObject gun = new GameObject("Gun");
        gun.transform.SetParent(anchor, false);
        gun.transform.localPosition = new Vector3(0f, -0.02f, 0.04f);
        gun.transform.localRotation = Quaternion.identity;

        Transform muzzlePoint = null;

        // ── Pistol3 model ─────────────────────────────────────────────────────
        GameObject pistolPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PISTOL3_FBX);
        if (pistolPrefab != null)
        {
            GameObject model = PrefabUtility.InstantiatePrefab(pistolPrefab, gun.transform)
                               as GameObject;
            model.name = "pistol3_model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            const float gunModelScale = 1f / 6f; // ⅓ then halved again
            model.transform.localScale    = Vector3.one * gunModelScale;

            foreach (Collider c in model.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(c);

            Material pistolMat = AssetDatabase.LoadAssetAtPath<Material>(PISTOL3_MAT);
            if (pistolMat != null)
            {
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
                    r.sharedMaterial = pistolMat;
            }
            else
            {
                Debug.LogWarning("[Shooter] pistol3.mat not found at: " + PISTOL3_MAT);
            }

            // Muzzle at barrel tip from mesh bounds (parented to model so it tracks scale)
            GameObject muzzle = new GameObject("MuzzlePoint");
            muzzle.transform.SetParent(model.transform, false);
            Bounds barrelBounds = CalcRendererBoundsLocal(model.transform);
            muzzle.transform.localPosition = new Vector3(
                barrelBounds.center.x,
                barrelBounds.center.y,
                barrelBounds.max.z + 0.005f);
            muzzle.transform.localRotation = Quaternion.identity;
            muzzlePoint = muzzle.transform;

            Debug.Log("[Shooter] pistol3 attached at 1/6 scale, muzzle local "
                    + muzzle.transform.localPosition
                    + " — fine-tune via Gun > ShooterGun > Muzzle Tuning, or move MuzzlePoint child.");
        }
        else
        {
            Debug.LogWarning("[Shooter] pistol3.fbx not found at: " + PISTOL3_FBX);

            GameObject muzzle = new GameObject("MuzzlePoint");
            muzzle.transform.SetParent(gun.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.02f, 0.05f);
            muzzlePoint = muzzle.transform;
        }

        ShooterGun sg = gun.AddComponent<ShooterGun>();
        sg.muzzlePoint         = muzzlePoint != null ? muzzlePoint : gun.transform;
        sg.muzzleLocalOffset   = new Vector3(0f, 0.04775f, 0f);
        sg.muzzleLocalRotation = Vector3.zero;
        sg.bulletSpeed           = 400f;
        sg.muzzleLightIntensity  = 9f;
        sg.muzzleLightRange      = 5f;

        EditorUtility.SetDirty(gun);
        return gun;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Bullet prefab
    // ═════════════════════════════════════════════════════════════════════════════

    static GameObject CreateBulletPrefab()
    {
        const string path = "Assets/Prefabs/ShooterBullet.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            Rigidbody existingRb = existing.GetComponent<Rigidbody>();
            if (existingRb != null)
            {
                existingRb.isKinematic = true;
                existingRb.useGravity  = false;
            }
            ShooterBullet existingBullet = existing.GetComponent<ShooterBullet>();
            if (existingBullet != null)
            {
                existingBullet.impactLightIntensityMiss      = 7f;
                existingBullet.impactLightIntensityTarget     = 18f;
                existingBullet.impactLightIntensityBullseye   = 36f;
                existingBullet.impactLightRange               = 7f;
                existingBullet.impactLightDuration            = 0.1f;
                existingBullet.impactLightFrontOffset         = 0.04f;
                EditorUtility.SetDirty(existingBullet);
            }
            EditorUtility.SetDirty(existing);
            return existing;
        }

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ShooterBullet";
        go.transform.localScale = Vector3.one * 0.04f;

        // Bright emissive material so the bullet is visible in flight
        Material mat = new Material(Shader.Find("Standard"))
        {
            color = new Color(1f, 0.9f, 0.3f)
        };
        mat.SetFloat("_Metallic",   0f);
        mat.SetFloat("_Glossiness", 0.8f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.05f) * 2f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        AssetDatabase.CreateAsset(mat, "Assets/Materials/BulletMat.mat");

        // Trigger collider as fallback; primary detection is SphereCast in ShooterBullet
        SphereCollider sc = go.GetComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius    = 0.5f; // world radius ≈ 0.02 m at scale 0.04

        // Kinematic Rigidbody so trigger events fire
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity  = false;

        ShooterBullet bullet = go.AddComponent<ShooterBullet>();
        bullet.impactLightIntensityMiss      = 7f;
        bullet.impactLightIntensityTarget     = 18f;
        bullet.impactLightIntensityBullseye   = 36f;
        bullet.impactLightRange               = 7f;
        bullet.impactLightDuration            = 0.1f;
        bullet.impactLightFrontOffset         = 0.04f;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        Debug.Log("[Shooter] Bullet prefab → " + path);
        return prefab;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Target prefab (bullseye disc — concentric rings)
    // ═════════════════════════════════════════════════════════════════════════════

    static GameObject CreateTargetPrefab()
    {
        const string prefabPath = "Assets/Prefabs/ShooterTarget.prefab";

        // Always recreate to pick up any layout fixes
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null)
            AssetDatabase.DeleteAsset(prefabPath);

        // Materials for bullseye rings
        Material white = GetOrCreateMat("Assets/Materials/TargetWhite.mat",
                                        new Color(0.95f, 0.95f, 0.95f), 0f, 0.3f);
        Material red   = GetOrCreateMat("Assets/Materials/TargetRed.mat",
                                        new Color(0.85f, 0.1f, 0.08f), 0f, 0.3f);
        Material gold  = GetOrCreateMat("Assets/Materials/TargetGold.mat",
                                        new Color(1f, 0.82f, 0.05f), 0.4f, 0.7f);

        float targetRadius = 0.5f;

        GameObject root = new GameObject("ShooterTarget");

        // Build the bullseye as flattened cylinders.
        // Targets are spawned with 180° Y rotation so local +Z faces the player.
        // Inner (smaller) rings must be at HIGHER local Z so they appear in front
        // after the Y-flip, giving correct layering (gold centre on top).
        float[] radii  = { 1.0f, 0.70f, 0.40f, 0.15f };
        Material[] mats = { white, red, white, gold };
        float zOff = 0f;
        for (int i = 0; i < radii.Length; i++)
        {
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = $"Ring{i}";
            ring.transform.SetParent(root.transform, false);
            float r = radii[i] * targetRadius;
            ring.transform.localScale    = new Vector3(r * 2f, 0.015f, r * 2f);
            ring.transform.localPosition = new Vector3(0f, 0f, zOff);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.GetComponent<Renderer>().sharedMaterial = mats[i];
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            zOff += 0.005f; // inner rings at higher local Z → closer to player after 180° flip
        }

        // Solid box collider on root (thick enough for backup; bullets use SphereCast)
        BoxCollider col = root.AddComponent<BoxCollider>();
        col.isTrigger = false;
        col.size   = new Vector3(targetRadius * 2f, targetRadius * 2f, 0.15f);
        col.center = Vector3.zero;

        // ShooterTarget component
        ShooterTarget st = root.AddComponent<ShooterTarget>();
        st.targetRadius = targetRadius;

        // Back plate (thin dark disc BEHIND the rings)
        // Negative local Z → after 180° Y flip ends up further from player
        GameObject back = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        back.name = "BackPlate";
        back.transform.SetParent(root.transform, false);
        back.transform.localScale    = new Vector3(targetRadius * 2.1f, 0.02f, targetRadius * 2.1f);
        back.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        back.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        back.GetComponent<Renderer>().sharedMaterial =
            GetOrCreateMat("Assets/Materials/TargetBack.mat", new Color(0.1f, 0.1f, 0.12f), 0f, 0.2f);
        Object.DestroyImmediate(back.GetComponent<Collider>());

        // Save as prefab
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        Debug.Log("[Shooter] Target prefab → " + prefabPath);
        return prefab;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Barrier + start button
    // ═════════════════════════════════════════════════════════════════════════════

    static GameObject CreateBarrierAndButton(out ShooterButton button)
    {
        Material barrierMat = GetOrCreateMat("Assets/Materials/BarrierMat.mat",
                                             new Color(0.3f, 0.3f, 0.35f), 0f, 0.3f);

        // Low wall at the shooting line (z=7.5, right in front of player at z=3)
        GameObject barrier = new GameObject("Barrier");

        // Waist-high wall (keeps cross-bar below eye level)
        const float postH   = 0.7f;
        const float postY   = postH * 0.5f;
        const float beamY   = postH + 0.04f;
        const float buttonY = beamY + 0.14f;

        MakeBox("BarrierLeft",  new Vector3(-2.8f, postY, 7.5f),
                                new Vector3(0.25f, postH, 0.25f), barrierMat)
            .transform.SetParent(barrier.transform, true);
        MakeBox("BarrierRight", new Vector3( 2.8f, postY, 7.5f),
                                new Vector3(0.25f, postH, 0.25f), barrierMat)
            .transform.SetParent(barrier.transform, true);
        MakeBox("BarrierBeam",  new Vector3(0f, beamY, 7.5f),
                                new Vector3(5.8f, 0.1f, 0.22f), barrierMat)
            .transform.SetParent(barrier.transform, true);

        // Start button — glowing cube sitting on top of the left post at easy aim height
        Material btnMat = GetOrCreateMat("Assets/Materials/StartButtonMat.mat",
                                         new Color(0.15f, 0.65f, 0.15f), 0f, 0.6f);
        btnMat.EnableKeyword("_EMISSION");
        btnMat.SetColor("_EmissionColor", new Color(0.05f, 0.3f, 0.05f));

        GameObject btnGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        btnGo.name = "StartButton";
        btnGo.transform.SetParent(barrier.transform, true);
        btnGo.transform.position   = new Vector3(0f, buttonY, 7.5f);
        btnGo.transform.localScale = new Vector3(0.3f, 0.18f, 0.15f);
        btnGo.GetComponent<Renderer>().sharedMaterial = btnMat;

        button = btnGo.AddComponent<ShooterButton>();
        EnsureSolidCollider(btnGo);

        Debug.Log("[Shooter] Barrier and start button created at z=7.5.");
        return barrier;
    }

    static GameObject MakeBox(string name, Vector3 pos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position   = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.isStatic = true;
        EnsureSolidCollider(go);
        return go;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Target Manager
    // ═════════════════════════════════════════════════════════════════════════════

    static GameObject CreateTargetManager(GameObject targetPrefab)
    {
        GameObject go = new GameObject("TargetManager");
        ShooterTargetManager mgr = go.AddComponent<ShooterTargetManager>();
        mgr.targetPrefab = targetPrefab;
        mgr.areaCenter   = TARGET_AREA_CENTER;
        mgr.areaSize     = TARGET_AREA_SIZE;
        mgr.poolSize     = 12;
        mgr.spawnDelay   = 3f;

        EditorUtility.SetDirty(go);
        return go;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Session lobby — player select + VR keyboard
    // ═════════════════════════════════════════════════════════════════════════════

    static GameObject CreateSessionLobby(Transform lobbyRoot, OVRCameraRig rig,
                                         out PlayerSelectUI selectUI)
    {
        selectUI = null;
        Font font = GetBuiltinFont();

        Material lobbyWallMat = GetOrCreateMat("Assets/Materials/LobbyWall.mat",
            new Color(0.08f, 0.10f, 0.18f), 0f, 0.25f);
        Material lobbyAccentMat = GetOrCreateMat("Assets/Materials/LobbyAccent.mat",
            new Color(0.12f, 0.35f, 0.55f), 0f, 0.5f);
        Material keyMat = GetOrCreateMat("Assets/Materials/KeyboardKey.mat",
            new Color(0.22f, 0.24f, 0.30f), 0f, 0.4f);
        Material listEntryMat = GetOrCreateMat("Assets/Materials/PlayerListEntry.mat",
            new Color(0.18f, 0.22f, 0.32f), 0f, 0.4f);

        // Warm lobby light
        GameObject lobbyLight = new GameObject("LobbyLight");
        lobbyLight.transform.SetParent(lobbyRoot, false);
        lobbyLight.transform.position = new Vector3(0f, 3.5f, 5f);
        Light ll = lobbyLight.AddComponent<Light>();
        ll.type = LightType.Point;
        ll.color = new Color(0.9f, 0.85f, 0.7f);
        ll.intensity = 1.8f;
        ll.range = 14f;

        // Partition between lobby and range
        GameObject partition = GameObject.CreatePrimitive(PrimitiveType.Cube);
        partition.name = "LobbyPartition";
        partition.transform.SetParent(lobbyRoot, false);
        partition.transform.position = new Vector3(0f, 2.5f, 8.2f);
        partition.transform.localScale = new Vector3(ARENA_W - 4f, 5f, 0.08f);
        partition.GetComponent<Renderer>().sharedMaterial = lobbyWallMat;
        partition.isStatic = true;
        EnsureSolidCollider(partition);

        // Main lobby panel (styled backdrop)
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "LobbyPanel";
        panel.transform.SetParent(lobbyRoot, false);
        panel.transform.position = new Vector3(0f, 1.55f, 4.8f);
        panel.transform.localScale = new Vector3(3.6f, 2.2f, 0.06f);
        panel.GetComponent<Renderer>().sharedMaterial = lobbyAccentMat;
        Object.DestroyImmediate(panel.GetComponent<Collider>());

        // World-space UI canvas — parented to lobbyRoot (NOT the scaled panel) so
        // text scale is not crushed by the panel's non-uniform transform.
        const float lobbyCanvasScale = 0.0011f;

        GameObject canvasObj = new GameObject("LobbyCanvas");
        canvasObj.transform.SetParent(lobbyRoot, false);
        canvasObj.transform.position   = new Vector3(0f, 1.55f, 4.74f);
        canvasObj.transform.rotation   = Quaternion.Euler(0f, 180f, 0f);
        // Positive scale — mirroring is handled by the ContentFlipRoot child below.
        canvasObj.transform.localScale = new Vector3(lobbyCanvasScale, lobbyCanvasScale, lobbyCanvasScale);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        RectTransform canvasRt = canvasObj.GetComponent<RectTransform>();
        canvasRt.sizeDelta = new Vector2(1200f, 800f);

        // ContentFlipRoot: -X scale so that all uGUI text sits in a coordinate space that
        // cancels the glyph-mirror produced by the canvas's 180° Y rotation.
        // Layout is preserved: left anchor still appears on the player's left.
        GameObject contentFlipObj = new GameObject("ContentFlipRoot");
        contentFlipObj.transform.SetParent(canvasObj.transform, false);
        RectTransform contentFlipRt = contentFlipObj.AddComponent<RectTransform>();
        contentFlipRt.anchorMin  = Vector2.zero;
        contentFlipRt.anchorMax  = Vector2.one;
        contentFlipRt.offsetMin  = Vector2.zero;
        contentFlipRt.offsetMax  = Vector2.zero;
        contentFlipRt.localScale = new Vector3(-1f, 1f, 1f);

        // All lobby text is parented to contentFlipObj (inside the flip root).
        Text titleText = CreateLobbyText(contentFlipObj.transform, "TitleText", "WHO IS PLAYING?",
            font, 38, FontStyle.Bold, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -25f), new Vector2(900f, 60f));

        Text hintText = CreateLobbyText(contentFlipObj.transform, "HintText",
            "Select a saved player or type a new name on the keyboard.",
            font, 20, FontStyle.Normal, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(1000f, 50f));
        hintText.color = new Color(0.85f, 0.9f, 1f);

        Text listHeader = CreateLobbyText(contentFlipObj.transform, "ListHeader", "SAVED PLAYERS",
            font, 22, FontStyle.Bold, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -140f), new Vector2(400f, 32f));

        Text nameLabel = CreateLobbyText(contentFlipObj.transform, "NameLabel", "YOUR NAME",
            font, 22, FontStyle.Bold, TextAnchor.UpperRight,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -140f), new Vector2(400f, 32f));

        Text nameDisplay = CreateLobbyText(contentFlipObj.transform, "NameDisplay", "_",
            font, 34, FontStyle.Bold, TextAnchor.MiddleRight,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -195f), new Vector2(500f, 55f));
        nameDisplay.color = new Color(0.3f, 1f, 0.5f);

        // List container (empty — populated at runtime; rotated so entry fronts face the player)
        GameObject listContainer = new GameObject("PlayerListContainer");
        listContainer.transform.SetParent(lobbyRoot, false);
        listContainer.transform.position = new Vector3(-1.2f, 1.35f, 4.76f);
        // Use the same 180° Y rotation as the lobby canvas so the container's +X axis
        // is world -X.  Combined with the 180° Y rotation on each label, this makes
        // text read left-to-right from the player's perspective without any -X scale trick.
        listContainer.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        GameObject listEntryPrefab = CreatePlayerListEntryPrefab(listEntryMat, font);

        // VR Keyboard — centred, tilted toward the player
        GameObject keyboardRoot = new GameObject("VRKeyboard");
        keyboardRoot.transform.SetParent(lobbyRoot, false);
        keyboardRoot.transform.position = new Vector3(0f, 0.95f, 4.25f);
        keyboardRoot.transform.rotation = Quaternion.Euler(22f, 0f, 0f);

        VRKeyboard keyboard = keyboardRoot.AddComponent<VRKeyboard>();
        BuildVRKeyboard(keyboardRoot.transform, keyboard, keyMat, font);

        // Confirm button
        Material confirmMat = GetOrCreateMat("Assets/Materials/ConfirmButton.mat",
            new Color(0.15f, 0.65f, 0.15f), 0f, 0.6f);
        Vector3 confirmSize = new Vector3(1.4f, 0.22f, 0.06f);
        GameObject confirmGo = new GameObject("ConfirmPlayerButton");
        confirmGo.transform.SetParent(lobbyRoot, false);
        confirmGo.transform.position   = new Vector3(0f, 0.45f, 4.2f);
        confirmGo.transform.rotation   = Quaternion.identity;

        CreateButtonBody(confirmGo.transform, confirmSize, confirmMat);
        EnsureSolidCollider(confirmGo.transform.Find("Body")?.gameObject);

        ShooterButton confirmBtn = confirmGo.AddComponent<ShooterButton>();
        confirmBtn.idleColor = new Color(0.15f, 0.65f, 0.15f);
        confirmBtn.hoverColor = new Color(1f, 0.9f, 0.1f);

        // Same label fix as keyboard keys (local -Z, 180° Y, −X scale)
        AttachKeyboardKeyLabel(confirmGo.transform, confirmSize.z, confirmSize.x, font, "START", 0.012f);

        // PlayerSelectUI component
        GameObject selectObj = new GameObject("PlayerSelectUI");
        selectObj.transform.SetParent(lobbyRoot, false);
        selectUI = selectObj.AddComponent<PlayerSelectUI>();
        selectUI.keyboard = keyboard;
        selectUI.nameDisplay = nameDisplay;
        selectUI.hintText = hintText;
        selectUI.listHeaderText = listHeader;
        selectUI.listContainer = listContainer.transform;
        selectUI.listEntryPrefab = listEntryPrefab;
        selectUI.confirmButton = confirmBtn;

        // Floor accent mat in lobby zone
        MakeArenaBox("LobbyFloorAccent", new Vector3(0f, 0.02f, 5.5f),
            new Vector3(8f, 0.02f, 6f),
            GetOrCreateMat("Assets/Materials/LobbyFloor.mat", new Color(0.1f, 0.2f, 0.35f), 0f, 0.6f))
            .transform.SetParent(lobbyRoot, true);

        EditorUtility.SetDirty(lobbyRoot.gameObject);
        Debug.Log("[Shooter] Session lobby created.");
        return lobbyRoot.gameObject;
    }

    static Text CreateLobbyText(Transform parent, string name, string content, Font font,
        int size, FontStyle style, TextAnchor align,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.text = content;
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        return t;
    }

    static GameObject CreatePlayerListEntryPrefab(Material mat, Font font)
    {
        const string path = "Assets/Prefabs/PlayerListEntry.prefab";
        // Force rebuild so label sizing fixes apply
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) AssetDatabase.DeleteAsset(path);

        Vector3 entrySize = new Vector3(0.75f, 0.14f, 0.05f);
        GameObject go = new GameObject("PlayerListEntry");
        CreateButtonBody(go.transform, entrySize, mat);
        go.AddComponent<PlayerListEntry>();
        AttachFrontFaceLabel(go.transform, entrySize.z, entrySize.x, font, "Player", 0.012f);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>Visible cube mesh on a scale-1 root (keeps label sizing independent).</summary>
    static GameObject CreateButtonBody(Transform parent, Vector3 localScale, Material mat)
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(parent, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale    = localScale;
        body.GetComponent<Renderer>().sharedMaterial = mat;
        EnsureSolidCollider(body);
        return body;
    }

    /// <summary>
    /// 3D TextMesh label on the -Z face (toward the player). Renders as real mesh geometry
    /// — always visible in the Scene view and unaffected by parent body scale.
    /// </summary>
    static TextMesh Attach3DLabel(Transform root, float bodyDepth, float bodyWidth, Font font,
        string text, float maxCharSize)
    {
        GameObject labelGo = new GameObject("Label");
        labelGo.transform.SetParent(root, false);
        labelGo.transform.localPosition = new Vector3(0f, 0f, -(bodyDepth * 0.5f + 0.004f));

        // Billboard toward the player spawn — readable, not mirrored
        Vector3 worldPos = labelGo.transform.position;
        Vector3 toPlayer = new Vector3(0f, 1.4f, 3f) - worldPos;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.01f)
            labelGo.transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        labelGo.transform.localScale = Vector3.one;

        // Fit glyph width to button — prevents multi-char labels overlapping edges
        int   len     = Mathf.Max(1, text.Length);
        float fitSize = (bodyWidth * 0.72f) / (len * 0.58f);
        float charSize = Mathf.Min(maxCharSize, fitSize);

        TextMesh tm = labelGo.AddComponent<TextMesh>();
        tm.font           = font;
        tm.text           = text;
        tm.fontSize       = 64;
        tm.characterSize  = charSize;
        tm.anchor         = TextAnchor.MiddleCenter;
        tm.alignment      = TextAlignment.Center;
        tm.color          = Color.white;
        tm.richText       = false;

        MeshRenderer mr = labelGo.GetComponent<MeshRenderer>();
        if (font != null && font.material != null)
            mr.sharedMaterial = font.material;

        return tm;
    }

    static void BuildVRKeyboard(Transform root, VRKeyboard keyboard, Material keyMat, Font font)
    {
        const float keyW = 0.11f;
        const float keyH = 0.11f;
        const float keyD = 0.03f;
        const float gap  = 0.012f;

        string[] row1 = { "Q","W","E","R","T","Y","U","I","O","P" };
        string[] row2 = { "A","S","D","F","G","H","J","K","L" };
        string[] row3 = { "Z","X","C","V","B","N","M" };

        float startX = -((row1.Length - 1) * (keyW + gap)) * 0.5f;
        PlaceKeyRow(root, keyboard, keyMat, font, row1, startX, 0.22f, keyW, keyH, keyD, gap);
        startX = -((row2.Length - 1) * (keyW + gap)) * 0.5f;
        PlaceKeyRow(root, keyboard, keyMat, font, row2, startX, 0.10f, keyW, keyH, keyD, gap);
        startX = -((row3.Length - 1) * (keyW + gap)) * 0.5f;
        PlaceKeyRow(root, keyboard, keyMat, font, row3, startX, -0.02f, keyW, keyH, keyD, gap);

        // Bottom row: SPACE, BACKSPACE, CLEAR
        CreateKeyboardKey(root, keyboard, keyMat, font, "SPACE",
            VRKeyboardKey.KeyAction.Space, ' ',
            new Vector3(-0.22f, -0.16f, 0f), new Vector3(0.42f, keyH, keyD), "SPACE");

        CreateKeyboardKey(root, keyboard, keyMat, font, "BACK",
            VRKeyboardKey.KeyAction.Backspace, '\0',
            new Vector3(0.28f, -0.16f, 0f), new Vector3(0.18f, keyH, keyD), "DEL");

        CreateKeyboardKey(root, keyboard, keyMat, font, "CLEAR",
            VRKeyboardKey.KeyAction.Clear, '\0',
            new Vector3(0.52f, -0.16f, 0f), new Vector3(0.14f, keyH, keyD), "CLR");
    }

    static void PlaceKeyRow(Transform root, VRKeyboard keyboard, Material keyMat, Font font,
        string[] keys, float startX, float y, float w, float h, float d, float gap)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            float x = startX + i * (w + gap);
            CreateKeyboardKey(root, keyboard, keyMat, font, "Key_" + keys[i],
                VRKeyboardKey.KeyAction.Character, keys[i][0],
                new Vector3(x, y, 0f), new Vector3(w, h, d), keys[i]);
        }
    }

    static void CreateKeyboardKey(Transform root, VRKeyboard keyboard, Material keyMat, Font font,
        string goName, VRKeyboardKey.KeyAction action, char ch,
        Vector3 localPos, Vector3 size, string label)
    {
        // Root keeps scale 1 so 3D labels are not crushed by key dimensions
        GameObject keyRoot = new GameObject(goName);
        keyRoot.transform.SetParent(root, false);
        keyRoot.transform.localPosition = localPos;

        CreateButtonBody(keyRoot.transform, size, keyMat);

        VRKeyboardKey key = keyRoot.AddComponent<VRKeyboardKey>();
        key.keyboard  = keyboard;
        key.action    = action;
        key.character = ch;

        float maxChar = action == VRKeyboardKey.KeyAction.Character ? 0.011f : 0.009f;
        AttachKeyboardKeyLabel(keyRoot.transform, size.z, size.x, font, label, maxChar);
    }

    /// <summary>Label on +Z face for objects whose root already faces the player.</summary>
    static TextMesh AttachFrontFaceLabel(Transform root, float bodyDepth, float bodyWidth,
        Font font, string text, float maxCharSize)
    {
        GameObject labelGo = new GameObject("Label");
        labelGo.transform.SetParent(root, false);
        labelGo.transform.localPosition = new Vector3(0f, 0f, bodyDepth * 0.5f + 0.004f);
        // 180° Y puts the TextMesh -Z face toward the player.
        // No -X scale needed: the list container is already rotated 180° Y, so its +X = world -X,
        // and the double-flip gives correct left-to-right text without an extra mirror step.
        labelGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        labelGo.transform.localScale    = Vector3.one;

        int   len      = Mathf.Max(1, text.Length);
        float fitSize  = (bodyWidth * 0.72f) / (len * 0.58f);
        float charSize = Mathf.Min(maxCharSize, fitSize);

        TextMesh tm = labelGo.AddComponent<TextMesh>();
        tm.font          = font;
        tm.text          = text;
        tm.fontSize      = 64;
        tm.characterSize = charSize;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.color         = Color.white;
        tm.richText      = false;

        MeshRenderer mr = labelGo.GetComponent<MeshRenderer>();
        if (font != null && font.material != null)
            mr.sharedMaterial = font.material;

        return tm;
    }

    /// <summary>
    /// Keyboard-only label: local -Z face + 180° Y + negative X scale (avoids mirrored glyphs).
    /// </summary>
    static TextMesh AttachKeyboardKeyLabel(Transform keyRoot, float bodyDepth, float bodyWidth,
        Font font, string text, float maxCharSize)
    {
        GameObject labelGo = new GameObject("Label");
        labelGo.transform.SetParent(keyRoot, false);
        labelGo.transform.localPosition = new Vector3(0f, 0f, -(bodyDepth * 0.5f + 0.004f));
        labelGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        labelGo.transform.localScale    = new Vector3(-1f, 1f, 1f);

        int   len      = Mathf.Max(1, text.Length);
        float fitSize  = (bodyWidth * 0.72f) / (len * 0.58f);
        float charSize = Mathf.Min(maxCharSize, fitSize);

        TextMesh tm = labelGo.AddComponent<TextMesh>();
        tm.font          = font;
        tm.text          = text;
        tm.fontSize      = 64;
        tm.characterSize = charSize;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.color         = Color.white;
        tm.richText      = false;

        MeshRenderer mr = labelGo.GetComponent<MeshRenderer>();
        if (font != null && font.material != null)
            mr.sharedMaterial = font.material;

        return tm;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Round Manager
    // ═════════════════════════════════════════════════════════════════════════════

    static GameObject CreateRoundManager()
    {
        GameObject go = new GameObject("RoundManager");
        go.AddComponent<ShooterRoundManager>();
        EditorUtility.SetDirty(go);
        return go;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // HUD (world-space canvas attached to center eye)
    // ═════════════════════════════════════════════════════════════════════════════

    static GameObject CreateHUD(OVRCameraRig rig)
    {
        Transform eye = rig != null ? rig.centerEyeAnchor : null;

        // Canvas root
        GameObject canvasObj = new GameObject("ShooterHUD");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        RectTransform crt = canvasObj.GetComponent<RectTransform>();
        crt.sizeDelta     = new Vector2(800f, 600f);
        crt.localScale    = Vector3.one * 0.001f; // 1 px = 1 mm in world

        if (eye != null)
        {
            canvasObj.transform.SetParent(eye, false);
            canvasObj.transform.localPosition = new Vector3(0f, 0f, 0.8f);
            canvasObj.transform.localRotation = Quaternion.identity;
        }

        Font font = GetBuiltinFont();

        // ── Gameplay HUD group (hidden during player select) ────────────────────
        GameObject gameplayRoot = new GameObject("GameplayHUD");
        gameplayRoot.transform.SetParent(canvasObj.transform, false);
        RectTransform gameplayRt = gameplayRoot.AddComponent<RectTransform>();
        gameplayRt.anchorMin = Vector2.zero;
        gameplayRt.anchorMax = Vector2.one;
        gameplayRt.offsetMin = Vector2.zero;
        gameplayRt.offsetMax = Vector2.zero;

        // ── Timer (upper-centre of view — kept in VR FOV, hidden until round starts) ──
        GameObject timerObj = new GameObject("TimerText");
        timerObj.transform.SetParent(gameplayRoot.transform, false);
        Text timerText = timerObj.AddComponent<Text>();
        timerText.text      = "30";
        timerText.font      = font;
        timerText.fontSize  = 72;
        timerText.fontStyle = FontStyle.Bold;
        timerText.alignment = TextAnchor.MiddleCenter;
        timerText.color     = Color.white;
        timerObj.SetActive(false);

        RectTransform trt = timerObj.GetComponent<RectTransform>();
        trt.anchorMin        = new Vector2(0.5f, 0.5f);
        trt.anchorMax        = new Vector2(0.5f, 0.5f);
        trt.pivot            = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(0f, 130f);
        trt.sizeDelta        = new Vector2(220f, 90f);

        // ── Player name (top-left, shown during gameplay) ───────────────────────
        GameObject playerNameObj = new GameObject("PlayerNameText");
        playerNameObj.transform.SetParent(gameplayRoot.transform, false);
        Text playerNameText = playerNameObj.AddComponent<Text>();
        playerNameText.text      = "";
        playerNameText.font      = font;
        playerNameText.fontSize  = 28;
        playerNameText.fontStyle = FontStyle.Bold;
        playerNameText.alignment = TextAnchor.UpperLeft;
        playerNameText.color     = new Color(0.5f, 0.9f, 1f);
        playerNameObj.SetActive(false);

        RectTransform pnRt = playerNameObj.GetComponent<RectTransform>();
        pnRt.anchorMin        = new Vector2(0f, 1f);
        pnRt.anchorMax        = new Vector2(0f, 1f);
        pnRt.pivot            = new Vector2(0f, 1f);
        pnRt.anchoredPosition = new Vector2(20f, -20f);
        pnRt.sizeDelta        = new Vector2(400f, 40f);

        // ── Score text (centre, just below timer) ───────────────────────────────
        GameObject scoreObj = new GameObject("ScoreText");
        scoreObj.transform.SetParent(gameplayRoot.transform, false);
        Text scoreText = scoreObj.AddComponent<Text>();
        scoreText.text      = "0";
        scoreText.font      = font;
        scoreText.fontSize  = 42;
        scoreText.alignment = TextAnchor.MiddleCenter;
        scoreText.color     = Color.white;

        RectTransform srt = scoreObj.GetComponent<RectTransform>();
        srt.anchorMin        = new Vector2(0.5f, 0.5f);
        srt.anchorMax        = new Vector2(0.5f, 0.5f);
        srt.pivot            = new Vector2(0.5f, 0.5f);
        srt.anchoredPosition = new Vector2(0f, 70f);
        srt.sizeDelta        = new Vector2(320f, 70f);

        // ── Stats panel (two columns: stats left, agent verdict right) ─────────
        GameObject statsPanel = new GameObject("StatsPanel");
        statsPanel.transform.SetParent(gameplayRoot.transform, false);
        Image panelBg = statsPanel.AddComponent<Image>();
        panelBg.color = new Color(0f, 0f, 0f, 0.72f);
        statsPanel.SetActive(false);

        RectTransform sprt = statsPanel.GetComponent<RectTransform>();
        sprt.anchorMin        = new Vector2(0.5f, 0.5f);
        sprt.anchorMax        = new Vector2(0.5f, 0.5f);
        sprt.pivot            = new Vector2(0.5f, 0.5f);
        sprt.anchoredPosition = new Vector2(-55f, -10f);
        sprt.sizeDelta        = new Vector2(720f, 420f);

        GameObject statsTextObj = new GameObject("StatsText");
        statsTextObj.transform.SetParent(statsPanel.transform, false);
        Text statsText = statsTextObj.AddComponent<Text>();
        statsText.font      = font;
        statsText.fontSize  = 18;
        statsText.alignment = TextAnchor.UpperLeft;
        statsText.color     = Color.white;
        statsText.text      = "";
        statsText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statsText.verticalOverflow   = VerticalWrapMode.Overflow;

        RectTransform strt = statsTextObj.GetComponent<RectTransform>();
        strt.anchorMin = new Vector2(0f, 0f);
        strt.anchorMax = new Vector2(0.48f, 1f);
        strt.offsetMin = new Vector2(12f, 12f);
        strt.offsetMax = new Vector2(-6f, -12f);

        GameObject agentTextObj = new GameObject("AgentText");
        agentTextObj.transform.SetParent(statsPanel.transform, false);
        Text agentText = agentTextObj.AddComponent<Text>();
        agentText.font      = font;
        agentText.fontSize  = 18;
        agentText.alignment = TextAnchor.UpperLeft;
        agentText.color     = new Color(0.82f, 0.94f, 1f);
        agentText.text      = "";
        agentText.horizontalOverflow = HorizontalWrapMode.Wrap;
        agentText.verticalOverflow   = VerticalWrapMode.Overflow;

        RectTransform art = agentTextObj.GetComponent<RectTransform>();
        art.anchorMin = new Vector2(0.52f, 0f);
        art.anchorMax = new Vector2(1f, 1f);
        art.offsetMin = new Vector2(6f, 12f);
        art.offsetMax = new Vector2(-12f, -12f);

        // ── Stamina bar (bottom-centre) ─────────────────────────────────────────
        GameObject barBg = new GameObject("StaminaBg");
        barBg.transform.SetParent(gameplayRoot.transform, false);
        Image bgImg = barBg.AddComponent<Image>();
        bgImg.color = new Color(0.08f, 0.08f, 0.08f, 0.75f);

        RectTransform bgRt = barBg.GetComponent<RectTransform>();
        bgRt.anchorMin        = new Vector2(0.25f, 0f);
        bgRt.anchorMax        = new Vector2(0.75f, 0f);
        bgRt.pivot            = new Vector2(0.5f, 0f);
        bgRt.anchoredPosition = new Vector2(0f, 18f);
        bgRt.sizeDelta        = new Vector2(0f, 26f);

        Sprite barSprite = GetUISprite();

        GameObject barFill = new GameObject("StaminaFill");
        barFill.transform.SetParent(barBg.transform, false);
        Image fillImg = barFill.AddComponent<Image>();
        fillImg.sprite     = barSprite;
        fillImg.type       = Image.Type.Simple;
        fillImg.color      = new Color(0.15f, 0.55f, 1f);

        // Width driven by anchorMax.x in ShooterHUD (true progress bar, drains left→right)
        RectTransform fillRt = barFill.GetComponent<RectTransform>();
        fillRt.anchorMin        = Vector2.zero;
        fillRt.anchorMax        = Vector2.one;
        fillRt.pivot            = new Vector2(0f, 0.5f);
        fillRt.offsetMin        = Vector2.zero;
        fillRt.offsetMax        = Vector2.zero;

        if (barSprite != null)
            bgImg.sprite = barSprite;

        // ── ShooterHUD component ────────────────────────────────────────────────
        ShooterHUD hud = canvasObj.AddComponent<ShooterHUD>();
        hud.staminaFill     = fillImg;
        hud.scoreText       = scoreText;
        hud.playerNameText  = playerNameText;
        hud.timerText       = timerText;
        hud.statsText       = statsText;
        hud.agentText       = agentText;
        hud.statsPanel      = statsPanel;
        hud.gameplayHudRoot = gameplayRoot;

        EditorUtility.SetDirty(canvasObj);
        return canvasObj;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Utilities
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>Renderer bounds in the model root's local space.</summary>
    static Bounds CalcRendererBoundsLocal(Transform root)
    {
        Bounds? merged = null;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            Bounds wb = r.bounds;
            Vector3[] corners =
            {
                new Vector3(wb.min.x, wb.min.y, wb.min.z),
                new Vector3(wb.min.x, wb.min.y, wb.max.z),
                new Vector3(wb.min.x, wb.max.y, wb.min.z),
                new Vector3(wb.min.x, wb.max.y, wb.max.z),
                new Vector3(wb.max.x, wb.min.y, wb.min.z),
                new Vector3(wb.max.x, wb.min.y, wb.max.z),
                new Vector3(wb.max.x, wb.max.y, wb.min.z),
                new Vector3(wb.max.x, wb.max.y, wb.max.z),
            };

            foreach (Vector3 corner in corners)
            {
                Vector3 local = root.InverseTransformPoint(corner);
                if (merged == null)
                    merged = new Bounds(local, Vector3.zero);
                else
                {
                    Bounds b = merged.Value;
                    b.Encapsulate(local);
                    merged = b;
                }
            }
        }

        return merged ?? new Bounds(Vector3.zero, Vector3.one * 0.05f);
    }

    static Sprite GetUISprite()
    {
        Sprite s = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (s == null) s = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        return s;
    }

    static Font GetBuiltinFont()
    {
        // Unity 2022+ renamed the built-in font; try both paths
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return f;
    }

    static Material GetOrCreateMat(string path, Color color, float metallic, float smoothness)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        mat = new Material(Shader.Find("Standard")) { color = color };
        mat.SetFloat("_Metallic",   metallic);
        mat.SetFloat("_Glossiness", smoothness);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Skill Prompt UI (VR panel for new player skill selection)
    // ═════════════════════════════════════════════════════════════════════════════

    // ═════════════════════════════════════════════════════════════════════════════
    // Difficulty feedback UI (5 shootable buttons along the firing line)
    // ═════════════════════════════════════════════════════════════════════════════

    static DifficultyFeedbackUI CreateDifficultyFeedbackUI(Transform parent)
    {
        GameObject root = new GameObject("DifficultyFeedback");
        root.transform.SetParent(parent, false);
        DifficultyFeedbackUI ui = root.AddComponent<DifficultyFeedbackUI>();

        // Buttons live under a child that toggles on/off; the component itself
        // stays active so its Awake() wires the button listeners.
        GameObject buttons = new GameObject("Buttons");
        buttons.transform.SetParent(root.transform, false);

        Font font = GetBuiltinFont();

        // Row of five buttons at the firing line (z = 7.5), just above the barrier.
        const float z = 7.5f, y = 1.02f, spacing = 0.92f;
        Color blue  = new Color(0.20f, 0.45f, 0.85f);
        Color teal  = new Color(0.20f, 0.65f, 0.70f);
        Color green = new Color(0.15f, 0.70f, 0.25f);
        Color amber = new Color(0.85f, 0.55f, 0.12f);
        Color red   = new Color(0.80f, 0.20f, 0.15f);

        ui.tooEasyButton = CreateFeedbackButton(buttons.transform, "TooEasyBtn", "TOO\nEASY",
            blue,  new Vector3(-2f * spacing, y, z), font);
        ui.easyButton    = CreateFeedbackButton(buttons.transform, "EasyBtn", "EASY",
            teal,  new Vector3(-1f * spacing, y, z), font);
        ui.perfectButton = CreateFeedbackButton(buttons.transform, "PerfectBtn", "PERFECT",
            green, new Vector3(0f,            y, z), font);
        ui.hardButton    = CreateFeedbackButton(buttons.transform, "HardBtn", "HARD",
            amber, new Vector3(1f * spacing,  y, z), font);
        ui.tooHardButton = CreateFeedbackButton(buttons.transform, "TooHardBtn", "TOO\nHARD",
            red,   new Vector3(2f * spacing,  y, z), font);

        ui.panelRoot = buttons;
        return ui;
    }

    static ShooterButton CreateFeedbackButton(Transform parent, string goName, string label,
        Color color, Vector3 worldPos, Font font)
    {
        GameObject btnGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        btnGo.name = goName;
        btnGo.transform.SetParent(parent, true);
        btnGo.transform.position   = worldPos;
        btnGo.transform.localScale = new Vector3(0.62f, 0.38f, 0.12f);

        Material mat = new Material(Shader.Find("Standard")) { color = color };
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * 0.35f);
        btnGo.GetComponent<Renderer>().sharedMaterial = mat;
        EnsureSolidCollider(btnGo);

        // Label on the face toward the player (player stands at z=3 looking +Z).
        // Identity rotation matches the confirmed-readable orientation used elsewhere.
        GameObject lblGo = new GameObject("Label");
        lblGo.transform.SetParent(btnGo.transform, false);
        lblGo.transform.localPosition = new Vector3(0f, 0f, -0.6f);
        lblGo.transform.localRotation = Quaternion.identity;
        lblGo.transform.localScale    = new Vector3(0.06f, 0.1f, 0.5f);

        TextMesh tm = lblGo.AddComponent<TextMesh>();
        tm.font          = font;
        tm.text          = label;
        tm.fontSize      = 64;
        tm.characterSize = 0.1f;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.color         = Color.white;
        if (font != null) lblGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

        return btnGo.AddComponent<ShooterButton>();
    }

    static void CreateSkillPromptUI(GameObject promptObj, SkillPromptUI promptUI, OVRCameraRig rig)
    {
        // OVRCameraRig is placed at Z=3 facing +Z.  The lobby canvas is at Z=4.74 with 180° Y
        // rotation so it faces -Z toward the player.  The skill prompt must sit at the same
        // relative depth so the player sees it in front of them after the lobby hides.
        promptObj.transform.position = new Vector3(0f, 1.55f, 4.5f);

        // Create a world-space canvas for the prompt.
        // 180° Y rotation makes the canvas face the player (who looks toward +Z from Z=3).
        // Negative X scale un-mirrors text rendered on a 180°-rotated canvas (same trick as LobbyCanvas).
        GameObject canvasObj = new GameObject("PromptCanvas");
        canvasObj.transform.SetParent(promptObj.transform, false);
        canvasObj.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        canvasObj.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta     = new Vector2(600, 400);
        canvasRect.localScale    = new Vector3(0.002f, 0.002f, 0.002f); // positive; flip via ContentFlipRoot
        canvasRect.localPosition = Vector3.zero;

        // ContentFlipRoot cancels the glyph-mirror from the canvas's 180° Y rotation.
        GameObject skillFlipObj = new GameObject("ContentFlipRoot");
        skillFlipObj.transform.SetParent(canvasObj.transform, false);
        RectTransform skillFlipRt = skillFlipObj.AddComponent<RectTransform>();
        skillFlipRt.anchorMin  = Vector2.zero;
        skillFlipRt.anchorMax  = Vector2.one;
        skillFlipRt.offsetMin  = Vector2.zero;
        skillFlipRt.offsetMax  = Vector2.zero;
        skillFlipRt.localScale = new Vector3(-1f, 1f, 1f);

        // Background panel
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(skillFlipObj.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.1f, 0.1f, 0.15f, 0.92f);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        Font font = GetBuiltinFont();

        // Title
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(skillFlipObj.transform, false);
        Text title = titleObj.AddComponent<Text>();
        title.font      = font;
        title.fontSize  = 36;
        title.alignment = TextAnchor.MiddleCenter;
        title.color     = Color.white;
        title.text      = "Welcome!";
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin     = new Vector2(0.05f, 0.75f);
        titleRect.anchorMax     = new Vector2(0.95f, 0.95f);
        titleRect.offsetMin     = Vector2.zero;
        titleRect.offsetMax     = Vector2.zero;
        promptUI.titleText = title;

        // Description
        GameObject descObj = new GameObject("Description");
        descObj.transform.SetParent(skillFlipObj.transform, false);
        Text desc = descObj.AddComponent<Text>();
        desc.font      = font;
        desc.fontSize  = 22;
        desc.alignment = TextAnchor.MiddleCenter;
        desc.color     = new Color(0.8f, 0.8f, 0.85f);
        desc.text      = "How experienced are you?";
        RectTransform descRect = descObj.GetComponent<RectTransform>();
        descRect.anchorMin     = new Vector2(0.05f, 0.55f);
        descRect.anchorMax     = new Vector2(0.95f, 0.75f);
        descRect.offsetMin     = Vector2.zero;
        descRect.offsetMax     = Vector2.zero;
        promptUI.descriptionText = desc;

        // Buttons — parented inside the flip root so their labels are also un-mirrored.
        promptUI.beginnerButton     = CreatePromptButton(skillFlipObj.transform, "Beginner",
            new Color(0.2f, 0.6f, 0.3f), new Vector2(0.05f, 0.15f), new Vector2(0.32f, 0.48f), font);
        promptUI.intermediateButton = CreatePromptButton(skillFlipObj.transform, "Intermediate",
            new Color(0.5f, 0.5f, 0.15f), new Vector2(0.35f, 0.15f), new Vector2(0.65f, 0.48f), font);
        promptUI.advancedButton     = CreatePromptButton(skillFlipObj.transform, "Advanced",
            new Color(0.6f, 0.2f, 0.2f), new Vector2(0.68f, 0.15f), new Vector2(0.95f, 0.48f), font);

        // panelRoot points to the canvas CHILD, not to promptObj itself.
        // This prevents SkillPromptUI.Awake() from deactivating its own GameObject
        // (which would prevent Start() from running and wiring the button listeners).
        promptUI.panelRoot = canvasObj;
        promptObj.SetActive(false);
    }

    // Canvas logical dimensions — must match the sizeDelta set in CreateSkillPromptUI.
    const float PromptCanvasW = 600f;
    const float PromptCanvasH = 400f;

    static ShooterButton CreatePromptButton(Transform parent, string label,
        Color color, Vector2 anchorMin, Vector2 anchorMax, Font font)
    {
        GameObject btnObj = new GameObject(label + "Btn");
        btnObj.transform.SetParent(parent, false);

        Image img = btnObj.AddComponent<Image>();
        img.color = color;

        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Label text
        GameObject lblObj = new GameObject("Label");
        lblObj.transform.SetParent(btnObj.transform, false);
        Text txt = lblObj.AddComponent<Text>();
        txt.font      = font;
        txt.fontSize  = 24;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color     = Color.white;
        txt.text      = label;
        RectTransform lblRt = lblObj.GetComponent<RectTransform>();
        lblRt.anchorMin = Vector2.zero;
        lblRt.anchorMax = Vector2.one;
        lblRt.offsetMin = Vector2.zero;
        lblRt.offsetMax = Vector2.zero;

        // Collider for VR pointer / trigger interaction.
        // rt.rect.width/height are 0 at editor-creation time (layout hasn't run),
        // so we compute the size directly from the anchor fractions × canvas dimensions.
        float colW = (anchorMax.x - anchorMin.x) * PromptCanvasW;
        float colH = (anchorMax.y - anchorMin.y) * PromptCanvasH;
        BoxCollider col = btnObj.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(colW, colH, 20f);

        ShooterButton btn = btnObj.AddComponent<ShooterButton>();
        return btn;
    }

    public static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            AssetDatabase.CreateFolder(parent, child);
    }

    public static void AddSceneToBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == scenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}

/// <summary>
/// Editor-only debug helpers accessible from the MGU menu bar.
/// </summary>
static class MGUDebugMenus
{
    [UnityEditor.MenuItem("MGU/Debug/Clear All Player Data (Editor Device)")]
    static void ClearAllPlayerData()
    {
        if (!UnityEditor.EditorUtility.DisplayDialog(
                "Clear All Player Data",
                "This will delete saved_players.json and all shooter_profiles on the currently " +
                "active device (Editor / connected headset via USB).\n\nContinue?",
                "Delete", "Cancel"))
            return;

        PlayerRegistry.ClearAll();
        UnityEditor.EditorUtility.DisplayDialog("Done",
            "Player data cleared.\nPath: " + UnityEngine.Application.persistentDataPath, "OK");
    }
}
