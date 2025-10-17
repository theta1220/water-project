using UnityEngine;

namespace App.Runtime.Framework
{
    public class AudioRandomPitcher : MonoBehaviour
    {
        [SerializeField] private AudioSource _audioSource;

        [Header("Pitch Randomization")] [SerializeField]
        private float _minPitch = 0.9f;

        [SerializeField] private float _maxPitch = 1.1f;

        private void Awake()
        {
            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
            }

            _audioSource.pitch = Random.Range(_minPitch, _maxPitch);
        }
    }
}