using System;
using System.IO;
using UnityEngine;

public enum PlayerSkillLevel { Beginner = 0, Intermediate = 1, Advanced = 2 }

[Serializable]
public class PlayerSkillProfile
{
    public string playerName;
    public int    skillLevel;
    public int    roundsPlayed;
    public float  lifetimeHitRate;
    public float  lifetimeAvgTimeToHit;

    // Q-table: [stateIndex * ACTION_COUNT + actionIndex] → expected reward
    public float[] qTable;

    public float epsilon;

    // Global EMAs
    public float emaHitRate;
    public float emaTimeToHit;
    public float emaEngagement;

    // Per-type EMAs
    public float emaHitRateStationary, emaHitRateMoving, emaHitRateErratic;
    public float emaTTHStationary, emaTTHMoving, emaTTHErratic;
    public float emaPointsStationary, emaPointsMoving, emaPointsErratic;
    public float emaHitRateRotating;
    public float emaPointsPerTarget;

    const int STATE_COUNT  = 27;
    const int ACTION_COUNT = 36;
    const float INITIAL_EPSILON = 0.70f;
    const float MIN_EPSILON     = 0.06f;
    const float EPSILON_DECAY   = 0.975f;
    const float LEARNING_RATE   = 0.3f;
    const float DISCOUNT        = 0.7f;
    const float EMA_ALPHA       = 0.4f;

    public PlayerSkillProfile() { }

    // ── Action encoding ──────────────────────────────────────────────────────

    public static void DecodeAction(int action, out int emphasisType, out int rotationLevel, out int spawnPace)
    {
        emphasisType = action / 12;
        rotationLevel = (action % 12) / 3;
        spawnPace = action % 3;
    }

    // ── Factory ──────────────────────────────────────────────────────────────

    public static PlayerSkillProfile CreateNew(string name, PlayerSkillLevel skill)
    {
        var p = new PlayerSkillProfile
        {
            playerName           = name,
            skillLevel           = (int)skill,
            roundsPlayed         = 0,
            lifetimeHitRate      = 0f,
            lifetimeAvgTimeToHit = 0f,
            epsilon              = INITIAL_EPSILON,
            emaHitRate           = GetDefaultHitRate(skill),
            emaTimeToHit         = GetDefaultTimeToHit(skill),
            emaEngagement        = 1.5f,
            qTable               = new float[STATE_COUNT * ACTION_COUNT]
        };

        SetDefaultPerTypeEMAs(p, skill);
        InitializeQTableFromProfile(p, skill);
        return p;
    }

    static void SetDefaultPerTypeEMAs(PlayerSkillProfile p, PlayerSkillLevel skill)
    {
        switch (skill)
        {
            case PlayerSkillLevel.Beginner:
                // Seeded near the expected values for Naive player with easy targets.
                // Stationary strong, erratic very weak — agent should learn Stationary+None.
                p.emaHitRateStationary = 0.75f;
                p.emaHitRateMoving     = 0.42f;
                p.emaHitRateErratic    = 0.22f;
                p.emaTTHStationary     = 1.2f;
                p.emaTTHMoving         = 2.0f;
                p.emaTTHErratic        = 3.5f;
                p.emaPointsStationary  = 1.2f;
                p.emaPointsMoving      = 0.8f;
                p.emaPointsErratic     = 0.4f;
                p.emaHitRateRotating   = 0.25f;
                p.emaPointsPerTarget   = 0.9f;
                break;
            case PlayerSkillLevel.Intermediate:
                // Seeded near expected values for Average player.
                // Strong on stationary, moderate on moving — agent should avoid rotation.
                p.emaHitRateStationary = 0.88f;
                p.emaHitRateMoving     = 0.55f;
                p.emaHitRateErratic    = 0.28f;
                p.emaTTHStationary     = 0.7f;
                p.emaTTHMoving         = 1.0f;
                p.emaTTHErratic        = 1.8f;
                p.emaPointsStationary  = 3.5f;
                p.emaPointsMoving      = 2.0f;
                p.emaPointsErratic     = 1.0f;
                p.emaHitRateRotating   = 0.35f;
                p.emaPointsPerTarget   = 2.2f;
                break;
            case PlayerSkillLevel.Advanced:
                // Seeded near expected values for Expert player without rotation.
                // All hit rates appear high — agent must discover fast rotation is the lever.
                p.emaHitRateStationary = 0.97f;
                p.emaHitRateMoving     = 0.90f;
                p.emaHitRateErratic    = 0.76f;
                p.emaTTHStationary     = 0.15f;
                p.emaTTHMoving         = 0.25f;
                p.emaTTHErratic        = 0.40f;
                p.emaPointsStationary  = 7.5f;
                p.emaPointsMoving      = 6.5f;
                p.emaPointsErratic     = 5.5f;
                p.emaHitRateRotating   = 0.66f;
                p.emaPointsPerTarget   = 6.5f;
                break;
            default:
                goto case PlayerSkillLevel.Intermediate;
        }
    }

    static void InitializeQTableFromProfile(PlayerSkillProfile p, PlayerSkillLevel skill)
    {
        // Beginner:     Stationary(0) + None(0) + Medium pace(1)  → 0*12+0*3+1 = 1
        // Intermediate: Stationary(0) + None(0) + Medium pace(1)  → 1  (same start, learns to Moving)
        // Advanced:     Erratic(2)   + Fast(3)  + Fast pace(0)   → 2*12+3*3+0 = 33
        int preferredAction;
        switch (skill)
        {
            case PlayerSkillLevel.Beginner:     preferredAction = 1;  break;
            case PlayerSkillLevel.Intermediate: preferredAction = 1;  break;
            case PlayerSkillLevel.Advanced:     preferredAction = 33; break;
            default:                            preferredAction = 1;  break;
        }

        DecodeAction(preferredAction, out int pType, out int pRot, out int pPace);

        for (int s = 0; s < STATE_COUNT; s++)
        {
            for (int a = 0; a < ACTION_COUNT; a++)
            {
                DecodeAction(a, out int aType, out int aRot, out int aPace);
                float dist = Mathf.Abs(aType - pType) + Mathf.Abs(aRot - pRot) * 0.5f + Mathf.Abs(aPace - pPace) * 0.7f;
                p.qTable[s * ACTION_COUNT + a] = Mathf.Max(0f, 0.5f - dist * 0.08f);
            }
        }
    }

    static float GetDefaultHitRate(PlayerSkillLevel skill)
    {
        switch (skill)
        {
            case PlayerSkillLevel.Beginner:     return 0.46f;  // Naive + Stat+None effective rate
            case PlayerSkillLevel.Intermediate: return 0.57f;  // Average + Stat+None effective rate
            case PlayerSkillLevel.Advanced:     return 0.88f;  // Expert before rotation is applied
            default:                            return 0.5f;
        }
    }

    static float GetDefaultTimeToHit(PlayerSkillLevel skill)
    {
        switch (skill)
        {
            case PlayerSkillLevel.Beginner:     return 1.2f;   // slow fire rate, often 2nd shot
            case PlayerSkillLevel.Intermediate: return 0.7f;   // medium fire rate
            case PlayerSkillLevel.Advanced:     return 0.15f;  // expert reacts very fast
            default:                            return 1.0f;
        }
    }

    // ── State discretization ─────────────────────────────────────────────────

    public int GetCurrentState()
    {
        float composite = emaHitRate * 0.6f + Mathf.Clamp01(emaPointsPerTarget / 10f) * 0.4f;
        int perfBucket = composite < 0.30f ? 0 : (composite < 0.60f ? 1 : 2);
        int paceBucket = emaTimeToHit > 3.5f ? 0 : (emaTimeToHit > 1.5f ? 1 : 2);
        int weakest = ComputeWeakestType();
        return perfBucket * 9 + paceBucket * 3 + weakest;
    }

    int ComputeWeakestType()
    {
        float statHR = emaHitRateStationary;
        float movHR = emaHitRateMoving;
        float errHR = emaHitRateErratic;

        float min = Mathf.Min(statHR, movHR, errHR);
        float max = Mathf.Max(statHR, movHR, errHR);

        if (max - min < 0.15f)
        {
            float minPts = Mathf.Min(emaPointsStationary, emaPointsMoving, emaPointsErratic);
            if (emaPointsStationary <= minPts) return 0;
            if (emaPointsMoving <= minPts) return 1;
            return 2;
        }

        if (statHR <= min) return 0;
        if (movHR <= min) return 1;
        return 2;
    }

    // ── Action selection (epsilon-greedy) ────────────────────────────────────

    public int SelectAction()
    {
        if (UnityEngine.Random.value < epsilon)
            return UnityEngine.Random.Range(0, ACTION_COUNT);

        int state = GetCurrentState();
        int bestAction = 0;
        float bestQ = float.NegativeInfinity;
        for (int a = 0; a < ACTION_COUNT; a++)
        {
            float q = qTable[state * ACTION_COUNT + a];
            if (q > bestQ) { bestQ = q; bestAction = a; }
        }
        return bestAction;
    }

    // ── Learning update (after each round) ───────────────────────────────────

    public void UpdateAfterRound(
        float hitRate, float avgTimeToHit, float shotsPerSec,
        float hitRateStat, float hitRateMov, float hitRateErr,
        float tthStat, float tthMov, float tthErr,
        float ptsStat, float ptsMov, float ptsErr,
        float hitRateRotating, float pointsPerTarget,
        int actionTaken, float reward)
    {
        roundsPlayed++;

        // Global EMAs
        emaHitRate    = Mathf.Lerp(emaHitRate,    hitRate,      EMA_ALPHA);
        emaTimeToHit  = Mathf.Lerp(emaTimeToHit,  avgTimeToHit, EMA_ALPHA);
        emaEngagement = Mathf.Lerp(emaEngagement, shotsPerSec,  EMA_ALPHA);

        // Per-type EMAs
        emaHitRateStationary = Mathf.Lerp(emaHitRateStationary, hitRateStat, EMA_ALPHA);
        emaHitRateMoving     = Mathf.Lerp(emaHitRateMoving,     hitRateMov,  EMA_ALPHA);
        emaHitRateErratic    = Mathf.Lerp(emaHitRateErratic,    hitRateErr,  EMA_ALPHA);
        emaTTHStationary     = Mathf.Lerp(emaTTHStationary,     tthStat,     EMA_ALPHA);
        emaTTHMoving         = Mathf.Lerp(emaTTHMoving,         tthMov,      EMA_ALPHA);
        emaTTHErratic        = Mathf.Lerp(emaTTHErratic,        tthErr,      EMA_ALPHA);
        emaPointsStationary  = Mathf.Lerp(emaPointsStationary,  ptsStat,     EMA_ALPHA);
        emaPointsMoving      = Mathf.Lerp(emaPointsMoving,      ptsMov,      EMA_ALPHA);
        emaPointsErratic     = Mathf.Lerp(emaPointsErratic,     ptsErr,      EMA_ALPHA);
        emaHitRateRotating   = Mathf.Lerp(emaHitRateRotating,   hitRateRotating, EMA_ALPHA);
        emaPointsPerTarget   = Mathf.Lerp(emaPointsPerTarget,   pointsPerTarget, EMA_ALPHA);

        // Lifetime stats
        lifetimeHitRate      = Mathf.Lerp(lifetimeHitRate, hitRate, 1f / roundsPlayed);
        lifetimeAvgTimeToHit = Mathf.Lerp(lifetimeAvgTimeToHit, avgTimeToHit, 1f / roundsPlayed);

        // Q-learning Bellman update
        int state = GetCurrentState();
        int idx = state * ACTION_COUNT + actionTaken;

        int nextState = GetCurrentState();
        float maxNextQ = float.NegativeInfinity;
        for (int a = 0; a < ACTION_COUNT; a++)
        {
            float q = qTable[nextState * ACTION_COUNT + a];
            if (q > maxNextQ) maxNextQ = q;
        }

        qTable[idx] += LEARNING_RATE * (reward + DISCOUNT * maxNextQ - qTable[idx]);

        // Decay exploration
        epsilon = Mathf.Max(MIN_EPSILON, epsilon * EPSILON_DECAY);
    }

    // ── Reward computation ───────────────────────────────────────────────────

    public static float ComputeFlowReward(float hitRate, float avgTimeToHit, float avgPointsPerTarget,
        float hitRateStat, float hitRateMov, float hitRateErr)
    {
        float reward = 0f;

        float hitDist = Mathf.Abs(hitRate - 0.55f);
        if (hitDist < 0.10f) reward += 2.0f;
        else if (hitDist < 0.15f) reward += 1.0f;
        else if (hitDist < 0.25f) reward += 0.3f;
        else reward -= 1.0f;

        float timeDist = Mathf.Abs(avgTimeToHit - 2.5f);
        if (timeDist < 0.5f) reward += 1.0f;
        else if (timeDist < 1.5f) reward += 0.3f;
        else reward -= 0.5f;

        float ptsDist = Mathf.Abs(avgPointsPerTarget - 4f);
        if (ptsDist < 1f) reward += 0.5f;
        else if (ptsDist < 2f) reward += 0.2f;
        else reward -= 0.3f;

        float typeRange = Mathf.Max(hitRateStat, hitRateMov, hitRateErr) -
                          Mathf.Min(hitRateStat, hitRateMov, hitRateErr);
        if (typeRange < 0.15f) reward += 0.3f;
        else if (typeRange > 0.35f) reward -= 0.3f;

        return reward;
    }

    // ── Explainability ───────────────────────────────────────────────────────

    public static string ExplainActionChange(int prevAction, int newAction)
    {
        DecodeAction(prevAction, out int pType, out int pRot, out int pPace);
        DecodeAction(newAction, out int nType, out int nRot, out int nPace);

        var parts = new System.Collections.Generic.List<string>();

        string[] typeNames = {"Stationary", "Moving", "Erratic"};
        string[] rotNames = {"No rotation", "Slow rotation", "Medium rotation", "Fast rotation"};
        string[] paceNames = {"Fast spawns (1-2s)", "Medium spawns (3-4s)", "Slow spawns (5-6s)"};

        if (nType != pType) parts.Add($"Target emphasis: {typeNames[pType]} \u2192 {typeNames[nType]}");
        if (nRot != pRot) parts.Add($"Rotation: {rotNames[pRot]} \u2192 {rotNames[nRot]}");
        if (nPace != pPace) parts.Add($"Spawn pace: {paceNames[pPace]} \u2192 {paceNames[nPace]}");

        if (parts.Count == 0) return "No change in strategy";
        return string.Join("\n", parts);
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    static string BaseProfileName(PlayerSkillLevel skill)
    {
        switch (skill)
        {
            case PlayerSkillLevel.Beginner:     return "_base_beginner";
            case PlayerSkillLevel.Intermediate: return "_base_intermediate";
            case PlayerSkillLevel.Advanced:     return "_base_advanced";
            default:                            return "_base_intermediate";
        }
    }

    static string ProfilesDir =>
        Path.Combine(Application.persistentDataPath, "shooter_profiles");

    static string GetProfilePath(string profileName)
    {
        string safe = profileName.Trim().ToLowerInvariant();
        foreach (char c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        return Path.Combine(ProfilesDir, safe + ".json");
    }

    public void Save()
    {
        string path = GetProfilePath(playerName);
        Directory.CreateDirectory(ProfilesDir);
        File.WriteAllText(path, JsonUtility.ToJson(this, true));
    }

    public void SaveAsBase(PlayerSkillLevel skill)
    {
        string origName = playerName;
        playerName = BaseProfileName(skill);
        Save();
        playerName = origName;
        Debug.Log($"[Profile] Saved base profile for {skill} \u2192 {GetProfilePath(BaseProfileName(skill))}");
    }

    public static PlayerSkillProfile CreateFromBase(string playerName, PlayerSkillLevel skill)
    {
        var baseProfile = LoadRaw(BaseProfileName(skill));
        if (baseProfile != null)
        {
            var p = new PlayerSkillProfile
            {
                playerName           = playerName,
                skillLevel           = (int)skill,
                roundsPlayed         = 0,
                lifetimeHitRate      = baseProfile.emaHitRate,
                lifetimeAvgTimeToHit = baseProfile.emaTimeToHit,
                epsilon              = INITIAL_EPSILON,
                emaHitRate           = baseProfile.emaHitRate,
                emaTimeToHit         = baseProfile.emaTimeToHit,
                emaEngagement        = baseProfile.emaEngagement,
                emaHitRateStationary = baseProfile.emaHitRateStationary,
                emaHitRateMoving     = baseProfile.emaHitRateMoving,
                emaHitRateErratic    = baseProfile.emaHitRateErratic,
                emaTTHStationary     = baseProfile.emaTTHStationary,
                emaTTHMoving         = baseProfile.emaTTHMoving,
                emaTTHErratic        = baseProfile.emaTTHErratic,
                emaPointsStationary  = baseProfile.emaPointsStationary,
                emaPointsMoving      = baseProfile.emaPointsMoving,
                emaPointsErratic     = baseProfile.emaPointsErratic,
                emaHitRateRotating   = baseProfile.emaHitRateRotating,
                emaPointsPerTarget   = baseProfile.emaPointsPerTarget,
                qTable               = (float[])baseProfile.qTable.Clone()
            };
            Debug.Log($"[Profile] '{playerName}' initialized from trained base ({skill}).");
            return p;
        }

        Debug.Log($"[Profile] No base for {skill}, using hand-seeded defaults for '{playerName}'.");
        return CreateNew(playerName, skill);
    }

    public static PlayerSkillProfile Load(string playerName)   => LoadRaw(playerName);
    public static bool Exists(string playerName) => File.Exists(GetProfilePath(playerName));

    static PlayerSkillProfile LoadRaw(string profileName)
    {
        string path = GetProfilePath(profileName);
        if (!File.Exists(path)) return null;
        try   { return JsonUtility.FromJson<PlayerSkillProfile>(File.ReadAllText(path)); }
        catch { return null; }
    }
}
