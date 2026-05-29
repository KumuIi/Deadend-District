using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Bootstraps the main menu scene. Provides named methods you wire into
/// MenuButton3D.OnClick via the inspector — no code needed per button.
///
/// Place on the root empty of your main menu scene. Assign hubScene name.
///
/// Implementors: one instance in the MainMenu scene only.
/// </summary>
public class MainMenu : MonoBehaviour
{
    [SerializeField] private string _hubScene = "Hub";

    private void Start()
    {
        // Main menu never locks the cursor — player needs it to click buttons
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f;
    }

    // ── Button callbacks — wire these to MenuButton3D.OnClick in inspector ─

    /// <summary>Load the most recently saved slot and go straight to the hub.</summary>
    public void ContinueGame()
    {
        if (SaveSystem.Instance == null) return;

        string slot = SaveMetadataIO.FindMostRecentSlot();
        if (slot == null || !SaveSystem.Instance.SlotExists(slot))
        {
            Debug.LogWarning("[MainMenu] No save found for Continue.");
            return;
        }

        RunManager.Instance?.SetActiveSlot(slot);
        SaveSystem.Instance.RestoreAfterSceneLoad(RunScopeTag.Profile, slot);
        SaveSystem.Instance.RestoreAfterSceneLoad(RunScopeTag.World, slot);
        SceneManager.LoadScene(_hubScene);
    }

    /// <summary>Settings stub — wire to a MenuButton3D that shifts camera to settings area.</summary>
    public void OpenSettings()
    {
        // Wave 4 polish: settings screen implementation
        Debug.Log("[MainMenu] Settings not yet implemented.");
    }

    public void ExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
