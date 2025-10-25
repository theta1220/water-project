using UnityEngine;

namespace App.Runtime.Player.Param
{
    [CreateAssetMenu(fileName = "PredatorParam", menuName = "water/Param/PredatorParam")]
    public class PredatorParam : ScriptableObject
    {
        public float decayFactor = 0.99f;

        [Header("Hunting")] 
        public float eatImpulse = 0f; // 捕食時のインパルス
        public float suctionForce = 6f;

        [Header("Health")] public float maxHealth = 100f; 
        public float currentHealth;
        public float healthDecayRate = 1f; // 1秒あたりの体力減少量
        public float collisionDamage = 10f; // 衝突1回あたりのダメージ
        public float healthRecoverOnEat = 20f; // 捕食1回あたりの回復量

        [Header("Movement")] 
        public float accel = 3f;
        public float dragLinear = 0.5f;
        public float preBoostMultiplier = 0.2f;
        public float boostMultiplier = 5f;
        public float headWobbleFrequency = 2f; // 揺れの速さ
        public float moveWobbleAmplitude = 0f; // 揺れの大きさ（移動）
        public float wobblePhase; // 揺れの位相
        public float rotationSpeed = 0.1f;
        public float huntedForce = 10;
        public float huntedTorque = 10;
        public float huntedSineSpeed = 2;
        public float huntedSineScale = 2;

        [Header("AI Settings")] 
        public float wanderInterval = 0.1f;
        public float wanderJitter = 0.2f;
        public float wanderStrength = 0.1f;
        public float targetRefreshInterval = 0.1f;

        [Header("External Target")] [Tooltip("グリッドマネージャなど外部システムからターゲットを受け取る場合に使用")]
        public bool useExternalTarget = true;
    }
}