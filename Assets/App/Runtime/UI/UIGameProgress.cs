using TMPro;
using UnityEngine;

namespace App.Runtime.UI
{
    public class UIGameProgress : MonoBehaviour
    {
        [SerializeField] private TMP_Text _text;
        [SerializeField] private TMP_Text _gaugeBase;
        [SerializeField] private TMP_Text _gaugeFill;

        private int _gaugeNum;
        
        public void Awake()
        {
            _gaugeNum = _gaugeBase.text.Length;
        }
        
        public void SetProgress(int progress, int max)
        {
            // _text.text = $"{((float)progress / max * 100):000.00}%";
            _gaugeFill.text = new string('■', Mathf.FloorToInt(((float)progress / max) * _gaugeNum));
        }
    }
}