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
            if (buttonPromptPrefab == null)
            {
                Debug.LogError("[CollectibleShard] buttonPromptPrefab not assigned.", this);
                enabled = false;
                return;
            }
            var col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 1.5f;

            _prompt = Instantiate(buttonPromptPrefab, transform.position + Vector3.left * 2f + Vector3.up * 0.5f, Quaternion.identity);
            _prompt.transform.SetParent(transform);
            _prompt.transform.localPosition = Vector3.left * 2f + Vector3.up * 0.5f;
            _prompt.Initialization();
            _prompt.SetText("F");
        }

        private void OnDestroy()
        {
            if (_prompt != null)
                Destroy(_prompt.gameObject);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_prompt == null) return;
            if (!other.transform.root.CompareTag("Player")) return;
            _playerInRange = true;
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
            if (_playerInRange && Input.GetKeyDown(KeyCode.F))
            {
                PickupCounter.Instance.Add(itemName);
                Destroy(gameObject);
            }
        }
    }
}
