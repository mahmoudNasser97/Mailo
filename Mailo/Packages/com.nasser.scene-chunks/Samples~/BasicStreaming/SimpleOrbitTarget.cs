using UnityEngine;

namespace Nasser.SceneChunks.Samples
{
    /// <summary>Moves a transform along a lissajous path so you can watch streaming without a controller.</summary>
    public class SimpleOrbitTarget : MonoBehaviour
    {
        [SerializeField] private float rangeX = 300f;
        [SerializeField] private float rangeZ = 220f;
        [SerializeField] private float speed = 0.08f;

        private float _t;

        private void Update()
        {
            _t += Time.deltaTime * speed;
            Vector3 next = new Vector3(
                rangeX * Mathf.Sin(_t),
                transform.position.y,
                rangeZ * Mathf.Sin(_t * 1.37f + 0.6f));

            Vector3 dir = next - transform.position;
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.position = next;
        }
    }
}
