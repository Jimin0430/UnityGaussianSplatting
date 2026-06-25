using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using GaussianSplatting.Runtime;

namespace GaussianSplatting
{
    /// <summary>
    /// CGXR 서버에 영상을 업로드하고 진행 상황을 추적한 뒤, 완료되면 Unity 에셋을 다운로드합니다.
    /// </summary>
    public class VideoUploadManager : MonoBehaviour
    {
        [Header("Server Configuration")]
        [SerializeField] private string serverUrl = "http://100.86.251.105:8000";
        [SerializeField] private string apiKey = "changeme";

        [Header("UI References")]
        [SerializeField] private Button uploadButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Slider progressBar;
        [SerializeField] private Text logText;

        [Header("Settings")]
        [SerializeField] private bool useSAM2 = true;
        [SerializeField] private float pollingInterval = 2f; // 진행 상황 확인 주기 (초)

        private string currentJobId;
        private Coroutine statusPollingCoroutine;

        private void Start()
        {
            if (uploadButton != null)
            {
                uploadButton.onClick.AddListener(OnUploadButtonClicked);
            }

            ResetUI();
        }

        private void OnDestroy()
        {
            if (uploadButton != null)
            {
                uploadButton.onClick.RemoveListener(OnUploadButtonClicked);
            }

            StopStatusPolling();
        }

        private void OnUploadButtonClicked()
        {
            // 모바일 플랫폼에서는 네이티브 갤러리 사용
            #if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            // 권한 확인
            NativeGallery.Permission permission = NativeGallery.CheckPermission();

            if (permission == NativeGallery.Permission.Denied)
            {
                UpdateStatus("Photo library access denied. Please enable in Settings.", Color.red);
                return;
            }
            else if (permission == NativeGallery.Permission.ShouldAsk)
            {
                NativeGallery.Permission result = NativeGallery.RequestPermission();
                if (result == NativeGallery.Permission.Denied)
                {
                    UpdateStatus("Photo library access denied.", Color.red);
                    return;
                }
            }

            UpdateStatus("Opening photo library...", Color.cyan);

            NativeGallery.PickVideo((string videoPath) =>
            {
                Debug.Log($"[VideoUploadManager] PickVideo callback: '{videoPath}', exists={File.Exists(videoPath ?? "")}");
                if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
                {
                    StartCoroutine(UploadVideoCoroutine(videoPath));
                }
                else
                {
                    UpdateStatus("No video selected.", Color.yellow);
                }
            });
            #else
            // 에디터 및 데스크톱에서는 파일 다이얼로그 사용
            string videoPath = OpenFileDialog();

            if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
            {
                StartCoroutine(UploadVideoCoroutine(videoPath));
            }
            else
            {
                UpdateStatus("No video file selected.", Color.yellow);
            }
            #endif
        }

        /// <summary>
        /// 크로스플랫폼 파일 선택
        /// </summary>
        private string OpenFileDialog()
        {
            #if UNITY_EDITOR
            return UnityEditor.EditorUtility.OpenFilePanel("Select Video File", "", "mp4,mov,avi,mkv");
            #elif UNITY_STANDALONE_WIN
            return OpenFileDialogWindows();
            #elif UNITY_STANDALONE_OSX
            return OpenFileDialogMac();
            #elif UNITY_STANDALONE_LINUX
            return OpenFileDialogLinux();
            #else
            Debug.LogError("File dialog not implemented for this platform. Please use editor or implement platform-specific dialog.");
            return "";
            #endif
        }

        #if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("Comdlg32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool GetOpenFileName([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] OpenFileName ofn);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private class OpenFileName
        {
            public int structSize = 0;
            public System.IntPtr dlgOwner = System.IntPtr.Zero;
            public System.IntPtr instance = System.IntPtr.Zero;
            public string filter = null;
            public string customFilter = null;
            public int maxCustFilter = 0;
            public int filterIndex = 0;
            public string file = null;
            public int maxFile = 0;
            public string fileTitle = null;
            public int maxFileTitle = 0;
            public string initialDir = null;
            public string title = null;
            public int flags = 0;
            public short fileOffset = 0;
            public short fileExtension = 0;
            public string defExt = null;
            public System.IntPtr custData = System.IntPtr.Zero;
            public System.IntPtr hook = System.IntPtr.Zero;
            public string templateName = null;
            public System.IntPtr reservedPtr = System.IntPtr.Zero;
            public int reservedInt = 0;
            public int flagsEx = 0;
        }

        private string OpenFileDialogWindows()
        {
            OpenFileName ofn = new OpenFileName();
            ofn.structSize = System.Runtime.InteropServices.Marshal.SizeOf(ofn);
            ofn.filter = "Video Files\0*.mp4;*.mov;*.avi;*.mkv\0All Files\0*.*\0\0";
            ofn.file = new string(new char[256]);
            ofn.maxFile = ofn.file.Length;
            ofn.fileTitle = new string(new char[64]);
            ofn.maxFileTitle = ofn.fileTitle.Length;
            ofn.initialDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyVideos);
            ofn.title = "Select Video File";
            ofn.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008;

            if (GetOpenFileName(ofn))
            {
                return ofn.file;
            }
            return "";
        }
        #endif

        #if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        private string OpenFileDialogMac()
        {
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "osascript";
            process.StartInfo.Arguments = "-e 'POSIX path of (choose file of type {\"public.movie\"} with prompt \"Select Video File\")'";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;

            try
            {
                process.Start();
                string path = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return path;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to open file dialog: {e.Message}");
                return "";
            }
        }
        #endif

        #if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
        private string OpenFileDialogLinux()
        {
            // zenity 사용 (대부분의 Linux 배포판에 포함)
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "zenity";
            process.StartInfo.Arguments = "--file-selection --title=\"Select Video File\" --file-filter=\"Video Files (mp4,mov,avi,mkv) | *.mp4 *.mov *.avi *.mkv\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;

            try
            {
                process.Start();
                string path = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return path;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to open file dialog. Make sure zenity is installed: {e.Message}");
                return "";
            }
        }
        #endif

        private IEnumerator UploadVideoCoroutine(string videoPath)
        {
            UpdateStatus("Uploading video...", Color.cyan);
            SetProgress(0f);

            byte[] videoData;
            try
            {
                videoData = File.ReadAllBytes(videoPath);
            }
            catch (Exception e)
            {
                UpdateStatus($"Failed to read video: {e.Message}", Color.red);
                yield break;
            }

            string fileName = Path.GetFileName(videoPath);

            WWWForm form = new WWWForm();
            form.AddBinaryData("file", videoData, fileName, "video/mp4");
            form.AddField("use_sam2", useSAM2 ? "true" : "false");

            UpdateStatus($"Uploading {(videoData.Length / 1024f / 1024f):F1} MB...", Color.cyan);

            using (UnityWebRequest request = UnityWebRequest.Post($"{serverUrl}/upload", form))
            {
                request.SetRequestHeader("X-API-Key", apiKey);

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    float pct = request.uploadProgress * 100f;
                    SetProgress(request.uploadProgress * 0.5f);
                    UpdateStatus($"Uploading... {pct:F0}%", Color.cyan);
                    yield return null;
                }

                // 업로드 후 임시 캐시 파일 삭제
                try { File.Delete(videoPath); } catch { }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    UploadResponse response = JsonUtility.FromJson<UploadResponse>(request.downloadHandler.text);
                    currentJobId = response.job_id;

                    UpdateStatus($"Upload complete. Processing...", Color.green);
                    AppendLog($"Job {currentJobId} started.");

                    statusPollingCoroutine = StartCoroutine(PollStatusCoroutine());
                }
                else
                {
                    UpdateStatus($"Upload failed: {request.error}", Color.red);
                    AppendLog(request.downloadHandler.text);
                }
            }
        }

        private IEnumerator PollStatusCoroutine()
        {
            bool isDone = false;

            while (!isDone)
            {
                yield return new WaitForSeconds(pollingInterval);

                using (UnityWebRequest request = UnityWebRequest.Get($"{serverUrl}/status/{currentJobId}"))
                {
                    request.SetRequestHeader("X-API-Key", apiKey);

                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        StatusResponse status = JsonUtility.FromJson<StatusResponse>(request.downloadHandler.text);

                        UpdateStatus($"Status: {status.status}", GetColorForStatus(status.status));
                        SetProgress(GetProgressForStatus(status.status));

                        // 최근 로그 표시
                        if (status.log_tail != null && status.log_tail.Length > 0)
                        {
                            string lastLog = status.log_tail[status.log_tail.Length - 1];
                            AppendLog(lastLog);
                        }

                        // 완료 또는 실패 시 종료
                        if (status.status == "done")
                        {
                            isDone = true;
                            StartCoroutine(DownloadUnityAssetCoroutine());
                        }
                        else if (status.status == "failed")
                        {
                            isDone = true;
                            UpdateStatus($"Training failed: {status.error}", Color.red);
                        }
                    }
                    else
                    {
                        UpdateStatus($"Status check failed: {request.error}", Color.red);
                    }
                }
            }
        }

        private IEnumerator DownloadUnityAssetCoroutine()
        {
            UpdateStatus("Downloading Unity asset...", Color.cyan);

            // Unity 바이너리 형식 요청
            using (UnityWebRequest request = UnityWebRequest.Get($"{serverUrl}/download/{currentJobId}?format=unity"))
            {
                request.SetRequestHeader("X-API-Key", apiKey);

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    // 저장 경로 설정 (런타임에서도 작동)
                    string saveDir = Path.Combine(Application.persistentDataPath, "GaussianAssets");
                    if (!Directory.Exists(saveDir))
                    {
                        Directory.CreateDirectory(saveDir);
                    }

                    // Unity 바이너리 형식으로 저장 (.unitygs)
                    string savePath = Path.Combine(saveDir, $"{currentJobId}.unitygs");
                    File.WriteAllBytes(savePath, request.downloadHandler.data);

                    UpdateStatus($"Download complete: {savePath}", Color.green);
                    AppendLog($"Asset saved to: {savePath}");
                    AppendLog($"File size: {FormatBytes(request.downloadHandler.data.Length)}");
                    SetProgress(0.95f);

                    // 에디터에서는 AssetDatabase 새로고침
                    #if UNITY_EDITOR
                    string editorPath = Path.Combine(Application.dataPath, "GaussianAssets", $"{currentJobId}.unitygs");
                    string editorDir = Path.GetDirectoryName(editorPath);
                    if (!Directory.Exists(editorDir))
                    {
                        Directory.CreateDirectory(editorDir);
                    }
                    File.Copy(savePath, editorPath, true);
                    UnityEditor.AssetDatabase.Refresh();
                    AppendLog($"Editor copy: {editorPath}");
                    #endif

                    // 런타임에서 Unity 바이너리 로드 및 렌더링
                    UnityGSBinaryLoader loader = FindObjectOfType<UnityGSBinaryLoader>();
                    if (loader == null)
                    {
                        GameObject loaderObj = new GameObject("UnityGSBinaryLoader");
                        loader = loaderObj.AddComponent<UnityGSBinaryLoader>();
                    }

                    AppendLog("Loading Unity Gaussian Splat binary into scene...");
                    bool loadSuccess = false;
                    GaussianSplatRenderer loadedRenderer = null;
                    yield return StartCoroutine(loader.LoadFromBinary(savePath, renderer =>
                    {
                        if (renderer != null)
                        {
                            loadedRenderer = renderer;
                            loadSuccess = true;
                            AppendLog($"✓ Gaussian Splat renderer created: {renderer.gameObject.name}");
                        }
                    }));

                    // 씬의 기존 렌더러에 asset 스왑 후 임시 렌더러 제거 (이중 렌더링 방지)
                    if (loadSuccess && loadedRenderer != null)
                    {
                        GaussianSplatRenderer swapTarget = null;
                        foreach (var r in FindObjectsByType<GaussianSplatRenderer>(FindObjectsSortMode.None))
                        {
                            if (r != loadedRenderer) { swapTarget = r; break; }
                        }

                        if (swapTarget != null)
                        {
                            swapTarget.m_Asset = loadedRenderer.m_Asset;
                            swapTarget.gameObject.SetActive(false);
                            yield return null;
                            swapTarget.gameObject.SetActive(true);
                            Destroy(loadedRenderer.gameObject);
                            AppendLog($"✓ Asset swapped onto existing renderer '{swapTarget.gameObject.name}'");
                        }
                    }

                    if (loadSuccess)
                    {
                        UpdateStatus("✓ Complete! Gaussian Splat is now rendered.", Color.green);
                        ShowCompleteState();
                    }
                    else
                    {
                        UpdateStatus("⚠ Downloaded but failed to render", Color.yellow);
                        AppendLog("Binary file saved, but rendering failed. Check console for errors.");
                    }
                }
                else
                {
                    UpdateStatus($"Download failed: {request.error}", Color.red);
                }
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void StopStatusPolling()
        {
            if (statusPollingCoroutine != null)
            {
                StopCoroutine(statusPollingCoroutine);
                statusPollingCoroutine = null;
            }
        }

        private float GetProgressForStatus(string status)
        {
            switch (status)
            {
                case "queued":      return 0.05f;
                case "extracting":  return 0.15f;
                case "colmap":      return 0.25f;
                case "sam2":        return 0.35f;
                case "pretrain":    return 0.50f;
                case "finetune":    return 0.65f;
                case "pruning":     return 0.75f;
                case "distilling":  return 0.85f;
                case "quantizing":  return 0.95f;
                case "done":        return 1.0f;
                case "failed":      return 0f;
                default:            return 0f;
            }
        }

        private Color GetColorForStatus(string status)
        {
            switch (status)
            {
                case "done":   return Color.green;
                case "failed": return Color.red;
                default:       return Color.yellow;
            }
        }

        private void UpdateStatus(string message, Color color)
        {
            if (statusText != null)
            {
                statusText.text = message;
                statusText.color = color;
            }

            Debug.Log($"[VideoUploadManager] {message}");
        }

        private void AppendLog(string message)
        {
            if (logText != null)
            {
                logText.text += $"\n{message}";

                // 로그가 너무 길어지면 자르기
                if (logText.text.Length > 2000)
                {
                    logText.text = logText.text.Substring(logText.text.Length - 2000);
                }
            }
        }

        private void SetProgress(float progress)
        {
            if (progressBar != null)
            {
                progressBar.value = progress;
            }
        }

        private void ShowCompleteState()
        {
            if (uploadButton != null)
                uploadButton.gameObject.SetActive(false);
            if (progressBar != null)
                progressBar.gameObject.SetActive(false);
            if (logText != null)
                logText.transform.parent.gameObject.SetActive(false);

            // statusText를 화면 상단 30% 위치로 이동
            if (statusText != null)
            {
                RectTransform rt = statusText.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.7f);
                rt.anchorMax = new Vector2(0.5f, 0.7f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(700, 40);
                statusText.fontSize = 22;
            }
        }

        private void ResetUI()
        {
            UpdateStatus("Ready to upload", Color.white);
            SetProgress(0f);

            if (logText != null)
            {
                logText.text = "";
            }
        }

        // JSON 응답 모델
        [Serializable]
        private class UploadResponse
        {
            public string job_id;
            public string status;
            public string message;
        }

        [Serializable]
        private class StatusResponse
        {
            public string job_id;
            public string status;
            public bool use_sam2;
            public string error;
            public string created_at;
            public string updated_at;
            public string[] log_tail;
        }
    }
}
