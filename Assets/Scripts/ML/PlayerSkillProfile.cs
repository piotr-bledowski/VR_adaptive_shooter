using System;
using System.IO;
using UnityEngine;

public enum PlayerSkillLevel { Beginner = 0, Intermediate = 1, Advanced = 2 }

/// <summary>
/// Player's subjective difficulty rating, gathered after every round.
/// In VR gameplay a human picks this on a 5-button panel; in offline training a
/// SyntheticPlayer derives it from its own preferences. This rating is the MAIN
/// driver of the Q-learning reward.
/// </summary>
public enum DifficultyRating { TooEasy = 0, Easy = 1, Perfect = 2, Hard = 3, TooHard = 4 }

/// <summary>
/// Tabular Q-learning profile that adapts spawn difficulty to a single player.
///
/// The agent picks one action per round (target emphasis × rotation × spawn pace),
/// the player plays, then rates the round. The rating maps to a scalar reward and a
/// one-step Q-update is applied. Optimistic initialisation + a fast ε-decay let the
/// table converge to a near-greedy policy within ~10 rounds.
///
/// This class is intentionally generic: nothing here knows about the offline
/// synthetic personas. The chosen skill level is stored for metadata only;
/// every new profile starts from the same neutral Q-table.
/// </summary>
[Serializable]
public class PlayerSkillProfile
{
    public string playerName;
    public int    skillLevel;
    public int    roundsPlayed;

    // Q-table: [stateIndex * ACTION_COUNT + actionIndex] → expected (discounted) reward.
    public float[] qTable;

    public float epsilon;

    // Last difficulty rating the player gave (−1 = none yet). Part of the state.
    public int lastRating = -1;

    // Runtime-only: set during UpdateAfterRound so ApplyFeedback can surface it.
    [NonSerialized] public string lastGuidanceNote = "";

    // Lightweight EMAs kept for state bucketing, reporting and explainability.
    public float emaHitRate;
    public float emaTimeToHit;

    public float lifetimeHitRate;
    public float lifetimeAvgTimeToHit;

    // ── MDP dimensions ─────────────────────────────────────────────────────────
    //  State  = lastRating (5) × hitRateBucket (3)            = 15
    //  Action = emphasis (3) × rotation (4) × spawnPace (3)   = 36
    public const int STATE_COUNT  = 15;
    public const int ACTION_COUNT = 36;

    // ── Hyper-parameters (tuned for fast, ~10-round convergence) ────────────────
    const float OPTIMISTIC_INIT = 2.0f;   // = max achievable immediate reward (Perfect)
    const float INITIAL_EPSILON = 0.30f;
    const float MIN_EPSILON     = 0.0f;
    const float EPSILON_DECAY   = 0.75f;  // ε ≈ 0.02 by round 8-9 → effectively greedy
    const float LEARNING_RATE   = 0.5f;
    const float DISCOUNT        = 0.5f;
    const float EMA_ALPHA       = 0.5f;   // responsive stats for short sessions
    const float INIT_NOISE      = 0.08f;  // per-cell jitter in the shared start table

    // One random Q-table drawn per play session; every new profile clones it.
    static float[] _sharedQTemplate;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSharedQTemplate() => _sharedQTemplate = null;

    public PlayerSkillProfile() { }

    // ── Action encoding ──────────────────────────────────────────────────────

    public static void DecodeAction(int action, out int emphasisType, out int rotationLevel, out int spawnPace)
    {
        emphasisType  = action / 12;        // 0 Stationary, 1 Moving, 2 Erratic
        rotationLevel = (action % 12) / 3;  // 0 None, 1 Slow, 2 Medium, 3 Fast
        spawnPace     = action % 3;         // 0 Fast, 1 Medium, 2 Slow
    }

    public static int EncodeAction(int emphasisType, int rotationLevel, int spawnPace)
        => emphasisType * 12 + rotationLevel * 3 + spawnPace;

    // ── Factory ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fresh profile. Every agent clones the same randomly drawn Q-table (built once
    /// per session on the first call). Skill level is stored but does not bias init.
    /// </summary>
    public static PlayerSkillProfile CreateNew(string name, PlayerSkillLevel skill)
    {
        var p = new PlayerSkillProfile
        {
            playerName           = name,
            skillLevel           = (int)skill,
            roundsPlayed         = 0,
            epsilon              = INITIAL_EPSILON,
            lastRating           = -1,
            emaHitRate           = 0.5f,
            emaTimeToHit         = 1.0f,
            lifetimeHitRate      = 0f,
            lifetimeAvgTimeToHit = 0f,
            qTable               = new float[STATE_COUNT * ACTION_COUNT]
        };

        InitializeQTable(p);
        return p;
    }

    static void InitializeQTable(PlayerSkillProfile p)
    {
        if (_sharedQTemplate == null)
        {
            _sharedQTemplate = new float[STATE_COUNT * ACTION_COUNT];
            for (int i = 0; i < _sharedQTemplate.Length; i++)
                _sharedQTemplate[i] = OPTIMISTIC_INIT + UnityEngine.Random.Range(-INIT_NOISE, INIT_NOISE);
        }
        System.Array.Copy(_sharedQTemplate, p.qTable, _sharedQTemplate.Length);
    }

    // ── State discretization ─────────────────────────────────────────────────

    /// <summary>
    /// State = lastRating (5, defaults to Perfect before the first rating) ×
    ///         hit-rate bucket (0 low &lt;0.35, 1 mid, 2 high &gt;0.60).
    /// </summary>
    public int GetCurrentState()
    {
        int ratingBucket = lastRating < 0 ? (int)DifficultyRating.Perfect : Mathf.Clamp(lastRating, 0, 4);
        int perfBucket   = emaHitRate < 0.35f ? 0 : (emaHitRate < 0.60f ? 1 : 2);
        return ratingBucket * 3 + perfBucket;
    }

    // ── Action selection (ε-greedy, random tie-break) ────────────────────────

    public int SelectAction()
    {
        if (UnityEngine.Random.value < epsilon)
            return UnityEngine.Random.Range(0, ACTION_COUNT);

        int state = GetCurrentState();
        int baseIdx = state * ACTION_COUNT;

        float bestQ = float.NegativeInfinity;
        for (int a = 0; a < ACTION_COUNT; a++)
            if (qTable[baseIdx + a] > bestQ) bestQ = qTable[baseIdx + a];

        // Random tie-break so optimistic exploration sweeps actions in random order.
        int count = 0;
        int chosen = 0;
        for (int a = 0; a < ACTION_COUNT; a++)
        {
            if (qTable[baseIdx + a] >= bestQ - 1e-4f)
            {
                count++;
                if (UnityEngine.Random.Range(0, count) == 0) chosen = a;
            }
        }
        return chosen;
    }

    /// <summary>Greedy best action for the current state (ignores ε) — for display.</summary>
    public int GreedyAction()
    {
        int baseIdx = GetCurrentState() * ACTION_COUNT;
        int best = 0;
        float bestQ = float.NegativeInfinity;
        for (int a = 0; a < ACTION_COUNT; a++)
            if (qTable[baseIdx + a] > bestQ) { bestQ = qTable[baseIdx + a]; best = a; }
        return best;
    }

    // ── Difficulty ordering ───────────────────────────────────────────────────

    // Built-in difficulty weights per action dimension.
    // emphasisType:  Stationary(0) < Moving(1) < Erratic(2)        weight 1.0
    // rotationLevel: None(0) < Slow(1) < Medium(2) < Fast(3)       weight 0.75
    // spawnPace:     Slow(2) < Medium(1) < Fast(0)  (inverted)     weight 0.50
    // Max raw score = 2 + 3*0.75 + 2*0.50 = 5.25
    const float MAX_DIFFICULTY   = 5.25f;
    const float GUIDANCE_STRONG  = 0.35f;   // nudge scale for TooEasy / TooHard
    const float GUIDANCE_MILD    = 0.12f;   // nudge scale for Easy / Hard

    /// <summary>
    /// Scalar difficulty of an action on a 0–5.25 scale based on
    /// target type, rotation, and spawn pace semantics.
    /// </summary>
    public static float ActionDifficulty(int action)
    {
        DecodeAction(action, out int emph, out int rot, out int pace);
        return emph * 1.0f + rot * 0.75f + (2 - pace) * 0.50f;
    }

    // ── Reward ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The player's difficulty rating IS the reward signal.
    /// Perfect is the goal; the adjacent ratings give a directional gradient.
    /// </summary>
    public static float ComputeRatingReward(DifficultyRating rating)
    {
        switch (rating)
        {
            case DifficultyRating.Perfect: return  2.0f;
            case DifficultyRating.Easy:    return  0.5f;
            case DifficultyRating.Hard:    return  0.5f;
            case DifficultyRating.TooEasy: return -1.0f;
            case DifficultyRating.TooHard: return -1.0f;
            default:                       return  0f;
        }
    }

    // ── Learning update (after each round, once the rating is known) ──────────

    /// <summary>
    /// One-step Q-update. <paramref name="stateAtSelection"/> is the state in which
    /// the action was chosen (captured at round start). The reward comes from the
    /// player's rating; stats are folded into the EMAs to form the next state.
    /// </summary>
    public void UpdateAfterRound(int stateAtSelection, int actionTaken,
                                 DifficultyRating rating, float reward,
                                 float hitRate, float avgTimeToHit)
    {
        roundsPlayed++;

        emaHitRate   = Mathf.Lerp(emaHitRate,   hitRate,      EMA_ALPHA);
        emaTimeToHit = Mathf.Lerp(emaTimeToHit, avgTimeToHit, EMA_ALPHA);
        lifetimeHitRate      = Mathf.Lerp(lifetimeHitRate,      hitRate,      1f / roundsPlayed);
        lifetimeAvgTimeToHit = Mathf.Lerp(lifetimeAvgTimeToHit, avgTimeToHit, 1f / roundsPlayed);

        lastRating = (int)rating;   // becomes part of the next state

        int nextState = GetCurrentState();
        int baseNext  = nextState * ACTION_COUNT;
        float maxNextQ = float.NegativeInfinity;
        for (int a = 0; a < ACTION_COUNT; a++)
            if (qTable[baseNext + a] > maxNextQ) maxNextQ = qTable[baseNext + a];

        int idx = stateAtSelection * ACTION_COUNT + actionTaken;
        qTable[idx] += LEARNING_RATE * (reward + DISCOUNT * maxNextQ - qTable[idx]);

        epsilon = Mathf.Max(MIN_EPSILON, epsilon * EPSILON_DECAY);

        // ── Directional guidance ──────────────────────────────────────────────
        // When the rating has a clear direction (too easy → need harder;
        // too hard → need easier), nudge the Q-values of every other action in
        // that direction proportionally to how much harder or easier each action is.
        // This gives the model built-in knowledge of the difficulty ordering
        // so it steers the right way without extra random exploration.
        float guidanceScale;
        float guidanceDir;
        string guidanceDesc;

        switch (rating)
        {
            case DifficultyRating.TooEasy:
                guidanceScale = GUIDANCE_STRONG; guidanceDir = +1f;
                guidanceDesc  = "Steering toward harder settings";
                break;
            case DifficultyRating.TooHard:
                guidanceScale = GUIDANCE_STRONG; guidanceDir = -1f;
                guidanceDesc  = "Steering toward easier settings";
                break;
            case DifficultyRating.Easy:
                guidanceScale = GUIDANCE_MILD; guidanceDir = +1f;
                guidanceDesc  = "Slight nudge toward harder settings";
                break;
            case DifficultyRating.Hard:
                guidanceScale = GUIDANCE_MILD; guidanceDir = -1f;
                guidanceDesc  = "Slight nudge toward easier settings";
                break;
            default:
                guidanceScale = 0f; guidanceDir = 0f;
                guidanceDesc  = "";
                break;
        }

        if (guidanceScale > 0f)
        {
            float currentDiff = ActionDifficulty(actionTaken);
            int   baseS       = stateAtSelection * ACTION_COUNT;
            for (int a = 0; a < ACTION_COUNT; a++)
            {
                if (a == actionTaken) continue;
                float diff  = ActionDifficulty(a);
                float nudge = guidanceScale * guidanceDir * (diff - currentDiff) / MAX_DIFFICULTY;
                qTable[baseS + a] += nudge;
            }
            lastGuidanceNote = guidanceDesc;
        }
        else
        {
            lastGuidanceNote = "";
        }
    }

    // ── Explainability ───────────────────────────────────────────────────────

    static readonly string[] TYPE_NAMES = { "Stationary", "Moving", "Erratic" };
    static readonly string[] ROT_NAMES  = { "no rotation", "slow rotation", "medium rotation", "fast rotation" };
    static readonly string[] PACE_NAMES = { "fast spawns (1-2s)", "medium spawns (3-4s)", "slow spawns (5-6s)" };

    public static string DescribeAction(int action)
    {
        DecodeAction(action, out int t, out int r, out int p);
        return $"{TYPE_NAMES[t]} emphasis, {ROT_NAMES[r]}, {PACE_NAMES[p]}";
    }

    public static string ExplainActionChange(int prevAction, int newAction)
    {
        if (prevAction == newAction) return "Strategy unchanged — keeping current settings.";

        DecodeAction(prevAction, out int pT, out int pR, out int pP);
        DecodeAction(newAction,  out int nT, out int nR, out int nP);

        var parts = new System.Collections.Generic.List<string>();
        if (nT != pT) parts.Add($"target focus {TYPE_NAMES[pT]} \u2192 {TYPE_NAMES[nT]}");
        if (nR != pR) parts.Add($"{ROT_NAMES[pR]} \u2192 {ROT_NAMES[nR]}");
        if (nP != pP) parts.Add($"{PACE_NAMES[pP]} \u2192 {PACE_NAMES[nP]}");
        return "Adjusting: " + string.Join(", ", parts);
    }

    public static string RatingLabel(DifficultyRating rating)
    {
        switch (rating)
        {
            case DifficultyRating.TooEasy: return "Too Easy";
            case DifficultyRating.Easy:    return "Easy";
            case DifficultyRating.Perfect: return "Perfect";
            case DifficultyRating.Hard:    return "Hard";
            case DifficultyRating.TooHard: return "Too Hard";
            default:                       return "?";
        }
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    static string BaseProfileName(PlayerSkillLevel skill)
    {
        switch (skill)
        {
            case PlayerSkillLevel.Beginner:     return "_base_beginner";
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

    public static PlayerSkillProfile Load(string playerName) => LoadRaw(playerName);
    public static bool Exists(string playerName) => File.Exists(GetProfilePath(playerName));

    static PlayerSkillProfile LoadRaw(string profileName)
    {
        string path = GetProfilePath(profileName);
        if (!File.Exists(path)) return null;
        try
        {
            var p = JsonUtility.FromJson<PlayerSkillProfile>(File.ReadAllText(path));
            // Guard against schema drift from older saves.
            if (p != null && (p.qTable == null || p.qTable.Length != STATE_COUNT * ACTION_COUNT))
                return null;
            return p;
        }
        catch { return null; }
    }
}
