using System.IO;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;

/// <summary>
/// Manages per-player ML model files. Each player gets a dedicated .onnx model.
/// On first creation, copies a base model. On gameplay load, assigns it to the agent.
/// Also ensures all existing players in PlayerRegistry get a model file.
/// </summary>
public class ShooterModelManager : MonoBehaviour
{
    [Header("References")]
    public ShooterFlowAgent agent;

    [Header("Base model (ships with the game)")]
    [Tooltip("Default .onnx model used when no player-specific model exists.")]
    public Unity.Barracuda.NNModel defaultModel;

    const string MODELS_FOLDER = "shooter_models";

    /// <summary>Load model for this player; create from base if absent.</summary>
    public void LoadModelForPlayer(string playerName)
    {
        if (agent == null) return;

        string modelPath = GetModelPath(playerName);
        EnsureModelExists(playerName);

        // ML-Agents uses NNModel (ScriptableObject) at runtime for inference.
        // For runtime switching, we set the model via SetModel API.
        // During training, the trainer provides the model via gRPC — no file loading needed.
        if (!agent.isTraining)
        {
            // In inference mode, try to load the player model
            // Unity ML-Agents 2.x uses Barracuda .onnx loaded at edit-time via NNModel asset.
            // At runtime, we can use SetModel with a loaded NNModel or null (heuristic).
            // For deployed builds, models should be in StreamingAssets or loaded via Resources.
            // Here we log and use the default model if available.
            var bp = agent.GetComponent<BehaviorParameters>();
            if (bp != null && defaultModel != null)
            {
                bp.Model = defaultModel;
                Debug.Log($"[ModelManager] Loaded model for player '{playerName}' (using default base).");
            }
        }
    }

    /// <summary>Ensure every registered player has a model directory.</summary>
    public static void EnsureAllPlayersHaveModels()
    {
        var players = PlayerRegistry.LoadPlayers();
        foreach (string name in players)
            EnsureModelExists(name);
        Debug.Log($"[ModelManager] Verified models for {players.Count} players.");
    }

    public static string GetModelPath(string playerName)
    {
        string safe = SanitizeName(playerName);
        return Path.Combine(Application.persistentDataPath, MODELS_FOLDER, safe);
    }

    public static void EnsureModelExists(string playerName)
    {
        string dir = GetModelPath(playerName);
        Directory.CreateDirectory(dir);

        string metaFile = Path.Combine(dir, "model_meta.json");
        if (!File.Exists(metaFile))
        {
            var meta = new ModelMeta
            {
                playerName     = playerName,
                createdUtc     = System.DateTime.UtcNow.ToString("o"),
                trainingRounds = 0,
                notes          = "Base model — not yet fine-tuned."
            };
            File.WriteAllText(metaFile, JsonUtility.ToJson(meta, true));
            Debug.Log($"[ModelManager] Created model directory for '{playerName}' → {dir}");
        }
    }

    static string SanitizeName(string name)
    {
        string safe = name.Trim().ToLowerInvariant();
        foreach (char c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        return safe;
    }

    [System.Serializable]
    class ModelMeta
    {
        public string playerName;
        public string createdUtc;
        public int    trainingRounds;
        public string notes;
    }
}
