using UnityEngine;
using UnityEngine.InputSystem;

namespace Playground
{
    public class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);
        [SerializeField] private float distance = 6f;
        [SerializeField] private float minPitch = -20f;
        [SerializeField] private float maxPitch = 60f;
        [SerializeField] private float sensitivity = 200f;
        [SerializeField] private float smoothTime = 0.1f;
        [SerializeField] private LayerMask collisionMask = ~0;

        private float _yaw;
        private float _pitch = 20f;
        private Vector3 _velocity;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                // Skip delta on the click frame — avoids cursor-unlock spike in Editor
                if (!mouse.leftButton.wasPressedThisFrame && !mouse.leftButton.wasReleasedThisFrame)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    _yaw   += delta.x * sensitivity * Time.deltaTime;
                    _pitch -= delta.y * sensitivity * Time.deltaTime;
                    _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);
                }
            }

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = target.position + targetOffset;
            Vector3 desiredPos = pivot - rotation * Vector3.forward * distance;

            if (Physics.SphereCast(pivot, 0.2f, desiredPos - pivot,
                out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore))
                desiredPos = pivot + (desiredPos - pivot).normalized * (hit.distance - 0.1f);

            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _velocity, smoothTime);
            transform.LookAt(pivot);
        }
    }
}
