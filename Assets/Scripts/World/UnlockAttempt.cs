/// <summary>
/// Result of a <see cref="LockedDoor"/> unlock attempt. Lets the base class drive the
/// open/feedback flow without knowing how a subclass validates credentials.
///
/// <see cref="Pending"/> exists for async unlocks (e.g. a keypad UI that resolves later):
/// the door stays locked now and a subclass calls <see cref="LockedDoor.CompleteExternalUnlock"/>
/// once the player succeeds. This avoids overloading "false" to mean both "denied" and "waiting".
/// </summary>
public enum UnlockAttempt
{
    /// <summary>The attempt was denied (wrong/missing credential). Door stays locked; play denied feedback.</summary>
    Failed,

    /// <summary>Credential accepted. The base opens the door immediately.</summary>
    Succeeded,

    /// <summary>Unlock is in progress out-of-band (UI). Door stays locked until CompleteExternalUnlock().</summary>
    Pending,
}
