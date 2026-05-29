using System;
using UnityEngine;

/// <summary>
/// Saves and restores where the player is standing and looking.
///
/// Position + yaw live on the PlayerMotor (the Rigidbody body rotates for horizontal look);
/// pitch (vertical look) lives on the CameraController child. Restore routes position/yaw
/// through PlayerMotor.Teleport (required for the kinematic Rigidbody) and pitch through
/// CameraController.SetPitch.
///
/// Run scope: it's current-life state, restored as part of a full save snapshot.
///
/// Implementors: attach to the player root and assign both refs in the inspector.
/// </summary>
public class PlayerTransformSaveAdapter : MonoBehaviour, ISaveable
{
    [SerializeField] private PlayerMotor       _motor;
    [SerializeField] private CameraController  _camera;

    public string      SaveId    => "player.transform";
    public string      SaveType  => "PlayerTransform";
    public RunScopeTag SaveScope => RunScopeTag.Run;

    private void Start()  => SaveSystem.Instance?.Register(this);
    private void OnDisable() => SaveSystem.Instance?.Unregister(this);

    public object CaptureSaveData()
    {
        if (_motor == null) throw new InvalidOperationException("PlayerMotor not assigned.");

        Vector3 pos = _motor.transform.position;
        return new PlayerTransformSaveData
        {
            posX  = pos.x,
            posY  = pos.y,
            posZ  = pos.z,
            yaw   = _motor.transform.eulerAngles.y,
            pitch = _camera != null ? _camera.Pitch : 0f,
        };
    }

    public void RestoreSaveData(object data)
    {
        if (_motor == null) return;

        var dto = JsonUtility.FromJson<PlayerTransformSaveData>((string)data);
        if (dto == null) return;

        // Position + yaw through Teleport (zeroes velocity, syncs the Rigidbody).
        _motor.Teleport(new Vector3(dto.posX, dto.posY, dto.posZ),
                        Quaternion.Euler(0f, dto.yaw, 0f));

        // Pitch back onto the camera so the player faces exactly where they saved.
        if (_camera != null) _camera.SetPitch(dto.pitch);
    }
}

[Serializable]
public class PlayerTransformSaveData
{
    public float posX, posY, posZ;
    public float yaw;
    public float pitch;
}
