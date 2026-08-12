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
            if (notificationText == null)
            {
                Debug.LogError("[PickupCounter] notificationText not assigned.", this);
                enabled = false;
                return;
            }
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
