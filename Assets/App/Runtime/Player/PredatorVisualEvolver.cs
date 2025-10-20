using System.Collections;
using System.Collections.Generic;
using App.Runtime.Framework;
using UnityEngine;

namespace App.Runtime.Player
{
    [RequireComponent(typeof(PredatorAgent))]
    public class PredatorVisualEvolver : MonoBehaviour
    {
        [Header("Bindings")] public MetaballBody body; // 色/発光/スケールを反映
        public Renderer materialTarget; // マテリアルにEmission等を設定（無ければ body.targetRenderer を使用）
        public AppearancePreset[] presets; // 形態ごとの見た目プリセット
        public Transform partsRoot; // 追加パーツの親

        private PredatorAgent agent;
        private PredatorMorphController morph;
        private AppearancePreset activePreset;
        private Coroutine blendCo;

        // パーツ管理
        private readonly List<GameObject> spikes = new();
        private readonly List<GameObject> tentacles = new();

        private void Awake()
        {
            agent = GetComponent<PredatorAgent>();
            if (!body) body = GetComponentInChildren<MetaballBody>();
            morph = GetComponent<PredatorMorphController>();
            if (!materialTarget && body) materialTarget = body.targetRenderer;
        }

        private void Start()
        {
            // 初期適用
            ApplyVisualImmediate();
        }

        private void OnEnable()
        {
            // MorphController のイベントをつなぐ（あれば）
            if (morph != null) morph.OnMorphed += OnMorphed;
        }

        private void OnDisable()
        {
            if (morph != null) morph.OnMorphed -= OnMorphed;
        }

        private void OnMorphed(PredatorMorphProfile prev, PredatorMorphProfile next)
        {
            // 形態が変わったら即プリセット切替＆補間
            ApplyVisualSmooth();
        }

        // Genome が変化した際にも滑らかに追随したい場合は FixedUpdate で追従
        private void FixedUpdate()
        {
            // 消化中は値が刻々と変わるので、軽量に補間更新（マテリアル/色だけ）
            if (activePreset != null && blendCo == null)
                ApplyContinuousBlendTick();
        }

        // === 主要API ===
        /// <summary>
        /// 現在の形態に対応するビジュアルプリセットを即座に適用します。
        /// </summary>
        public void ApplyVisualImmediate()
        {
            activePreset = PickPreset();
            if (activePreset == null) return;
            ApplyPresetInstant(activePreset);
            RebuildParts(activePreset, agent.genome);
        }

        /// <summary>
        /// 現在の形態に対応するビジュアルプリセットへ滑らかに移行します。
        /// </summary>
        public void ApplyVisualSmooth()
        {
            var preset = PickPreset();
            if (preset == null) return;

            // パーツはプリセット変わるごとに再構築
            RebuildParts(preset, agent.genome);

            // 材質/色は補間
            if (blendCo != null) StopCoroutine(blendCo);
            blendCo = StartCoroutine(BlendToPreset(preset, agent.genome, preset.blendSeconds));
        }

        // === 内部 ===
        private AppearancePreset PickPreset()
        {
            var id = morph ? morph.currentMorphId : "base";
            if (presets == null || presets.Length == 0) return null;
            // morphId が一致するものを優先。無ければ最初を返す。
            foreach (var p in presets)
                if (p && p.morphId == id)
                    return p;
            return presets[0];
        }

        private void ApplyPresetInstant(AppearancePreset p)
        {
            var g = agent.genome;
            var tSize = Mathf.Clamp(g.size, 0.2f, 3f);
            var tSpeed = Mathf.Clamp(g.speed, 0.1f, 3f);
            var tVisc = Mathf.Clamp(g.viscosity, 0.5f, 3f);
            var tEmis = Mathf.Clamp01(g.emission);

            // スケール
            var bodyScaleMul = p.bodyScaleBySize.Evaluate(tSize);

            // 色/発光
            var baseCol = p.colorBySize.Evaluate(Mathf.InverseLerp(0.2f, 3f, tSize));
            var hueOffset = p.hueOffsetBySpeed.Evaluate(tSpeed);
            Color.RGBToHSV(baseCol, out var h, out var s, out var v);
            h = Mathf.Repeat(h + hueOffset, 1f);
            var finalCol = Color.HSVToRGB(h, s, v);

            var emission = p.emissionByEmission.Evaluate(tEmis);

            if (body)
            {
                body.ApplyGenome(g); // 既存の見た目反映（hue/emissionを利用している場合）
            }

            var target = materialTarget ? materialTarget : (body ? body.targetRenderer : null);
            if (target && target.material)
            {
                var mat = target.material;
                if (p.overrideMaterial) target.material = Instantiate(p.overrideMaterial);

                // 汎用プロパティ（存在すれば設定）
                TrySetFloat(mat, "_Emission", emission);
                TrySetFloat(mat, "_Roundness", p.roundnessByViscosity.Evaluate(tVisc));
                TrySetColor(mat, "_BaseColor", finalCol);
                TrySetColor(mat, "_Color", finalCol);
                TrySetColor(mat, "_EmissionColor", finalCol * emission);
            }

            activePreset = p;
        }

        private IEnumerator BlendToPreset(AppearancePreset p, Genome g, float seconds)
        {
            var target = materialTarget ? materialTarget : (body ? body.targetRenderer : null);
            var mat = target ? target.material : null;

            // 初期値
            var c0 = Color.white;
            var e0 = Color.black;
            var r0 = 1f;
            if (mat)
            {
                c0 = GetColor(mat, "_BaseColor", GetColor(mat, "_Color", c0));
                e0 = GetColor(mat, "_EmissionColor", c0);
                r0 = GetFloat(mat, "_Roundness", 1f);
            }

            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, seconds);

                var gg = agent.genome; // 途中で変わるかもしれないので毎フレーム参照
                var sizeN = Mathf.InverseLerp(0.2f, 3f, Mathf.Clamp(gg.size, 0.2f, 3f));
                var speed = Mathf.Clamp(gg.speed, 0.1f, 3f);
                var visc = Mathf.Clamp(gg.viscosity, 0.5f, 3f);
                var emisN = Mathf.Clamp01(gg.emission);

                var baseCol = p.colorBySize.Evaluate(sizeN);
                var hueOffset = p.hueOffsetBySpeed.Evaluate(speed);
                Color.RGBToHSV(baseCol, out var h, out var s, out var v);
                h = Mathf.Repeat(h + hueOffset, 1f);
                var finalCol = Color.HSVToRGB(h, s, v);
                var finalEmi = p.emissionByEmission.Evaluate(emisN);
                var finalEmiCol = finalCol * finalEmi;

                var round = p.roundnessByViscosity.Evaluate(visc);

                if (mat)
                {
                    TrySetColor(mat, "_BaseColor", Color.Lerp(c0, finalCol, t));
                    TrySetColor(mat, "_Color", Color.Lerp(c0, finalCol, t));
                    TrySetColor(mat, "_EmissionColor", Color.Lerp(e0, finalEmiCol, t));
                    TrySetFloat(mat, "_Emission", Mathf.Lerp(GetFloat(mat, "_Emission", 1f), finalEmi, t));
                    TrySetFloat(mat, "_Roundness", Mathf.Lerp(r0, round, t));
                }

                // ボディスケールもなめらかに
                var bodyScaleMul = p.bodyScaleBySize.Evaluate(Mathf.Lerp(gg.size, gg.size, t));
                if (body)
                    body.transform.localScale = Vector3.Lerp(body.transform.localScale, Vector3.one * bodyScaleMul, t);

                yield return null;
            }

            activePreset = p;
            blendCo = null;
        }

        private void ApplyContinuousBlendTick()
        {
            // 連続変化時の軽量追従（Emission/Colorだけ微更新）
            var p = activePreset;
            if (p == null) return;
            var target = materialTarget ? materialTarget : (body ? body.targetRenderer : null);
            var mat = target ? target.material : null;
            if (!mat) return;

            var g = agent.genome;
            var sizeN = Mathf.InverseLerp(0.2f, 3f, Mathf.Clamp(g.size, 0.2f, 3f));
            var speed = Mathf.Clamp(g.speed, 0.1f, 3f);
            var visc = Mathf.Clamp(g.viscosity, 0.5f, 3f);
            var emisN = Mathf.Clamp01(g.emission);

            var baseCol = p.colorBySize.Evaluate(sizeN);
            var hueOffset = p.hueOffsetBySpeed.Evaluate(speed);
            Color.RGBToHSV(baseCol, out var h, out var s, out var v);
            h = Mathf.Repeat(h + hueOffset, 1f);
            var finalCol = Color.HSVToRGB(h, s, v);
            var finalEmi = p.emissionByEmission.Evaluate(emisN);
            var finalEmiCol = finalCol * finalEmi;

            // 小さく追随（0.15のイージング）
            TrySetColor(mat, "_BaseColor", Color.Lerp(GetColor(mat, "_BaseColor", finalCol), finalCol, 0.15f));
            TrySetColor(mat, "_Color", Color.Lerp(GetColor(mat, "_Color", finalCol), finalCol, 0.15f));
            TrySetColor(mat, "_EmissionColor",
                Color.Lerp(GetColor(mat, "_EmissionColor", finalEmiCol), finalEmiCol, 0.15f));
            TrySetFloat(mat, "_Emission", Mathf.Lerp(GetFloat(mat, "_Emission", finalEmi), finalEmi, 0.15f));
            TrySetFloat(mat, "_Roundness",
                Mathf.Lerp(GetFloat(mat, "_Roundness", 1f), p.roundnessByViscosity.Evaluate(visc), 0.1f));
        }

        private void RebuildParts(AppearancePreset p, Genome g)
        {
            if (!partsRoot) partsRoot = this.transform;

            // 既存パーツ破棄
            foreach (var go in spikes)
                if (go)
                    Destroy(go);
            spikes.Clear();
            foreach (var go in tentacles)
                if (go)
                    Destroy(go);
            tentacles.Clear();

            // スパイク
            if (p.spikePrefab)
            {
                var n = Mathf.Clamp(Mathf.RoundToInt(p.spikesBySize.Evaluate(Mathf.Clamp(g.size, 0.2f, 3f))),
                    p.spikesMin, p.spikesMax);
                for (var i = 0; i < n; i++)
                {
                    var ang = (360f / Mathf.Max(1, n)) * i;
                    var go = Instantiate(p.spikePrefab, partsRoot);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.Euler(0, 0, ang);
                    go.transform.localScale = Vector3.one;
                    spikes.Add(go);
                }
            }

            // 触手
            if (p.tentaclePrefab)
            {
                var n = Mathf.Clamp(Mathf.RoundToInt(p.tentaclesBySpeed.Evaluate(Mathf.Clamp(g.speed, 0.1f, 3f))),
                    p.tentacleMin, p.tentacleMax);
                for (var i = 0; i < n; i++)
                {
                    var ang = Random.Range(0f, 360f);
                    var go = Instantiate(p.tentaclePrefab, partsRoot);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.Euler(0, 0, ang);
                    go.transform.localScale = Vector3.one;
                    tentacles.Add(go);
                }
            }
        }

        // --- マテリアル安全Set ---
        private static bool TrySetFloat(Material m, string name, float v)
        {
            if (!m.HasProperty(name)) return false;
            m.SetFloat(name, v);
            return true;
        }

        private static bool TrySetColor(Material m, string name, Color v)
        {
            if (!m.HasProperty(name)) return false;
            m.SetColor(name, v);
            return true;
        }

        private static Color GetColor(Material m, string name, Color def)
        {
            return m.HasProperty(name) ? m.GetColor(name) : def;
        }

        private static float GetFloat(Material m, string name, float def)
        {
            return m.HasProperty(name) ? m.GetFloat(name) : def;
        }
    }
}