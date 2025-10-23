using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace App.Runtime.Component
{
    public class ResetOrigin : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private float distanceThreshold = 100f;
        
        private void FixedUpdate()
        {
            var magnitude = _target.position.magnitude;
            if (magnitude < distanceThreshold)
            {
                return;
            }

            var targetPosition = _target.position;
            var allTransforms = SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var child in allTransforms)
            {
                child.gameObject.transform.position -= targetPosition;
            }
        }
    }
}