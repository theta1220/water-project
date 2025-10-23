using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace App.Runtime.Player
{
    public class PlayerSegment : MonoBehaviour
    {
        [SerializeField] private int delaySize = 5;
        [SerializeField] private float dampening = 0.2f;
        
        private Transform parentTransform;
        private Quaternion targetRotation;
        private Quaternion rotationOffset;
        private Queue<Quaternion> parentPreviousRotationQueue;

        private void Start()
        {
            parentTransform = transform.parent;
            if (parentTransform != null)
            {
                targetRotation = transform.rotation;
                rotationOffset = Quaternion.Inverse(parentTransform.rotation) * targetRotation;
                parentPreviousRotationQueue = new Queue<Quaternion>();
                parentPreviousRotationQueue.Enqueue(parentTransform.rotation);
            }
        }

        private void FixedUpdate()
        {
            if (parentTransform == null) return;

            // 親の回転を取得
            var parentRotation = parentTransform.rotation;

            if (parentPreviousRotationQueue.Count > delaySize)
            {
                parentPreviousRotationQueue.Dequeue();
            }

            var parentPreviousRotation = parentPreviousRotationQueue.First();
            // 親オブジェクトの回転を補間して追従
            targetRotation = Quaternion.Slerp(
                targetRotation,
                parentPreviousRotation * rotationOffset,
                dampening
            );

            // 回転を適用
            transform.rotation = targetRotation;
            parentPreviousRotationQueue.Enqueue(parentRotation);
        }
    }
}