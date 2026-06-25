using UnityEngine;
using UnityEngine.UI;

namespace GaussianSplatting
{
    /// <summary>
    /// 렌더링 FPS를 측정하여 Debug.Log와 선택적 UI Text로 출력.
    /// GaussianSplatRenderer가 있는 씬에 추가할 것.
    /// </summary>
    public class FPSMeter : MonoBehaviour
    {
        [Header("Measurement")]
        [Tooltip("측정 시작 전 건너뛸 프레임 수 (초기 로딩 지연 제외)")]
        [SerializeField] private int warmupFrames = 60;

        [Tooltip("로그 출력 주기 (초)")]
        [SerializeField] private float logInterval = 3f;

        [Header("UI (optional)")]
        [SerializeField] private Text fpsText;

        private int   _warmupCount;
        private int   _frameCount;
        private float _elapsed;
        private float _lastFps;

        private void Update()
        {
            if (_warmupCount < warmupFrames)
            {
                _warmupCount++;
                return;
            }

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

            if (fpsText != null)
                fpsText.text = $"{_lastFps:F0} FPS";
        }
    }
}
