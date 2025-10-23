using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace App.Runtime.Player
{
    public class PlayerSineMove : MonoBehaviour
    {
        [SerializeField] private float sineAmplitude = 5f; // 振幅
        [SerializeField] private float sineFrequency = 5f; // 周波数
        [SerializeField] private float sinePhase = 0f; // 位相
        
        private float _time = 0f;

        private void FixedUpdate()
        {
            // Sinでゆらぎを与える
            var sineAngle = Mathf.Sin(_time * sineFrequency + sinePhase) * sineAmplitude;
            var sineRotation = Quaternion.Euler(0f, 0f, sineAngle);
            transform.rotation *= sineRotation;

            _time += Time.fixedDeltaTime;
        }
    }
}