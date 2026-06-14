using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Persists player names to a local JSON file under Application.persistentDataPath.
/// </summary>
[Serializable]
public class PlayerListData
{
    public List<string> players = new List<string>();
}

public static class PlayerRegistry
{
    const string FileName = "saved_players.json";

    static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    public static List<string> LoadPlayers()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<string>();

            string json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
                return new List<string>();

            PlayerListData data = JsonUtility.FromJson<PlayerListData>(json);
            if (data?.players == null)
                return new List<string>();

            return data.players
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[PlayerRegistry] Failed to load players: " + ex.Message);
            return new List<string>();
        }
    }

    public static bool Exists(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string trimmed = name.Trim();
        return LoadPlayers().Any(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds the name if it is new (case-insensitive). Returns true if saved.
    /// </summary>
    public static bool AddPlayerIfNew(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        string trimmed = name.Trim();
        var players = LoadPlayers();

        if (players.Any(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase)))
            return false;

        players.Add(trimmed);
        players = players
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SavePlayers(players);
        return true;
    }

    /// <summary>
    /// Deletes the player list file and all skill-profile JSON files on this device.
    /// Called from the MGU / Debug menu in the Editor, or from a runtime debug button.
    /// </summary>
    public static void ClearAll()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
                Debug.Log("[PlayerRegistry] Deleted " + FilePath);
            }

            string profileDir = Path.Combine(Application.persistentDataPath, "shooter_profiles");
            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, recursive: true);
                Debug.Log("[PlayerRegistry] Deleted profile directory: " + profileDir);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[PlayerRegistry] ClearAll failed: " + ex.Message);
        }
    }

    static void SavePlayers(List<string> players)
    {
        try
        {
            var data = new PlayerListData { players = players };
            string json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(FilePath, json);
            Debug.Log("[PlayerRegistry] Saved " + players.Count + " player(s) → " + FilePath);
        }
        catch (Exception ex)
        {
            Debug.LogError("[PlayerRegistry] Failed to save players: " + ex.Message);
        }
    }
}
