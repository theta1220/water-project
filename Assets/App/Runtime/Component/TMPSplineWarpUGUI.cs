using System;
using UnityEngine;
using UnityEngine.Splines;
using TMPro;

/// <summary>
/// TextMeshProUGUI を Unity Spline に沿わせるコンポーネント（安定版）
/// Canvas のスケール・回転に完全対応
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
public class TMPSplineWarpUGUI : MonoBehaviour
{
    public SplineContainer splineContainer;
    public bool loop = false;
    public float distancePerTextUnit = 0.005f;
    public float splineOffset = 0f;
    public float verticalScale = 1f;
    public bool tiltWithTangent = true;
    [Range(8, 1024)] public int samples = 256;

    TMP_Text _tmp;
    RectTransform _rect;
    float[] _tTable, _lenTable;
    float _totalLength;
    Spline _spline => splineContainer ? splineContainer.Spline : null;

    void OnEnable()
    {
        _tmp = GetComponent<TMP_Text>();
        _rect = _tmp.rectTransform;
        RebuildLUT();
        _tmp.ForceMeshUpdate();
    }

    void OnValidate()
    {
        if (!enabled) return;
        RebuildLUT();
        if (_tmp) _tmp.ForceMeshUpdate();
    }

    void LateUpdate() => Warp();

    void RebuildLUT()
    {
        if (_spline == null) return;
        samples = Mathf.Max(4, samples);
        _tTable = new float[samples];
        _lenTable = new float[samples];
        Vector3 prev = EvalPosWorld(0);
        _tTable[0] = 0;
        _lenTable[0] = 0;
        _totalLength = 0;
        for (int i = 1; i < samples; i++)
        {
            float t = (float)i / (samples - 1);
            _tTable[i] = t;
            Vector3 p = EvalPosWorld(t);
            _totalLength += Vector3.Distance(prev, p);
            _lenTable[i] = _totalLength;
            prev = p;
        }
    }

    void Warp()
    {
        if (_spline == null || _tmp == null) return;
        _tmp.ForceMeshUpdate();

        var info = _tmp.textInfo;
        if (info.characterCount == 0) return;

        Matrix4x4 worldToLocal = _rect.worldToLocalMatrix;
        float firstOrigin = info.characterInfo[0].origin;

        for (int ci = 0; ci < info.characterCount; ci++)
        {
            var ch = info.characterInfo[ci];
            if (!ch.isVisible) continue;
            int vi = ch.vertexIndex;
            var verts = info.meshInfo[ch.materialReferenceIndex].vertices;

            float baseline = ch.baseLine;

            for (int v = 0; v < 4; v++)
            {
                int idx = vi + v;
                Vector3 src = verts[idx];

                float localX = src.x - firstOrigin;
                float s = localX * distancePerTextUnit + splineOffset;
                float L = Mathf.Max(_totalLength, 1e-6f);
                if (loop) s = s % L;
                else s = Mathf.Clamp(s, 0, L - 1e-6f);

                float t = LenToT(s);

                Vector3 posW = EvalPosWorld(t);
                Vector3 tanW = EvalTanWorld(t);
                Vector3 nW = new Vector3(-tanW.y, tanW.x, 0).normalized;

                // スプライン上の位置を RectTransform ローカルへ
                Vector3 posLocal = worldToLocal.MultiplyPoint3x4(posW);
                Vector3 tanLocal = worldToLocal.MultiplyVector(tanW).normalized;
                Vector3 nLocal = new Vector3(-tanLocal.y, tanLocal.x, 0).normalized;

                float yOff = (src.y - baseline) * verticalScale;
                Vector3 dst = posLocal + nLocal * yOff;

                verts[idx] = dst;
            }
        }

        // 頂点を更新
        for (int i = 0; i < info.meshInfo.Length; i++)
        {
            var mi = info.meshInfo[i];
            mi.mesh.vertices = mi.vertices;
            _tmp.UpdateGeometry(mi.mesh, i);
        }
    }

    float LenToT(float s)
    {
        int hi = Array.BinarySearch(_lenTable, s);
        if (hi >= 0) return _tTable[hi];
        hi = ~hi;
        if (hi <= 0) return _tTable[0];
        if (hi >= _lenTable.Length) return _tTable[^1];
        int lo = hi - 1;
        float u = (s - _lenTable[lo]) / (_lenTable[hi] - _lenTable[lo]);
        return Mathf.Lerp(_tTable[lo], _tTable[hi], u);
    }

    Vector3 EvalPosWorld(float t)
    {
        SplineUtility.Evaluate(_spline, t, out var p, out _, out _);
        return splineContainer.transform.TransformPoint(p);
    }

    Vector3 EvalTanWorld(float t)
    {
        SplineUtility.Evaluate(_spline, t, out _, out var tan, out _);
        return splineContainer.transform.TransformVector(tan).normalized;
    }
}
