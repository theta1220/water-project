using UnityEngine;

namespace App.Runtime.Master
{
    [CreateAssetMenu(fileName = "GameMaster", menuName = "water/Master/GameMaster")]
    public class GameMaster : ScriptableObject
    {
        public int MaxProgress = 1000;
    }
}