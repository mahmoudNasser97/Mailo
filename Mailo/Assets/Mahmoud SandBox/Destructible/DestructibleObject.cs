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
            if (buttonPromptPrefab == null) { Debug.LogError("[DestructibleObject] buttonPromptPrefab not assigned.", this); return; }
            if (rayfireRigid == null) { Debug.LogError("[DestructibleObject] rayfireRigid not assigned.", this); return; }
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
            if (_canInteract)
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
