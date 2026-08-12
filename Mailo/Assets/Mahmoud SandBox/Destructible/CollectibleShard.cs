using System.Collections;
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

            var glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.75f, 0.1f);
            glow.range = 3f;
            glow.intensity = 1.5f;
            StartCoroutine(PulseGlow(glow));

            _prompt = Instantiate(buttonPromptPrefab, transform.position + Vector3.left * 2f + Vector3.up * 0.5f, Quaternion.identity);
            _prompt.transform.SetParent(transform);
            _prompt.transform.localPosition = Vector3.left * 2f + Vector3.up * 0.5f;
            _prompt.Initialization();
            _prompt.SetText("F");
        }

        private IEnumerator PulseGlow(Light glow)
        {
            float baseIntensity = glow.intensity;
            while (true)
            {
                glow.intensity = baseIntensity + Mathf.Sin(Time.time * 3f) * 0.5f;
                yield return null;
            }
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
