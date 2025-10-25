using Unity.Cinemachine;
using UnityEngine;

namespace App.Runtime.Framework
{
    [RequireComponent(typeof(CinemachineCamera))]
    public class CinemachineDynamicFOV : MonoBehaviour
    {
        [SerializeField] private float defaultFOV = 10f; // デフォルトのFOV
        [SerializeField] private float smoothTime = 0.5f; // 補間のなめらかさ

        private CinemachineCamera vcam;
        private float currentVelocity = 0f; // Mathf.SmoothDamp用
        private float targetFOV;

        private void Start()
        {
            vcam = GetComponent<CinemachineCamera>();
            targetFOV = vcam.Lens.OrthographicSize;
        }

        private void LateUpdate()
        {
            // スムーズにFOVを補間
            var newFOV = Mathf.SmoothDamp(vcam.Lens.OrthographicSize, targetFOV, ref currentVelocity, smoothTime);
            vcam.Lens.OrthographicSize = newFOV;
        }
        
        public void SetTargetFOV(float fov)
        {
            targetFOV = fov;

            if (targetFOV <= 0f)
            {
                targetFOV = defaultFOV;
            }
        }
    }
}