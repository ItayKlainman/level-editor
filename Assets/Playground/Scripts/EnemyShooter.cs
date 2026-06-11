using UnityEngine;

namespace Playground
{
    public class EnemyShooter : MonoBehaviour
    {
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private float fireInterval  = 2f;
        [SerializeField] private float detectionRange = 25f;
        [SerializeField] private Vector3 fireOffset  = new Vector3(0f, 1f, 0f);

        private float     _nextFireTime;
        private Transform _player;

        private void Start()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _player = p.transform;
            // Stagger first shot so all enemies don't fire simultaneously
            _nextFireTime = Time.time + Random.Range(0.5f, fireInterval);
        }

        private void Update()
        {
            if (_player == null || projectilePrefab == null) return;
            if (Vector3.Distance(transform.position, _player.position) > detectionRange) return;
            if (Time.time < _nextFireTime) return;

            Fire();
            _nextFireTime = Time.time + fireInterval;
        }

        private void Fire()
        {
            Vector3 origin = transform.position + fireOffset;
            Vector3 target = _player.position + Vector3.up * 1f;
            Vector3 dir    = (target - origin).normalized;

            var go = Instantiate(projectilePrefab, origin, Quaternion.LookRotation(dir));
            go.GetComponent<Projectile>().ignoreTag = "Enemy";

            // Don't let the bullet hit this enemy's own collider
            var projCol = go.GetComponent<Collider>();
            if (projCol != null)
                foreach (var c in GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(projCol, c);
        }
    }
}
