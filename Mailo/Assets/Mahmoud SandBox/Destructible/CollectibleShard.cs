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
