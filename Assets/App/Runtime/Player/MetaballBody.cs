using UnityEngine;

namespace App.Runtime.Player
{
    public class MetaballBody : MonoBehaviour
    {
        public Renderer targetRenderer;
        public SpriteRenderer spriteRenderer;
        public string viscosityProp = "_Viscosity";
        public string hueProp = "_Hue";
        public string emissionProp = "_Emission";
        public float baseScale = 0.8f;

        /// <summary>
        /// ゲノム情報を適用して見た目を更新します。
        /// </summary>
        /// <param name="g">適用するゲノム</param>
        public void ApplyGenome(Genome g)
        {
            var s = Mathf.Max(0.1f, baseScale * g.size);
            transform.localScale = Vector3.one * s;

            if (targetRenderer && targetRenderer.material)
            {
                var mat = targetRenderer.material;
                if (mat.HasProperty(viscosityProp)) mat.SetFloat(viscosityProp, g.viscosity);
                if (mat.HasProperty(hueProp)) mat.SetFloat(hueProp, g.hue);
                if (mat.HasProperty(emissionProp)) mat.SetFloat(emissionProp, g.emission);
            }

            if (spriteRenderer)
            {
                var c = Color.HSVToRGB(g.hue, 0.8f, Mathf.Clamp01(0.5f + g.emission * 0.5f));
                spriteRenderer.color = c;
            }
        }
    }
}