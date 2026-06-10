using System;
using System.IO;
using UnityEngine;

public enum PlayerSkillLevel { Beginner = 0, Intermediate = 1, Advanced = 2 }

/// <summary>
/// Per-player adaptive spawn parameters learned online during gameplay.
/// Uses tabular Q-learning over a discretized state-action space for
/// fast convergence (adapts meaningfully within 3–5 rounds).
/// </summary>
[Serializable]
public class PlayerSkillProfile
{
    public string playerName;
    public int    skillLevel;  // 0=beginner, 1=intermediate, 2=advanced
    public int    roundsPlayed;
    public float  lifetimeHitRate;
    public float  lifetimeAvgTimeToHit;

    // Q-table: [stateIndex, actionIndex] → expected reward
    // State: 3 performance buckets × 3 pace buckets = 9 states
    // Actions: 7 difficulty presets (target count, type mix, spawn speed)
    public float[] qTable;

    // Exploration
    public float epsilon;

    // EMA of player metrics (updated each round)
    public float emaHitRate;
    public float emaTimeToHit;
    public float emaEngagement; // shots per second

    const int STATE_COUNT  = 9;
    const int ACTION_COUNT = 7;
    const float INITIAL_EPSILON   = 0.4f;
    const float MIN_EPSILON       = 0.05f;
    const float EPSILON_DECAY     = 0.85f;
    const float LEARNING_RATE     = 0.3f;
    const float DISCOUNT          = 0.7f;
    const float EMA_ALPHA         = 0.4f; // high alpha = fast adaptation

    public PlayerSkillProfile() { }

    public static PlayerSkillProfile CreateNew(string name, PlayerSkillLevel skill)
    {
        var p = new PlayerSkillProfile
        {
            playerName        = name,
            skillLevel        = (int)skill,
            roundsPlayed      = 0,
            lifetimeHitRate   = 0f,
            lifetimeAvgTimeToHit = 0f,
            epsilon           = INITIAL_EPSILON,
            emaHitRate        = GetDefaultHitRate(skill),
            emaTimeToHit      = GetDefaultTimeToHit(skill),
            emaEngagement     = 1.5f,
            qTable            = new float[STATE_COUNT * ACTION_COUNT]
        };

        InitializeQTableFromProfile(p, skill);
        return p;
    }

    static void InitializeQTableFromProfile(PlayerSkillProfile p, PlayerSkillLevel skill)
    {
        // Seed Q-values so the agent starts with reasonable behavior
        // based on the pretrained profile's known-good actions
        int preferredAction;
        switch (skill)
        {
            case PlayerSkillLevel.Beginner:     preferredAction = 1; break;
            case PlayerSkillLevel.Intermediate: preferredAction = 3; break;
            case PlayerSkillLevel.Advanced:     preferredAction = 5; break;
            default:                            preferredAction = 3; break;
        }

        for (int s = 0; s < STATE_COUNT; s++)
        {
            for (int a = 0; a < ACTION_COUNT; a++)
            {
                float dist = Mathf.Abs(a - preferredAction);
                p.qTable[s * ACTION_COUNT + a] = Mathf.Max(0f, 1.0f - dist * 0.25f);
            }
        }
    }

    static float GetDefaultHitRate(PlayerSkillLevel skill)
    {
        switch (skill)
        {
            case PlayerSkillLevel.Beginner:     return 0.3f;
            case PlayerSkillLevel.Intermediate: return 0.55f;
            case PlayerSkillLevel.Advanced:     return 0.75f;
            default:                            return 0.5f;
        }
    }

    static float GetDefaultTimeToHit(PlayerSkillLevel skill)
    {
        switch (skill)
        {
            case PlayerSkillLevel.Beginner:     return 4.0f;
            case PlayerSkillLevel.Intermediate: return 2.5f;
            case PlayerSkillLevel.Advanced:     return 1.2f;
            default:                            return 2.5f;
        }
    }

    // ── State discretization ────────────────────────────────────────────────

    public int GetCurrentState()
    {
        int perfBucket = emaHitRate < 0.35f ? 0 : (emaHitRate < 0.65f ? 1 : 2);
        int paceBucket = emaTimeToHit > 3.5f ? 0 : (emaTimeToHit > 1.5f ? 1 : 2);
        return perfBucket * 3 + paceBucket;
    }

    // ── Action selection (epsilon-greedy) ───────────────────────────────────

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

    // ── Learning update (after each round) ──────────────────────────────────

    public void UpdateAfterRound(float hitRate, float avgTimeToHit, float shotsPerSec,
                                  int actionTaken, float reward)
    {
        roundsPlayed++;

        // Update EMAs
        emaHitRate    = Mathf.Lerp(emaHitRate,    hitRate,      EMA_ALPHA);
        emaTimeToHit  = Mathf.Lerp(emaTimeToHit,  avgTimeToHit, EMA_ALPHA);
        emaEngagement = Mathf.Lerp(emaEngagement, shotsPerSec,  EMA_ALPHA);

        // Lifetime stats
        lifetimeHitRate      = Mathf.Lerp(lifetimeHitRate, hitRate, 1f / roundsPlayed);
        lifetimeAvgTimeToHit = Mathf.Lerp(lifetimeAvgTimeToHit, avgTimeToHit, 1f / roundsPlayed);

        // Q-learning update
        int state = GetCurrentState();
        int idx = state * ACTION_COUNT + actionTaken;

        // Find max Q for next state (after EMA update, state may have changed)
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

    // ── Reward computation ──────────────────────────────────────────────────

    public static float ComputeFlowReward(float hitRate, float avgTimeToHit)
    {
        // Ideal flow: hit rate 40-70%, time-to-hit 1.5-3.5s
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

        return reward;
    }

    // ── Persistence ─────────────────────────────────────────────────────────

    // Base profile names — written by the training scene, read when a new player joins.
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

    /// <summary>
    /// Save this profile as the base (pretrained) template for the given skill level.
    /// Called by the training scene after offline training converges.
    /// </summary>
    public void SaveAsBase(PlayerSkillLevel skill)
    {
        string origName = playerName;
        playerName = BaseProfileName(skill);
        Save();
        playerName = origName;
        Debug.Log($"[Profile] Saved base profile for {skill} → {GetProfilePath(BaseProfileName(skill))}");
    }

    /// <summary>
    /// Load the base (pretrained) profile for a skill level, copy its Q-table
    /// into a fresh profile for the given real player name, and return it.
    /// Falls back to the hand-seeded Q-table if no base profile exists yet.
    /// </summary>
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
                qTable               = (float[])baseProfile.qTable.Clone()
            };
            Debug.Log($"[Profile] '{playerName}' initialized from trained base ({skill}).");
            return p;
        }

        // No base profile on disk yet — fall back to hand-seeded defaults
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
