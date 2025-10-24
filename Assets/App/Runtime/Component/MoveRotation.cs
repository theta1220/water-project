using UnityEngine;

namespace App.Runtime.Component
{
    public class MoveRotation : MonoBehaviour
    {
        [SerializeField] private float speed = 10f;
        [SerializeField] private Vector3 rotationAxis = Vector3.up;
        
        private void Update()
        {
            // 無限に回転させるように途中にリセットを入れる
            transform.Rotate(rotationAxis, speed * Time.deltaTime);
            
            // リセット
            var eulerAngles = transform.rotation.eulerAngles;
            if (eulerAngles.z > 360f)
            {
                eulerAngles.z -= 360f;
                transform.rotation = Quaternion.Euler(eulerAngles);
            }
        }
    }
}