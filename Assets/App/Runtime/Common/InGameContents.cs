using App.Runtime.Framework;
using App.Runtime.Master;
using App.Runtime.Player;
using App.Runtime.UI;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

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
        }
        
        [SerializeField] private MasterContainer masterContainer;
        [SerializeField] private PredatorAgent myPredatorPrefab;
        [SerializeField] private UIHealth health;
        [SerializeField] private UIGameProgress gameProgress;
        [SerializeField] private CinemachineCamera cam;
        [SerializeField] private CinemachineDynamicFOV dynamicFOV;
        
        private PredatorAgent myPredator;
        
        public MasterContainer MasterContainer => masterContainer;
        public PredatorAgent MyPredator => myPredator;
        public UIGameProgress GameProgress => gameProgress;
        public UIHealth Health => health;
        public CinemachineDynamicFOV DynamicFOV => dynamicFOV;
        
        public void Initialize()
        {
            myPredator = Instantiate(myPredatorPrefab, Vector3.zero, quaternion.identity);
            cam.Target.TrackingTarget = myPredator.transform;
            
            health.SetHealth(1);
            gameProgress.SetProgress(0, masterContainer.GameMaster.MaxProgress);
            dynamicFOV.SetTargetFOV(0);
        }
    }
}