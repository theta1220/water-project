using UnityEngine;

namespace App.Runtime.Player
{
    public class Prey : MonoBehaviour
    {
        [SerializeField] private float health = 0.5f;
        
        public void OnEaten(PredatorAgent predator)
        {
            health -= Time.fixedDeltaTime;

            if (IsDead())
            {
                predator.transform.parent = null;
                Destroy(gameObject);
            }
        }
        
        public bool IsDead()
        {
            return health <= 0f;
        }
    }
}