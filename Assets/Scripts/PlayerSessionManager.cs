using UnityEngine;

public enum SessionPhase { PlayerSelect, SkillPrompt, Gameplay }

/// <summary>
/// Gates the shooter scene: player-select lobby first, optional skill prompt
/// for new players, then full gameplay with gun and adaptive ML spawning.
/// </summary>
public class PlayerSessionManager : MonoBehaviour
{
    [Header("References")]
    public GameObject          lobbyRoot;
    public GameObject          rangeRoot;
    public GameObject          gunObject;
    public GameObject          rightControllerVisual;
    public VRPointerSystem     pointerSystem;
    public ShooterInteractSystem interactSystem;
    public ShooterGun          gun;
    public ShooterPlayerController playerController;
    public ShooterHUD          hud;
    public ShooterButton       rangeStartButton;

    [Header("ML / Adaptive")]
    public AdaptiveSpawnController adaptiveController;
    public SkillPromptUI           skillPromptUI;

    public SessionPhase Phase { get; private set; } = SessionPhase.PlayerSelect;
    public string CurrentPlayerName { get; private set; }

    void Start()
    {
        EnterPlayerSelect();
    }

    public void EnterPlayerSelect()
    {
        Phase = SessionPhase.PlayerSelect;
        CurrentPlayerName = "";

        if (lobbyRoot != null) lobbyRoot.SetActive(true);
        if (rangeRoot != null) rangeRoot.SetActive(false);

        SetGunVisible(false);
        SetRightControllerVisible(true);

        if (pointerSystem != null)   pointerSystem.SetActive(true);
        if (interactSystem != null)  interactSystem.enabled = false;
        if (gun != null)             gun.enabled = false;
        if (playerController != null) playerController.movementEnabled = false;

        if (rangeStartButton != null)
            rangeStartButton.CanActivate = false;

        if (skillPromptUI != null) skillPromptUI.Hide();

        hud?.SetSessionPhase(SessionPhase.PlayerSelect, "");
    }

    public void BeginGameplay(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName)) return;

        CurrentPlayerName = playerName.Trim();

        // Check if this is a new player who needs the skill prompt
        bool isNewPlayer = !PlayerSkillProfile.Exists(CurrentPlayerName);

        if (isNewPlayer && skillPromptUI != null)
        {
            Phase = SessionPhase.SkillPrompt;
            // Hide lobby but keep skill prompt visible (prompt is a sibling, not child)
            if (lobbyRoot != null) lobbyRoot.SetActive(false);
            skillPromptUI.gameObject.SetActive(true);
            skillPromptUI.Show(CurrentPlayerName, OnSkillSelected);
            return;
        }

        // Existing player — load their profile and go straight to gameplay
        PlayerRegistry.AddPlayerIfNew(CurrentPlayerName);
        LoadProfileAndEnterGameplay(null);
    }

    void OnSkillSelected(PlayerSkillLevel level)
    {
        PlayerRegistry.AddPlayerIfNew(CurrentPlayerName);
        // Load Q-table from offline-trained base profile if available,
        // otherwise fall back to hand-seeded defaults.
        var profile = PlayerSkillProfile.CreateFromBase(CurrentPlayerName, level);
        profile.Save();
        LoadProfileAndEnterGameplay(profile);
    }

    void LoadProfileAndEnterGameplay(PlayerSkillProfile profile)
    {
        Phase = SessionPhase.Gameplay;

        if (skillPromptUI != null) skillPromptUI.Hide();

        if (profile == null)
            profile = PlayerSkillProfile.Load(CurrentPlayerName);

        // Fallback: create intermediate profile if somehow missing
        if (profile == null)
        {
            profile = PlayerSkillProfile.CreateNew(CurrentPlayerName, PlayerSkillLevel.Intermediate);
            profile.Save();
        }

        // Assign to adaptive controller
        if (adaptiveController != null)
            adaptiveController.activeProfile = profile;

        if (lobbyRoot != null) lobbyRoot.SetActive(false);
        if (rangeRoot != null) rangeRoot.SetActive(true);

        SetGunVisible(true);
        SetRightControllerVisible(false);

        if (pointerSystem != null)   pointerSystem.SetActive(false);
        if (interactSystem != null)  interactSystem.enabled = true;
        if (gun != null)             gun.enabled = true;
        if (playerController != null) playerController.movementEnabled = true;

        if (rangeStartButton != null)
        {
            rangeStartButton.gameObject.SetActive(true);
            rangeStartButton.CanActivate = true;
            rangeStartButton.RefreshVisual();
        }

        hud?.SetSessionPhase(SessionPhase.Gameplay, CurrentPlayerName);

        // Legacy model manager support
        ShooterModelManager.EnsureModelExists(CurrentPlayerName);
        var modelMgr = FindObjectOfType<ShooterModelManager>();
        if (modelMgr != null) modelMgr.LoadModelForPlayer(CurrentPlayerName);
    }

    void SetGunVisible(bool visible)
    {
        if (gunObject != null)
            gunObject.SetActive(visible);
    }

    void SetRightControllerVisible(bool visible)
    {
        if (rightControllerVisual != null)
            rightControllerVisual.SetActive(visible);
    }
}
