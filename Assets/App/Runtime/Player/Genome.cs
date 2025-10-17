using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace App.Runtime.Player
{
    [Serializable]
    public class Genome
    {
        [Header("Core stats")] [Range(0.1f, 3f)]
        public float speed = 1.0f;

        [Range(0.1f, 3f)] public float viscosity = 1.0f;
        [Range(0.2f, 3f)] public float size = 1.0f;
        [Range(0.5f, 10f)] public float senseRange = 4.0f;
        [Range(0.05f, 2f)] public float biteDistance = 0.45f;
        [Range(0.0f, 1f)] public float metabolism = 0.4f;

        [Header("Aesthetic")] [Range(0f, 1f)] public float hue = 0.6f;
        [Range(0f, 2f)] public float emission = 0.2f;

        /// <summary>
        /// 捕食者のゲノムを吸収し、突然変異を適用します。
        /// </summary>
        /// <param name="prey">捕食されるゲノム</param>
        /// <param name="preyWeight">捕食者のゲノムの重み</param>
        /// <param name="mutationStrength">突然変異の強さ</param>
        public void Absorb(Genome prey, float preyWeight, float mutationStrength)
        {
            var selfWeight = Mathf.Max(0f, 1f - preyWeight);
            speed = Mutate(LerpClamp(speed, prey.speed, selfWeight, preyWeight), 0.10f, mutationStrength);
            viscosity = Mutate(LerpClamp(viscosity, prey.viscosity, selfWeight, preyWeight), 0.10f, mutationStrength);
            size = Mutate(LerpClamp(size, prey.size, selfWeight, preyWeight), 0.20f, mutationStrength);
            senseRange = Mutate(LerpClamp(senseRange, prey.senseRange, selfWeight, preyWeight), 0.50f,
                mutationStrength);
            biteDistance = Mutate(LerpClamp(biteDistance, prey.biteDistance, selfWeight, preyWeight), 0.05f,
                mutationStrength);
            metabolism = Mutate(LerpClamp(metabolism, prey.metabolism, selfWeight, preyWeight), 0.05f,
                mutationStrength);

            hue = Mathf.Repeat(ShortHueLerp(hue, prey.hue, preyWeight), 1f);
            emission = Mutate(LerpClamp(emission, prey.emission, selfWeight, preyWeight), 0.10f, mutationStrength);
        }
        
        /// <summary>
        /// ゲノムの値を時間とともに減衰させます。
        /// </summary>
        /// <param name="decayFactor">減衰係数</param>
        public void Decay(float decayFactor)
        {
            speed = Mathf.Max(0.1f, speed * decayFactor);
            viscosity = Mathf.Max(0.1f, viscosity * decayFactor);
            size = Mathf.Max(0.2f, size * decayFactor);
            senseRange = Mathf.Max(0.5f, senseRange * decayFactor);
            biteDistance = Mathf.Max(0.05f, biteDistance * decayFactor);
            metabolism = Mathf.Clamp01(metabolism * decayFactor);

            emission = Mathf.Max(0f, emission * decayFactor);
        }

        private static float LerpClamp(float a, float b, float wa, float wb)
        {
            var sum = wa + wb;
            if (sum <= 0f)
            {
                return a;
            }
            return Mathf.Lerp(a, b, Mathf.Clamp01(wb / sum));
        }

        private static float Mutate(float v, float span, float strength)
        {
            var n = (Random.value + Random.value + Random.value) / 3f - 0.5f;
            return Mathf.Clamp(v + n * span * strength, 0.001f, 999f);
        }

        private static float ShortHueLerp(float h0, float h1, float t)
        {
            var d = Mathf.Repeat(h1 - h0 + 0.5f, 1f) - 0.5f;
            return h0 + d * t;
        }

        /// <summary>
        /// ゲノムのクローンを作成します。
        /// </summary>
        /// <returns>ゲノムの新しいインスタンス</returns>
        public Genome Clone() => (Genome)MemberwiseClone();
    }
}