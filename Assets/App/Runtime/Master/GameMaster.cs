using UnityEngine;

namespace App.Runtime.Master
{
    [CreateAssetMenu(fileName = "GameMaster", menuName = "water/Master/GameMaster")]
    public class GameMaster : ScriptableObject
    {
        public int MaxProgress = 71;
        public float HuningFOV = 5f;
        public float SmoothDamping = 0.1f;
        public float HundtedSineSpeed = 2;
    }
}