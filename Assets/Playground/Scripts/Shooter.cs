using UnityEngine;
using UnityEngine.InputSystem;

namespace Playground
{
    public class Shooter : MonoBehaviour
    {
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Camera     shootCamera;
        [SerializeField] private float      fireRate = 0.25f;

        private float _nextFireTime;

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame && Time.time >= _nextFireTime)
            {
                Fire();
                _nextFireTime = Time.time + fireRate;
            }
        }

        private void Fire()
        {
            if (projectilePrefab == null || shootCamera == null) return;

            // Direction from screen centre — same as before
            Ray ray = shootCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            // Spawn from player chest so the bullet is never between the camera and player.
            // The camera's SphereCast goes from player toward camera; a bullet spawned at the
            // camera position lands right in that path and yanks the camera inward every shot.
            Vector3 spawnPos = transform.position + Vector3.up * 1.3f + transform.forward * 0.6f;

            var go = Instantiate(projectilePrefab, spawnPos, Quaternion.LookRotation(ray.direction));
            go.GetComponent<Projectile>().ignoreTag = "Player";

            var projCol = go.GetComponent<Collider>();
            if (projCol != null)
                foreach (var c in GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(projCol, c);
        }
    }
}
