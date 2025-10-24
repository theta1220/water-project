using UnityEngine;
using UnityEngine.UI;

namespace App.Runtime.UI
{
    public class UIHealth : MonoBehaviour
    {
        [SerializeField] private Image healthBarFill;
        
        public void SetHealth(float healthNormalized)
        {
            if (healthBarFill != null)
            {
                healthBarFill.fillAmount = Mathf.Clamp01(healthNormalized);
            }
        }
    }
}