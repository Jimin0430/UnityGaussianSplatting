using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Profiling;

namespace GaussianSplatting
{
    /// <summary>
    /// FPS / 프레임 시간 / 메모리 / 에셋 로딩 시간을 화면 우측 상단에 표시.
    /// 씬 설정 없이 자동 생성됨.
    /// </summary>
    public class FPSMeter : MonoBehaviour
    {
        [SerializeField] private int   warmupFrames = 60;
        [SerializeField] private float logInterval  = 3f;

        private int   _warmupCount;
        private int   _frameCount;
        private float _elapsed;
        private float _avgFps;
        private float _avgMs;
        private Text  _text;

        // 에셋 로딩 시간 추적 (외부에서 호출)
        private static float s_loadStartTime = -1f;
        private static float s_loadDurationSec = -1f;

        public static void NotifyLoadStart()  => s_loadStartTime = Time.realtimeSinceStartup;
        public static void NotifyLoadEnd()
        {
            if (s_loadStartTime >= 0f)
                s_loadDurationSec = Time.realtimeSinceStartup - s_loadStartTime;
        }

        // ── 자동 생성 ─────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<FPSMeter>() != null) return;
            var go = new GameObject("FPSMeter");
            DontDestroyOnLoad(go);
            go.AddComponent<FPSMeter>();
        }

        private void Awake() => _text = CreateOverlayText();

        // ── 매 프레임 측정 ────────────────────────────────────────────────

        private void Update()
        {
            if (_warmupCount < warmupFrames) { _warmupCount++; return; }

            _frameCount++;
            _elapsed += Time.unscaledDeltaTime;

            if (_elapsed >= logInterval)
            {
                _avgFps = _frameCount / _elapsed;
                _avgMs  = _elapsed / _frameCount * 1000f;
                Debug.Log($"[FPSMeter] avg={_avgFps:F1} fps  frame={_avgMs:F1} ms  " +
                          $"over {_elapsed:F1}s ({_frameCount} frames)");
                _frameCount = 0;
                _elapsed    = 0f;
            }

            if (_text == null) return;

            float instantFps = 1f / Time.unscaledDeltaTime;
            float memMB = Profiler.GetTotalAllocatedMemoryLong() / 1048576f;

            string loadLine = s_loadDurationSec >= 0f
                ? $"\nLoad: {s_loadDurationSec:F1}s"
                : "";

            _text.text =
                $"{Mathf.RoundToInt(instantFps)} FPS  {Time.unscaledDeltaTime * 1000f:F1}ms\n" +
                $"Mem: {memMB:F1} MB" +
                loadLine;
        }

        // ── UI 생성 ───────────────────────────────────────────────────────

        private static Text CreateOverlayText()
        {
            var canvas = new GameObject("FPSMeter_Canvas").AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            DontDestroyOnLoad(canvas.gameObject);

            var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            canvas.gameObject.AddComponent<GraphicRaycaster>();

            var textGo = new GameObject("FPSText");
            textGo.transform.SetParent(canvas.transform, false);

            // 우측 상단에서 20% 아래 위치 (1920 기준 → 384px)
            var rt = textGo.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(1f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-30f, -384f);
            rt.sizeDelta        = new Vector2(360f, 200f);

            var text = textGo.AddComponent<Text>();
            text.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize  = 54;
            text.fontStyle = FontStyle.Bold;
            text.color     = Color.yellow;
            text.alignment = TextAnchor.UpperRight;
            text.text      = "-- FPS";

            var shadow = textGo.AddComponent<Shadow>();
            shadow.effectColor    = Color.black;
            shadow.effectDistance = new Vector2(3f, -3f);

            return text;
        }
    }
}
