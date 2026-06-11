using UnityEngine;

namespace Playground
{
    public class CrosshairUI : MonoBehaviour
    {
        [SerializeField] private float size  = 10f;
        [SerializeField] private float gap   = 4f;
        [SerializeField] private Color color = Color.white;

        private void OnGUI()
        {
            float cx = Screen.width  * 0.5f;
            float cy = Screen.height * 0.5f;

            GUI.color = color;
            GUI.DrawTexture(new Rect(cx - gap - size, cy - 1f,          size, 2f),  Texture2D.whiteTexture); // left
            GUI.DrawTexture(new Rect(cx + gap,        cy - 1f,          size, 2f),  Texture2D.whiteTexture); // right
            GUI.DrawTexture(new Rect(cx - 1f,         cy - gap - size,  2f,   size), Texture2D.whiteTexture); // up
            GUI.DrawTexture(new Rect(cx - 1f,         cy + gap,         2f,   size), Texture2D.whiteTexture); // down
            // Centre dot
            GUI.DrawTexture(new Rect(cx - 1f,         cy - 1f,          2f,   2f),  Texture2D.whiteTexture);
        }
    }
}
