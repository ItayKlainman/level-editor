using UnityEngine;
using UnityEngine.InputSystem;

namespace Playground
{
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float jumpForce = 5f;
        [SerializeField] private int maxJumps = 2;
        [SerializeField] public Transform cameraTransform;

        private Rigidbody _rb;
        private int _jumpsLeft;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // Camera-relative axes projected onto XZ
            Vector3 fwd   = cameraTransform ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = cameraTransform ? Vector3.ProjectOnPlane(cameraTransform.right,   Vector3.up).normalized : Vector3.right;

            float h = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float v = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);

            Vector3 move = (fwd * v + right * h).normalized * moveSpeed;
            _rb.linearVelocity = new Vector3(move.x, _rb.linearVelocity.y, move.z);

            // Always face the camera's yaw
            if (cameraTransform)
                transform.rotation = Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f);

            // Double jump
            if (kb.spaceKey.wasPressedThisFrame && _jumpsLeft > 0)
            {
                _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
                _rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
                _jumpsLeft--;
            }
        }

        private void OnCollisionStay(Collision col)
        {
            if (col.gameObject.CompareTag("Ground"))
                _jumpsLeft = maxJumps;
        }

        private void OnCollisionExit(Collision col)
        {
            if (col.gameObject.CompareTag("Ground") && _jumpsLeft == maxJumps)
                _jumpsLeft = Mathf.Max(0, _jumpsLeft - 1);
        }
    }
}
