using UnityEngine;

namespace App.Runtime.Framework
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class RigidBody2DSetting : MonoBehaviour
    {
        [SerializeField] private float inertia = 0f;
        
        public void Awake()
        {
            var rb2d = GetComponent<Rigidbody2D>();
            rb2d.inertia = inertia;
        }
    }
}