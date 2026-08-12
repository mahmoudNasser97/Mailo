# Destructible Object System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a destructible object system where the player presses T near a prop to smash it (RayFire shatters it), then can collect a random shard by pressing F, tracked by a simple on-screen counter.

**Architecture:** Three MonoBehaviour scripts — `PickupCounter` (singleton UI), `DestructibleObject` (on the prop), and `CollectibleShard` (added at runtime to a random fragment) — communicate through direct references and the singleton. The player's existing Animator and RayFire's `demolitionEvent` hook are the only external integrations.

**Tech Stack:** Unity, RayFire (`RayfireRigid`, namespace `RayFire`), TopDownEngine `ButtonPrompt` (namespace `MoreMountains.TopDownEngine`), TextMeshPro (`TMP_Text`)

## Global Constraints

- All scripts go in `Assets/Mahmoud SandBox/Destructible/`
- Namespace: `MailoGame`
- Player GameObject must have tag `"Player"`
- `RayfireRigid` on destructible object: Demolition Type = Runtime, Simulation Type = Dynamic
- `ButtonPrompt` API: `Initialization()`, `SetText(string)`, `Show()`, `Hide()`
- `RayfireRigid` API: `Demolish()`, `demolitionEvent.LocalEvent` (delegate `void(RayfireRigid)`), `fragments` (`List<RayfireRigid>`)
- No save/load, no multiple collectible shards, no damage accumulation before break

---

### Task 1: PickupCounter — singleton UI notification tracker

**Files:**
- Create: `Assets/Mahmoud SandBox/Destructible/PickupCounter.cs`

**Interfaces:**
- Produces: `PickupCounter.Instance` (singleton), `void Add(string itemName)`, `int GetCount(string itemName)`

- [ ] **Step 1: Create the folder**

In Unity's Project window, right-click `Assets/Mahmoud SandBox` → Create → Folder → name it `Destructible`.

- [ ] **Step 2: Write PickupCounter.cs**

Create `Assets/Mahmoud SandBox/Destructible/PickupCounter.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace MailoGame
{
    public class PickupCounter : MonoBehaviour
    {
        public static PickupCounter Instance { get; private set; }

        [SerializeField] private TMP_Text notificationText;
        [SerializeField] private float notificationDuration = 2f;
        [SerializeField] private float fadeDuration = 0.3f;

        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        private Coroutine _notificationCoroutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            notificationText.alpha = 0f;
        }

        public void Add(string itemName)
        {
            if (!_counts.ContainsKey(itemName))
                _counts[itemName] = 0;
            _counts[itemName]++;

            if (_notificationCoroutine != null)
                StopCoroutine(_notificationCoroutine);
            _notificationCoroutine = StartCoroutine(ShowNotification(itemName));
        }

        public int GetCount(string itemName) =>
            _counts.TryGetValue(itemName, out int count) ? count : 0;

        private IEnumerator ShowNotification(string itemName)
        {
            notificationText.text = $"+1 {itemName}  (total: {_counts[itemName]})";
            notificationText.alpha = 1f;
            yield return new WaitForSeconds(notificationDuration);
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                notificationText.alpha = 1f - (elapsed / fadeDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }
            notificationText.alpha = 0f;
        }
    }
}
```

- [ ] **Step 3: Create the PickupCounter Canvas in the scene**

In Unity Hierarchy:
1. Right-click → UI → Canvas. Name it `PickupCounterCanvas`.
2. Inspector → Canvas → Render Mode: **Screen Space - Overlay**.
3. Add `PickupCounter` component to `PickupCounterCanvas`.
4. Right-click `PickupCounterCanvas` → UI → Text - TextMeshPro. Name it `NotificationText`.
5. `NotificationText` Rect Transform: set anchors to **bottom-center**, Pos Y = 60, Width = 600, Height = 60.
6. `NotificationText` TMP settings: Font Size = 28, Alignment = Center, Color = white.
7. On `PickupCounter` Inspector, drag `NotificationText` into the **Notification Text** field.

- [ ] **Step 4: Smoke test in Play Mode**

Add this temporary script to any scene GameObject to verify:

```csharp
using UnityEngine;
using MailoGame;

public class PickupCounterTest : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
            PickupCounter.Instance.Add("Wood Fragment");
    }
}
```

Press Play → press P three times.
Expected: bottom-center shows `+1 Wood Fragment  (total: 1)`, updates on each press, fades out after 2 seconds.

Remove `PickupCounterTest` after confirming.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Mahmoud SandBox/Destructible/"
git commit -m "feat: add PickupCounter singleton for item collection tracking"
```

---

### Task 2: CollectibleShard — runtime component added to a debris fragment

**Files:**
- Create: `Assets/Mahmoud SandBox/Destructible/CollectibleShard.cs`

**Interfaces:**
- Consumes: `PickupCounter.Instance.Add(string)` from Task 1; `ButtonPrompt.Initialization()`, `.SetText()`, `.Show()`, `.Hide()`
- Produces: public fields `string itemName` and `ButtonPrompt buttonPromptPrefab` (set by DestructibleObject before the component's Start fires)

- [ ] **Step 1: Write CollectibleShard.cs**

Create `Assets/Mahmoud SandBox/Destructible/CollectibleShard.cs`:

```csharp
using UnityEngine;
using MoreMountains.TopDownEngine;

namespace MailoGame
{
    public class CollectibleShard : MonoBehaviour
    {
        [HideInInspector] public string itemName;
        [HideInInspector] public ButtonPrompt buttonPromptPrefab;

        private bool _playerInRange;
        private ButtonPrompt _prompt;

        private void Start()
        {
            var col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 1.5f;

            _prompt = Instantiate(buttonPromptPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);
            _prompt.Initialization();
            _prompt.SetText($"Press F to collect {itemName}");
        }

        private void OnDestroy()
        {
            if (_prompt != null)
                Destroy(_prompt.gameObject);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInRange = true;
            _prompt.Show();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInRange = false;
            _prompt.Hide();
        }

        private void Update()
        {
            if (_playerInRange && Input.GetKeyDown(KeyCode.F))
            {
                PickupCounter.Instance.Add(itemName);
                Destroy(gameObject);
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add "Assets/Mahmoud SandBox/Destructible/CollectibleShard.cs"
git commit -m "feat: add CollectibleShard runtime component for debris pickup"
```

---

### Task 3: DestructibleObject — prop controller (proximity, smash, demolish, tag shard)

**Files:**
- Create: `Assets/Mahmoud SandBox/Destructible/DestructibleObject.cs`

**Interfaces:**
- Consumes: `RayfireRigid.Demolish()`, `rayfireRigid.demolitionEvent.LocalEvent` (subscribe with `void(RayfireRigid)`), `rayfireRigid.fragments` (`List<RayfireRigid>`); `ButtonPrompt`; `CollectibleShard.itemName` and `CollectibleShard.buttonPromptPrefab` from Task 2
- Produces: self-contained component, no public API

- [ ] **Step 1: Write DestructibleObject.cs**

Create `Assets/Mahmoud SandBox/Destructible/DestructibleObject.cs`:

```csharp
using System.Collections;
using UnityEngine;
using RayFire;
using MoreMountains.TopDownEngine;

namespace MailoGame
{
    public class DestructibleObject : MonoBehaviour
    {
        [SerializeField] private string animatorTriggerName = "Smash";
        [SerializeField] private float animationDelay = 0.6f;
        [SerializeField] private string itemName = "Wood Fragment";
        [SerializeField] private ButtonPrompt buttonPromptPrefab;
        [SerializeField] private RayfireRigid rayfireRigid;

        private bool _playerInRange;
        private bool _canInteract = true;
        private Animator _playerAnimator;
        private ButtonPrompt _prompt;

        private void Start()
        {
            _prompt = Instantiate(buttonPromptPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);
            _prompt.Initialization();
            _prompt.SetText("Press T to break");
            rayfireRigid.demolitionEvent.LocalEvent += OnDemolished;
        }

        private void OnDestroy()
        {
            if (rayfireRigid != null)
                rayfireRigid.demolitionEvent.LocalEvent -= OnDemolished;
            if (_prompt != null)
                Destroy(_prompt.gameObject);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInRange = true;
            _playerAnimator = other.transform.root.GetComponentInChildren<Animator>();
            _prompt.Show();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInRange = false;
            _prompt.Hide();
        }

        private void Update()
        {
            if (_playerInRange && _canInteract && Input.GetKeyDown(KeyCode.T))
                StartSmash();
        }

        private void StartSmash()
        {
            _canInteract = false;
            _prompt.Hide();
            if (_playerAnimator != null)
                _playerAnimator.SetTrigger(animatorTriggerName);
            StartCoroutine(DemolishAfterDelay());
        }

        private IEnumerator DemolishAfterDelay()
        {
            yield return new WaitForSeconds(animationDelay);
            rayfireRigid.Demolish();
        }

        private void OnDemolished(RayfireRigid rigid)
        {
            if (rigid.fragments == null || rigid.fragments.Count == 0) return;
            int index = Random.Range(0, rigid.fragments.Count);
            var shard = rigid.fragments[index];
            var collectible = shard.gameObject.AddComponent<CollectibleShard>();
            collectible.itemName = itemName;
            collectible.buttonPromptPrefab = buttonPromptPrefab;
            enabled = false;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add "Assets/Mahmoud SandBox/Destructible/DestructibleObject.cs"
git commit -m "feat: add DestructibleObject with RayFire demolition and smash animation"
```

---

### Task 4: Scene wiring — set up and test one destructible prop end-to-end

**Files:** No new scripts. Scene and Inspector wiring only.

- [ ] **Step 1: Look up your player's smash Animator trigger name**

Select the Player in the Hierarchy → find the Animator component → open the Animator Controller (double-click it) → look in the Parameters panel for the attack/smash trigger name (e.g., `"Attack"` or `"Smash"`). Write it down — you'll need the exact string.

- [ ] **Step 2: Place a test destructible prop in the scene**

1. Place any mesh GameObject in the scene (cube, imported prop, etc.). Name it `TestDestructible`.
2. Add component: `RayfireRigid`.
   - Demolition Type: **Runtime**
   - Simulation Type: **Dynamic**
3. Add component: `DestructibleObject`.
4. Add component: `SphereCollider` → check **Is Trigger** → set Radius to **2.0**.

- [ ] **Step 3: Wire DestructibleObject Inspector fields**

On the `DestructibleObject` component:
- **Animator Trigger Name**: paste the exact trigger name from Step 1
- **Animation Delay**: set to the attack animation's length in seconds (e.g., `0.5`)
- **Item Name**: `Wood Fragment`
- **Button Prompt Prefab**: find the `ButtonPrompt` prefab in the TopDownEngine folder (search Project for `ButtonPrompt`) and drag it in
- **Rayfire Rigid**: drag the `RayfireRigid` component from `TestDestructible` into this field

- [ ] **Step 4: Confirm Player tag**

Select the Player GameObject → Inspector → Tag dropdown → confirm it says **Player**. Set it if not.

- [ ] **Step 5: Full end-to-end test in Play Mode**

Press Play and verify each step:

1. Walk near `TestDestructible` → **"Press T to break"** prompt fades in above the object.
2. Walk away → prompt fades out.
3. Walk near again → press **T** → player plays the smash animation.
4. After the animation delay → object shatters into pieces (RayFire runtime fracture).
5. Walk near any shard → **"Press F to collect Wood Fragment"** prompt appears.
6. Press **F** → shard disappears, bottom-center shows **"+1 Wood Fragment  (total: 1)"**, fades out after 2 seconds.
7. Press T on the now-destroyed object → nothing happens (one-shot confirmed).

- [ ] **Step 6: Commit scene**

```bash
git add Assets/Scenes/
git commit -m "feat: wire test destructible object in scene"
```
