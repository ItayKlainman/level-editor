using UnityEngine;

namespace Playground
{
    public class DummyTarget : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;

        private float   _health;
        private Camera  _cam;

        private void Awake()
        {
            _health = maxHealth;
            _cam    = Camera.main;
        }

        public void TakeDamage(float amount)
        {
            _health = Mathf.Max(0f, _health - amount);
            if (_health <= 0f) Destroy(gameObject);
        }

        private void OnGUI()
        {
            if (_cam == null) return;

            Vector3 screen = _cam.WorldToScreenPoint(transform.position + Vector3.up * 1.8f);
            if (screen.z < 0f) return;

            const float barW = 100f, barH = 14f;
            float x = screen.x - barW * 0.5f;
            float y = Screen.height - screen.y - barH * 0.5f;

            // Black border
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(x - 1, y - 1, barW + 2, barH + 2), Texture2D.whiteTexture);

            // Empty bar (dark red)
            GUI.color = new Color(0.4f, 0f, 0f);
            GUI.DrawTexture(new Rect(x, y, barW, barH), Texture2D.whiteTexture);

            // Filled portion (green → red)
            float pct = _health / maxHealth;
            GUI.color = Color.Lerp(Color.red, Color.green, pct);
            GUI.DrawTexture(new Rect(x, y, barW * pct, barH), Texture2D.whiteTexture);

            // Text
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y, barW, barH), $"  {_health:0} / {maxHealth:0}");
        }
    }
}
