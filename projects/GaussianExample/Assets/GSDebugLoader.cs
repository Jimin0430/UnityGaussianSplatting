// ============================================================
// DEBUG ONLY — 테스트 후 이 파일(GSDebugLoader.cs)만 삭제하면 완전 복원
// ============================================================
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using GaussianSplatting.Runtime;

namespace GaussianSplatting
{
    public class GSDebugLoader : MonoBehaviour
    {
        [Header("Debug Settings")]
        [Tooltip("샘플 파일 경로 (상대경로 → StreamingAssets 기준, 절대경로 가능)")]
        [SerializeField] private string samplePath = "sample/splat_example.unitygs";
        [SerializeField] private bool runOnStart = true;

        // ──────────────────────────────────────────────────────────────────

        private void Start()
        {
            if (runOnStart)
                StartCoroutine(DebugLoad());
        }

        // Inspector 우클릭 메뉴에서도 실행 가능
        [ContextMenu("Run Debug Load Now")]
        public void RunNow() => StartCoroutine(DebugLoad());

        // ──────────────────────────────────────────────────────────────────

        private IEnumerator DebugLoad()
        {
            Log("========== GSDebugLoader START ==========");

            // ── Step 1: 파일 경로 탐색 ────────────────────────────────────
            string[] candidates = {
                samplePath,
                Path.Combine(Application.streamingAssetsPath, samplePath),
                Path.Combine(Application.persistentDataPath, samplePath),
                Path.Combine(Application.dataPath, samplePath),
            };

            string resolvedPath = null;
            Log("Step 1: Searching sample file...");
            foreach (var p in candidates)
            {
                if (File.Exists(p))
                {
                    resolvedPath = p;
                    Log($"  ✓ Found: {p}  ({new FileInfo(p).Length:N0} bytes)");
                    break;
                }
                Log($"  ✗ Not found: {p}");
            }

            if (resolvedPath == null)
            {
                LogError("Step 1 FAILED: sample file not found at any candidate path.\n" +
                         $"Place splat_example.unitygs at:\n  {Path.Combine(Application.streamingAssetsPath, samplePath)}");
                yield break;
            }

            // ── Step 2: 파일 헤더 raw check ──────────────────────────────
            Log("Step 2: Checking file header...");
            try
            {
                using var fs = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);
                uint magic = br.ReadUInt32();
                uint ver   = br.ReadUInt32();
                int  n     = (int)br.ReadUInt32();
                Log($"  magic=0x{magic:X8} (expected 0x41534755={magic == 0x41534755})");
                Log($"  version={ver} (expected 1={ver == 1})");
                Log($"  splatCount={n:N0}");

                if (magic != 0x41534755) { LogError("Step 2 FAILED: wrong magic number"); yield break; }
                if (ver != 1)            { LogError($"Step 2 FAILED: unsupported version {ver}"); yield break; }
                if (n <= 0)              { LogError("Step 2 FAILED: splatCount <= 0"); yield break; }
            }
            catch (Exception e) { LogError($"Step 2 FAILED: {e.Message}\n{e.StackTrace}"); yield break; }
            Log("  ✓ Header OK");

            // ── Step 3: UnityGSBinaryLoader 실행 ─────────────────────────
            Log("Step 3: Running UnityGSBinaryLoader.LoadFromBinary...");

            var loaderObj = new GameObject("DEBUG_Loader");
            var loader    = loaderObj.AddComponent<UnityGSBinaryLoader>();

            GaussianSplatRenderer resultRenderer = null;
            bool callbackFired = false;

            yield return StartCoroutine(loader.LoadFromBinary(resolvedPath, r =>
            {
                callbackFired = true;
                resultRenderer = r;
            }));

            Log($"  callback fired: {callbackFired}");
            Log($"  renderer returned: {(resultRenderer != null ? resultRenderer.gameObject.name : "NULL")}");

            if (!callbackFired || resultRenderer == null)
            {
                LogError("Step 3 FAILED: LoadFromBinary returned null renderer. Check earlier logs for parse/build errors.");
                yield break;
            }
            Log("  ✓ Loader returned a renderer");

            // ── Step 4: GaussianSplatAsset 유효성 ────────────────────────
            Log("Step 4: Checking GaussianSplatAsset validity...");
            var asset = resultRenderer.m_Asset;
            if (asset == null)
            {
                LogError("Step 4 FAILED: renderer.m_Asset is null after load");
                yield break;
            }
            Log($"  splatCount:    {asset.splatCount}");
            Log($"  formatVersion: {asset.formatVersion}  (kCurrentVersion={GaussianSplatAsset.kCurrentVersion})  match={asset.formatVersion == GaussianSplatAsset.kCurrentVersion}");
            Log($"  posData:       {(asset.posData != null  ? $"{asset.posData.dataSize} bytes"   : "NULL")}");
            Log($"  otherData:     {(asset.otherData != null? $"{asset.otherData.dataSize} bytes"  : "NULL")}");
            Log($"  colorData:     {(asset.colorData != null? $"{asset.colorData.dataSize} bytes"  : "NULL")}");
            Log($"  shData:        {(asset.shData != null   ? $"{asset.shData.dataSize} bytes"     : "NULL")}");
            Log($"  chunkData:     {(asset.chunkData != null? $"{asset.chunkData.dataSize} bytes"  : "NULL")}");
            Log($"  HasValidAsset: {resultRenderer.HasValidAsset}");

            if (!resultRenderer.HasValidAsset)
            {
                LogError("Step 4 FAILED: HasValidAsset=false. One of the data fields above is likely wrong.");
                yield break;
            }
            Log("  ✓ Asset is valid");

            // ── Step 5: 셰이더/컴퓨트 셰이더 참조 확인 ───────────────────
            Log("Step 5: Checking renderer shader references (must be non-null for rendering)...");
            CheckField(resultRenderer, "m_ShaderSplats");
            CheckField(resultRenderer, "m_ShaderComposite");
            CheckField(resultRenderer, "m_ShaderDebugPoints");
            CheckField(resultRenderer, "m_ShaderDebugBoxes");
            CheckField(resultRenderer, "m_CSSplatUtilities");

            bool resourcesOk = CheckBoolProp(resultRenderer, "resourcesAreSetUp");
            Log($"  resourcesAreSetUp: {resourcesOk}");

            if (!resourcesOk)
            {
                LogError("Step 5 FAILED: Shader references are null.\n" +
                         "AddComponent<GaussianSplatRenderer>() creates a bare component with no shaders.\n" +
                         "Fix: Copy shader refs from an existing scene renderer, or use a prefab instead of AddComponent.");

                // 씬에 이미 있는 렌더러에서 셰이더를 복사해서 계속 시도
                Log("  → Attempting to copy shader refs from existing scene renderer...");
                var existingRenderer = FindFirstRendererInScene(resultRenderer);
                if (existingRenderer != null)
                {
                    CopyShaderRefs(existingRenderer, resultRenderer);
                    bool nowOk = CheckBoolProp(resultRenderer, "resourcesAreSetUp");
                    Log($"  resourcesAreSetUp after copy: {nowOk}");
                    if (nowOk)
                    {
                        // OnEnable을 다시 강제 실행
                        resultRenderer.gameObject.SetActive(false);
                        yield return null;
                        resultRenderer.gameObject.SetActive(true);
                        Log("  ✓ Re-enabled renderer with shader refs");
                    }
                    else
                    {
                        LogError("  Still no resourcesAreSetUp after copy. Check prefab setup in scene.");
                        yield break;
                    }
                }
                else
                {
                    LogError("  No existing GaussianSplatRenderer found in scene to copy shaders from.\n" +
                             "Add a GaussianSplatRenderer prefab to the scene with shaders assigned.");
                    yield break;
                }
            }
            else
            {
                Log("  ✓ Shader references OK");
            }

            // ── Step 5b: 내부 상태 추가 확인 ─────────────────────────────
            Log("Step 5b: Checking internal renderer state...");
            CheckField(resultRenderer, "m_MatSplats");
            CheckField(resultRenderer, "m_MatComposite");
            var regField = typeof(GaussianSplatRenderer)
                .GetField("m_Registered", BindingFlags.NonPublic | BindingFlags.Instance);
            bool isRegistered = regField != null && (bool)regField.GetValue(resultRenderer);
            Log($"  m_Registered: {isRegistered}");

            // ── Step 5c: 기존 씬 렌더러에 asset 직접 스왑 시도 ───────────
            Log("Step 5c: Trying asset swap on existing scene renderer...");
            var swapTarget = FindFirstRendererInScene(resultRenderer);
            if (swapTarget != null)
            {
                var prevAsset = swapTarget.m_Asset;
                Log($"  Existing renderer '{swapTarget.gameObject.name}' prev asset: {(prevAsset != null ? prevAsset.name : "null")}");
                swapTarget.m_Asset = resultRenderer.m_Asset;
                swapTarget.gameObject.SetActive(false);
                yield return null;
                swapTarget.gameObject.SetActive(true);
                Log($"  ✓ Swapped asset onto existing renderer '{swapTarget.gameObject.name}'");
                Log($"  HasValidAsset after swap: {swapTarget.HasValidAsset}");
                Log($"  HasValidRenderSetup after swap: {swapTarget.HasValidRenderSetup}");
                CheckField(swapTarget, "m_Registered");

                // 동적으로 생성한 임시 렌더러를 제거해서 이중 렌더링 방지
                Destroy(resultRenderer.gameObject);
                resultRenderer = swapTarget; // 이후 체크는 swapTarget 기준
            }
            else
            {
                Log("  No existing renderer to swap — using dynamically created one");
            }

            // ── Step 6: GPU 버퍼 유효성 ──────────────────────────────────
            Log("Step 6: Checking GPU render setup...");
            yield return null; // OnEnable 이후 한 프레임 대기
            Log($"  HasValidRenderSetup: {resultRenderer.HasValidRenderSetup}");
            CheckField(resultRenderer, "m_MatSplats");
            CheckField(resultRenderer, "m_MatComposite");

            if (!resultRenderer.HasValidRenderSetup)
            {
                LogError("Step 6 FAILED: GPU buffers not created. CreateResourcesForAsset may have failed.");
                yield break;
            }
            Log("  ✓ GPU buffers ready");

            // ── Step 7: 카메라 vs Splat 위치 확인 ────────────────────────
            Log("Step 7: Checking camera vs splat position...");
            var cam = Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>();

            if (cam != null)
            {
                Log($"  Camera position:  {cam.transform.position}");
                Log($"  Camera forward:   {cam.transform.forward}");
                Log($"  Splat GameObject: {resultRenderer.transform.position}");
                Log($"  Splat bounds min: {asset.boundsMin}");
                Log($"  Splat bounds max: {asset.boundsMax}");
                var center = (asset.boundsMin + asset.boundsMax) * 0.5f;
                Log($"  Splat center:     {center}");
                float dist = Vector3.Distance(cam.transform.position, center);
                Log($"  Camera→Splat center distance: {dist:F2}");
                if (dist > 100f)
                    LogWarn("  Camera seems very far from splat center. Consider moving camera or splat.");
            }
            else
            {
                LogWarn("  No camera found in scene.");
            }

            Log("========== GSDebugLoader DONE — rendering should be visible ==========");
        }

        // ── 헬퍼 ──────────────────────────────────────────────────────────

        private static GaussianSplatRenderer FindFirstRendererInScene(GaussianSplatRenderer exclude)
        {
            foreach (var r in FindObjectsByType<GaussianSplatRenderer>(FindObjectsSortMode.None))
                if (r != exclude) return r;
            return null;
        }

        private static void CopyShaderRefs(GaussianSplatRenderer src, GaussianSplatRenderer dst)
        {
            string[] fields = { "m_ShaderSplats", "m_ShaderComposite", "m_ShaderDebugPoints",
                                "m_ShaderDebugBoxes", "m_CSSplatUtilities" };
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var name in fields)
            {
                var f = typeof(GaussianSplatRenderer).GetField(name, flags);
                if (f == null) continue;
                var val = f.GetValue(src);
                f.SetValue(dst, val);
                Log($"  Copied {name}: {(val != null ? val.GetType().Name : "null")}");
            }
        }

        private static void CheckField(object obj, string name)
        {
            var f = obj.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var val = f?.GetValue(obj);
            string status = val != null ? $"✓ {val.GetType().Name}" : "✗ NULL";
            Log($"  {name}: {status}");
        }

        private static bool CheckBoolProp(object obj, string propName)
        {
            var prop = obj.GetType().GetProperty(propName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
                return (bool)prop.GetValue(obj);
            // fallback: try as field
            var field = obj.GetType().GetField(propName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null && (bool)field.GetValue(obj);
        }

        private static void Log(string msg)      => Debug.Log($"[GSDebug] {msg}");
        private static void LogError(string msg) => Debug.LogError($"[GSDebug] {msg}");
        private static void LogWarn(string msg)  => Debug.LogWarning($"[GSDebug] {msg}");
    }
}
