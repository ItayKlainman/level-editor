using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Playground
{
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;

        private float    _health;
        private bool     _isDead;
        private Rigidbody _rb;

        private void Awake()
        {
            _health = maxHealth;
            _rb     = GetComponent<Rigidbody>();
        }

        public void TakeDamage(float amount)
        {
            if (_isDead) return;
            _health = Mathf.Max(0f, _health - amount);
            if (_health <= 0f) Die();
        }

        private void Die()
        {
            _isDead = true;

            // Stop movement and shooting
            var ctrl    = GetComponent<PlayerController>();
            var shooter = GetComponent<Shooter>();
            if (ctrl    != null) ctrl.enabled    = false;
            if (shooter != null) shooter.enabled = false;

            // Unlock rotation so the capsule can topple over
            if (_rb != null)
            {
                _rb.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
                _rb.AddTorque(transform.right * 5f, ForceMode.Impulse);
            }
        }

        private void Update()
        {
            if (!_isDead) return;
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame)
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnGUI()
        {
            const float barW = 200f, barH = 20f, margin = 20f;
            float x = margin;
            float y = Screen.height - margin - barH;

            if (!_isDead)
            {
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(x - 1, y - 1, barW + 2, barH + 2), Texture2D.whiteTexture);
                GUI.color = new Color(0.35f, 0f, 0f);
                GUI.DrawTexture(new Rect(x, y, barW, barH), Texture2D.whiteTexture);
                float pct = _health / maxHealth;
                GUI.color = Color.Lerp(Color.red, Color.green, pct);
                GUI.DrawTexture(new Rect(x, y, barW * pct, barH), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(x + 4, y + 2, barW, barH), $"HP  {_health:0} / {maxHealth:0}");
            }
            else
            {
                float sw = Screen.width, sh = Screen.height;

                // Dark overlay
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);

                var big = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 52,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
                big.normal.textColor = Color.red;
                GUI.Label(new Rect(0, sh * 0.33f, sw, 70f), "YOU DIED", big);

                var small = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 22,
                    alignment = TextAnchor.MiddleCenter,
                };
                small.normal.textColor = Color.white;
                GUI.Label(new Rect(0, sh * 0.53f, sw, 40f), "Press  SPACE  to restart", small);
            }
        }
    }
}
