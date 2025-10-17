using Unity.Cinemachine;
using UnityEngine;

namespace App.Runtime.Framework
{
    [RequireComponent(typeof(CinemachineCamera))]
    public class CinemachineDynamicFOV : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float minFOV = 40f; // 静止時の狭い画角
        [SerializeField] private float maxFOV = 70f; // 移動時の広い画角
        [SerializeField] private float speedThreshold = 1f; // この速度以上で広がり始める
        [SerializeField] private float smoothTime = 0.5f; // 補間のなめらかさ

        private CinemachineCamera vcam;
        private Vector3 lastPosition;
        private float currentVelocity = 0f; // Mathf.SmoothDamp用
        private float targetFOV;

        private void Start()
        {
            vcam = GetComponent<CinemachineCamera>();
            if (target == null)
                target = vcam.Follow;

            if (target != null)
                lastPosition = target.position;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // 移動速度を算出
            var speed = (target.position - lastPosition).magnitude / Time.deltaTime;
            lastPosition = target.position;

            // 速度に応じたFOVターゲット値を決定
            var t = speed / speedThreshold;
            targetFOV = Mathf.Lerp(minFOV, maxFOV, t);

            // スムーズにFOVを補間
            var newFOV = Mathf.SmoothDamp(vcam.Lens.OrthographicSize, targetFOV, ref currentVelocity, smoothTime);
            vcam.Lens.OrthographicSize = newFOV;
        }
    }
}