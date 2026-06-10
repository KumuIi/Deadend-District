# Core Systems — AI Reference

Four foundation systems built in Phase 1. Every future gameplay system should build on these rather than rolling its own equivalent.

---

## InteractionSystem

**Files:** `InteractionSystem/IInteractable.cs`, `InteractionSystem/PlayerInteractor.cs`

Make any object interactable by implementing `IInteractable` on a MonoBehaviour:

```csharp
public class Door : MonoBehaviour, IInteractable
{
    public bool   CanInteract(GameObject interactor) => !_isOpen;
    public string GetPrompt(GameObject interactor)   => "Open door";
    public void   Interact(GameObject interactor)    => Open();
}
```

`PlayerInteractor` (on the Player root) raycasts from the camera on E-press via `GameInputState.InteractPressed`. It exposes `Current` and `CurrentPrompt` for HUD use.

The `interactor` parameter lets AI agents call `Interact()` directly without `PlayerInteractor`.

---

## WorldStateManager

**Files:** `WorldState/WorldStateManager.cs`, `WorldState/WorldStateValue.cs`

Singleton on a `DontDestroyOnLoad` GameObject. Typed key/value store for all named world state.

```csharp
// Write
WorldStateManager.Instance.SetBool("door.factory_01.unlocked", true);
WorldStateManager.Instance.SetInt("quest.intro.stage", 2);

// Read
bool unlocked = WorldStateManager.Instance.GetBool("door.factory_01.unlocked");
int  stage    = WorldStateManager.Instance.GetInt("quest.intro.stage", fallback: 0);

// React to changes
WorldStateManager.Instance.OnStateChanged += (key, oldVal, newVal) => {
    if (key == "world.power.active") RefreshLights();
};
```

**Key naming convention:** `"category.identifier.property"` — e.g. `"npc.guard_a.dead"`, `"quest.intro.met_trader"`, `"door.factory_01.unlocked"`.

Supported types: `bool`, `int`, `float`, `string`. Persisted automatically by `WorldStateSaveAdapter`.

---

## StimulusSystem

**Files:** `StimulusSystem/StimulusSystem.cs`, `StimulusSystem/Stimulus.cs`, `StimulusSystem/IStimulusListener.cs`

Decoupled sensory event bus. `GunController` and `FootstepAudio` already emit `StimulusType.Sound`.

**Broadcasting** (from any system):
```csharp
StimulusSystem.Instance.Broadcast(new Stimulus(
    StimulusType.Sound,
    position:   transform.position,
    radius:     12f,
    intensity:  0.8f,
    source:     gameObject,      // the physical object making noise
    instigator: playerGO         // who caused it
));
```

**Listening** (AI, alarm triggers, etc.):
```csharp
public class GuardAI : MonoBehaviour, IStimulusListener
{
    public StimulusType[] ListensTo => new[] { StimulusType.Sound, StimulusType.Explosion };

    private void OnEnable()  => StimulusSystem.Instance.Register(this);
    private void OnDisable() => StimulusSystem.Instance.Unregister(this);

    public void OnStimulus(in Stimulus s)
    {
        if (s.Instigator == gameObject) return; // ignore own noise
        InvestigatePosition(s.Position);
    }
}
```

`StimulusType` enum: `Sound`, `Sight`, `Damage`, `Explosion`.

> **Performance note:** flat List dispatch is fine up to ~20 AI. See TODO in `StimulusSystem.cs` for spatial hash upgrade path.

---

## SaveSystem

**Files:** `SaveSystem/SaveSystem.cs`, `SaveSystem/ISaveable.cs`, `SaveSystem/SaveEnvelope.cs`, `SaveSystem/Adapters/`

Singleton on a `DontDestroyOnLoad` GameObject. Systems self-register via `ISaveable`.

**Adding a new saveable system:**
```csharp
public class MySystemSaveAdapter : MonoBehaviour, ISaveable
{
    public string SaveId   => "my.system";   // stable — never change this
    public string SaveType => "MySystem";

    private void Start()     => SaveSystem.Instance?.Register(this);
    private void OnDisable() => SaveSystem.Instance?.Unregister(this);

    public object CaptureSaveData() => new MyDTO { value = _mySystem.Value };

    public void RestoreSaveData(object data)
    {
        var dto = JsonUtility.FromJson<MyDTO>((string)data);
        _mySystem.Value = dto.value;
    }
}

[Serializable] public class MyDTO { public int value; }
```

**Triggering save/load** (call from a menu or checkpoint):
```csharp
SaveSystem.Instance.Save("slot0");
SaveSystem.Instance.Load("slot0");
bool hasSave = SaveSystem.Instance.SlotExists("slot0");
```

**Already-registered adapters:**
| SaveId | Covers |
|---|---|
| `player.inventory` | `InventoryGrid` contents via `InventoryUI`, including per-instance state (ammo stack counts, magazine rounds, weapon-loaded magazines, **flashlight charge + inserted battery**, loose battery charge) and the **equipped loadout** (which grid item is in hand as weapon / flashlight) |
| `player.health` | `PlayerHealth` current/max values |
| `world.state` | All `WorldStateManager` keys |

> **Per-instance state lives in `InventoryGrid.GridSaveEntry`** — every typed `ItemInstance` that holds mutable state (ammo, magazine, weapon, flashlight, battery) must add a capture case in `GetSaveData` *and* a matching restore case in `RestoreInstanceState`. Missing one means that item silently loads in its default-constructed state (e.g. a flashlight always loading empty). The equipped loadout is stored separately by `InventorySaveAdapter` as grid anchors — a grid position uniquely identifies a placed item, so restore re-equips the exact loaded instance (preserving its ammo/battery), never a fresh full one.

Save files are written to `Application.persistentDataPath/save_<slot>.json`. Version field is `1` — increment on breaking format changes.

---

## Adding to a Scene

One `GameSystems` GameObject (DontDestroyOnLoad) needs:
- `WorldStateManager`
- `StimulusSystem`
- `SaveSystem`
- `WorldStateSaveAdapter` (sibling of WorldStateManager)

Player root needs:
- `PlayerInteractor` (assign camera + Interaction LayerMask)
- `PlayerHealthSaveAdapter` (alongside PlayerHealth)

InventoryUI GameObject needs:
- `InventorySaveAdapter` (assign InventoryUI reference)
