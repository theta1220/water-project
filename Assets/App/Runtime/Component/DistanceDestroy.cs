using App.Runtime.Common;
using UnityEngine;

namespace App.Runtime.Component
{
    public class DistanceDestroy : MonoBehaviour
    {
        [SerializeField] private float destroyDistance = 50f;
        [SerializeField] private bool debugLogs;
        
        public void FixedUpdate()
        {
            if (!InGameContents.Instance.MyPredator)
            {
                return;
            }
            
            var target = InGameContents.Instance.MyPredator.transform;
            if (target == null)
            {
                return;
            }
            
            var distance = Vector3.Distance(target.position, transform.position);
            if (distance > destroyDistance)
            {
                Destroy(gameObject);
            }
            
            if (debugLogs)
            {
                Debug.Log($"DistanceDestroy: Distance to target = {distance}");
            }
        }
        
        
    }
}