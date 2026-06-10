using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lobby UI: name field, saved-player list, VR keyboard, confirm button.
/// </summary>
public class PlayerSelectUI : MonoBehaviour
{
    [Header("References")]
    public VRKeyboard          keyboard;
    public Text                nameDisplay;
    public Text                hintText;
    public Text                listHeaderText;
    public Transform           listContainer;
    public GameObject          listEntryPrefab;
    public ShooterButton       confirmButton;
    public PlayerSessionManager sessionManager;

    [Header("Empty list message")]
    public string noPlayersHint = "No saved players yet.\nType a new name below.";

    private readonly List<PlayerListEntry> _entries = new List<PlayerListEntry>();
    private string _selectedName = "";

    void Awake()
    {
        if (sessionManager == null)
            sessionManager = FindObjectOfType<PlayerSessionManager>();

        if (confirmButton != null)
            confirmButton.OnActivated += OnConfirmPressed;
    }

    void Start()
    {
        if (keyboard != null)
            keyboard.OnTextChanged += OnKeyboardTextChanged;

        RefreshPlayerList();
        OnKeyboardTextChanged(keyboard != null ? keyboard.Text : "");
    }

    void OnDestroy()
    {
        if (keyboard != null)
            keyboard.OnTextChanged -= OnKeyboardTextChanged;

        if (confirmButton != null)
            confirmButton.OnActivated -= OnConfirmPressed;
    }

    public void RefreshPlayerList()
    {
        foreach (var e in _entries)
        {
            if (e != null) Destroy(e.gameObject);
        }
        _entries.Clear();

        List<string> players = PlayerRegistry.LoadPlayers();

        if (listHeaderText != null)
            listHeaderText.text = players.Count > 0 ? "SAVED PLAYERS" : "SAVED PLAYERS (empty)";

        if (hintText != null && players.Count == 0)
            hintText.text = noPlayersHint;

        if (listContainer == null || listEntryPrefab == null)
            return;

        float y = 0f;
        const float rowHeight = 0.16f;

        foreach (string name in players)
        {
            GameObject go = Instantiate(listEntryPrefab, listContainer);
            go.transform.localPosition   = new Vector3(0f, -y, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one;

            PlayerListEntry entry = go.GetComponent<PlayerListEntry>();
            entry.Setup(name, SelectPlayerFromList);
            _entries.Add(entry);
            y += rowHeight;
        }
    }

    void OnKeyboardTextChanged(string text)
    {
        _selectedName = text?.Trim() ?? "";
        if (nameDisplay != null)
            nameDisplay.text = string.IsNullOrEmpty(text) ? "_" : text;

        UpdateListSelection(_selectedName);
        UpdateConfirmState();
    }

    void SelectPlayerFromList(string name)
    {
        if (keyboard != null)
            keyboard.SetText(name);

        _selectedName = name;
        UpdateListSelection(name);
        UpdateConfirmState();
    }

    void UpdateListSelection(string name)
    {
        foreach (var entry in _entries)
        {
            bool match = !string.IsNullOrEmpty(name)
                && string.Equals(entry.playerName, name.Trim(),
                    System.StringComparison.OrdinalIgnoreCase);
            entry.SetSelected(match);
        }
    }

    void UpdateConfirmState()
    {
        if (confirmButton == null) return;
        bool valid = !string.IsNullOrWhiteSpace(_selectedName);
        confirmButton.CanActivate = valid;
        confirmButton.RefreshVisual();
    }

    void OnConfirmPressed()
    {
        string name = GetEnteredName();
        if (string.IsNullOrEmpty(name))
        {
            Debug.Log("[PlayerSelect] Enter a name before starting.");
            return;
        }

        if (sessionManager == null)
            sessionManager = FindObjectOfType<PlayerSessionManager>();

        if (sessionManager == null)
        {
            Debug.LogError("[PlayerSelect] PlayerSessionManager not found.");
            return;
        }

        // PlayerRegistry.AddPlayerIfNew is handled by PlayerSessionManager
        // after skill prompt (for new players) or immediately (returning players).
        sessionManager.BeginGameplay(name);
    }

    string GetEnteredName()
    {
        if (!string.IsNullOrWhiteSpace(_selectedName))
            return _selectedName.Trim();

        if (keyboard != null && !string.IsNullOrWhiteSpace(keyboard.Text))
            return keyboard.Text.Trim();

        return "";
    }
}
