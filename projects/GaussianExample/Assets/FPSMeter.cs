using UnityEngine;
using UnityEngine.UI;

namespace GaussianSplatting
{
    /// <summary>
    /// 렌더링 FPS를 측정하여 화면 우측 상단에 표시.
    /// 씬에 없으면 자동 생성됨 (RuntimeInitializeOnLoadMethod).
    /// </summary>
    public class FPSMeter : MonoBehaviour
    {
        [Header("Measurement")]
        [SerializeField] private int warmupFrames = 60;
        [SerializeField] private float logInterval = 3f;

        private int   _warmupCount;
        private int   _frameCount;
        private float _elapsed;
        private float _lastFps;
        private Text  _fpsText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<FPSMeter>() != null) return;

            var go = new GameObject("FPSMeter");
            DontDestroyOnLoad(go);
            go.AddComponent<FPSMeter>();
        }

        private void Awake()
        {
            _fpsText = CreateOverlayText();
        }

        private void Update()
        {
            if (_warmupCount < warmupFrames) { _warmupCount++; return; }

            _frameCount++;
            _elapsed += Time.unscaledDeltaTime;

            if (_elapsed >= logInterval)
            {
                _lastFps = _frameCount / _elapsed;
                float frameMs = _elapsed / _frameCount * 1000f;
                Debug.Log($"[FPSMeter] avg={_lastFps:F1} fps  frame={frameMs:F1} ms  " +
                          $"over {_elapsed:F1}s ({_frameCount} frames)");
                _frameCount = 0;
                _elapsed    = 0f;
            }

            if (_fpsText != null)
                _fpsText.text = $"{Mathf.RoundToInt(1f / Time.unscaledDeltaTime)} FPS";
        }

        private static Text CreateOverlayText()
        {
            var canvas = new GameObject("FPSMeter_Canvas").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            DontDestroyOnLoad(canvas.gameObject);

            var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            canvas.gameObject.AddComponent<GraphicRaycaster>();

            var textGo = new GameObject("FPSText");
            textGo.transform.SetParent(canvas.transform, false);

            var rt = textGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-20f, -20f);
            rt.sizeDelta = new Vector2(200f, 60f);

            var text = textGo.AddComponent<Text>();
            text.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize  = 36;
            text.fontStyle = FontStyle.Bold;
            text.color     = Color.yellow;
            text.alignment = TextAnchor.UpperRight;
            text.text      = "-- FPS";

            var shadow = textGo.AddComponent<Shadow>();
            shadow.effectColor    = Color.black;
            shadow.effectDistance = new Vector2(2f, -2f);

            return text;
        }
    }
}
