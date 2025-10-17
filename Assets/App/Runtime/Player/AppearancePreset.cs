using UnityEngine;

namespace App.Runtime.Player
{
    [CreateAssetMenu(menuName = "Evo/Appearance Preset")]
    public class AppearancePreset : ScriptableObject
    {
        [Header("Morph Binding")] public string morphId = "base"; // この見た目はどの形態に適用するか

        [Header("Color/Emission")] public Gradient colorBySize; // Genome.size を 0..3 → 色へ

        public AnimationCurve
            emissionByEmission = AnimationCurve.Linear(0, 0.3f, 1, 1.2f); // Genome.emission -> 材質Emission

        public AnimationCurve hueOffsetBySpeed = AnimationCurve.Linear(0, 0, 3, 0.1f); // 速いほど色相が少し回る

        [Header("Scale & Roundness")]
        public AnimationCurve bodyScaleBySize = AnimationCurve.Linear(0.2f, 0.6f, 3f, 1.6f); // 見た目倍率

        public AnimationCurve roundnessByViscosity = AnimationCurve.Linear(0.5f, 1f, 3f, 0.4f); // 丸さ(0..1)

        [Header("Parts (optional)")] public GameObject spikePrefab; // スパイク/棘
        public int spikesMin = 0;
        public int spikesMax = 12;
        public AnimationCurve spikesBySize = AnimationCurve.Linear(0.2f, 0, 3f, 12);

        public GameObject tentaclePrefab; // 触手（スプライン等）
        public int tentacleMin = 0;
        public int tentacleMax = 6;
        public AnimationCurve tentaclesBySpeed = AnimationCurve.Linear(0.5f, 0, 3f, 6);

        [Header("Material Override (optional)")]
        public Material overrideMaterial; // 形態固有マテリアル（なければ既存を使用）

        [Header("Transitions")] public float blendSeconds = 0.35f; // 補間時間
    }
}