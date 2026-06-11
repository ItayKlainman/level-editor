using UnityEngine;

namespace Playground
{
    [RequireComponent(typeof(Rigidbody))]
    public class Projectile : MonoBehaviour
    {
        [SerializeField] private float speed  = 30f;
        [SerializeField] private float damage = 25f;
        // Objects with this tag are not damaged (set per-prefab or after Instantiate)
        public string ignoreTag = "";

        private void Awake()
        {
            var rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
            rb.linearVelocity = transform.forward * speed;
            Destroy(gameObject, 5f);
        }

        private void OnCollisionEnter(Collision col)
        {
            if (!string.IsNullOrEmpty(ignoreTag) && col.gameObject.CompareTag(ignoreTag))
                return;

            col.gameObject.GetComponent<IDamageable>()?.TakeDamage(damage);
            Destroy(gameObject);
        }
    }
}
