using UnityEngine;

namespace App.Runtime.Master
{
    [CreateAssetMenu(fileName = "MasterContainer", menuName = "water/Master/MasterContainer")]
    public class MasterContainer : ScriptableObject
    {
        public GameMaster GameMaster;
    }
}