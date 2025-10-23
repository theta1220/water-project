using App.Runtime.Player;
using App.Runtime.UI;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEngine;

namespace App.Runtime.Common
{
    public class InGameContents : MonoBehaviour
    {
        public static InGameContents Instance { get; private set; }
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            myPredator = Instantiate(myPredator, Vector3.zero, quaternion.identity);
            cam.Target.TrackingTarget = myPredator.transform;
        }
        
        [SerializeField] private Health healthUI;
        [SerializeField] private PredatorAgent myPredator;
        [SerializeField] private CinemachineCamera cam;
        
        public Health HealthUI => healthUI;
        public PredatorAgent MyPredator => myPredator;
    }
}