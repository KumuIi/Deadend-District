using UnityEngine;

/// <summary>
/// Listens for Escape to toggle the pause menu.
/// Scene-scoped — place one in each gameplay scene alongside PauseMenu.
/// Does not persist across scene loads; each scene owns its own instance.
/// Not present in the main menu scene.
///
/// Implementors: one instance per gameplay scene.
/// </summary>
public class MenuController : MonoBehaviour
{
    [SerializeField] private PauseMenu _pauseMenu;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            Toggle();
    }

    public void Toggle()
    {
        if (_pauseMenu == null) return;

        if (_pauseMenu.IsOpen)
            _pauseMenu.Close();
        else
            _pauseMenu.Open();
    }

    public void ForceClose()
    {
        if (_pauseMenu != null && _pauseMenu.IsOpen)
            _pauseMenu.Close();
    }
}
