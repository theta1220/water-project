using App.Runtime.Player;
using App.Runtime.UI;
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
        }
        
        [SerializeField] private Health healthUI;
        [SerializeField] private PredatorAgent myPredator;
        
        public Health HealthUI => healthUI;
        public PredatorAgent MyPredator => myPredator;
    }
}