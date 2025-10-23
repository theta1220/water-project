using App.Runtime.Common;
using UnityEngine;

namespace App.Runtime.Component
{
    public class DistanceDestroy : MonoBehaviour
    {
        [SerializeField] private float destroyDistance = 50f;
        [SerializeField] private bool debugLogs;
        private Transform _target;

        public void Start()
        {
            _target = InGameContents.Instance.MyPredator.transform;
        }
        
        public void FixedUpdate()
        {
            var distance = Vector3.Distance(_target.position, transform.position);
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