using UnityEngine;

namespace App.Runtime.Framework
{
    public class ApplicationSettings : MonoBehaviour
    {
        [SerializeField] private Vector2Int resolution = new Vector2Int(1920, 1080);

        private void Awake()
        {
            // アプリケーションのターゲットフレームレートを設定
            Application.targetFrameRate = 60;

            Screen.SetResolution(resolution.x, resolution.y, FullScreenMode.MaximizedWindow);
        }
    }
}