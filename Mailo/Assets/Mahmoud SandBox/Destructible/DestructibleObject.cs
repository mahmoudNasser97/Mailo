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
        [SerializeField] private GameObject collectiblePrefab;

        private bool _playerInRange;
        private bool _canInteract = true;
        private Animator _playerAnimator;
        private ButtonPrompt _prompt;

        private void Start()
        {
            if (buttonPromptPrefab == null)
            {
                Debug.LogError("[DestructibleObject] buttonPromptPrefab not assigned.", this);
                enabled = false;
                return;
            }
            if (rayfireRigid == null)
            {
                Debug.LogError("[DestructibleObject] rayfireRigid not assigned.", this);
                enabled = false;
                return;
            }
            _prompt = Instantiate(buttonPromptPrefab, transform.position + Vector3.left * 2f + Vector3.up * 0.5f, Quaternion.identity);
            _prompt.Initialization();
            _prompt.SetText("T");
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
            if (_prompt == null) return;
            if (!other.transform.root.CompareTag("Player")) return;
            _playerInRange = true;
            _playerAnimator = other.transform.root.GetComponentInChildren<Animator>();
            if (_canInteract)
                _prompt.Show();
        }

        private void OnTriggerExit(Collider other)
        {
            if (_prompt == null) return;
            if (!other.transform.root.CompareTag("Player")) return;
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
            Vector3 spawnPos = transform.position + Vector3.up * 0.5f;

            GameObject item = collectiblePrefab != null
                ? Instantiate(collectiblePrefab, spawnPos, Quaternion.identity)
                : CreateDefaultItem(spawnPos);

            var shard = item.AddComponent<CollectibleShard>();
            shard.itemName = itemName;
            shard.buttonPromptPrefab = buttonPromptPrefab;
            enabled = false;
        }

        private GameObject CreateDefaultItem(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.3f;
            go.AddComponent<Rigidbody>();
            return go;
        }
    }
}
