using System;
using System.Collections;
using System.IO;
using UnityEngine;
using GaussianSplatting.Runtime;

namespace GaussianSplatting
{
    /// <summary>
    /// .unitygs 바이너리 파일 로더.
    /// 백그라운드 스레드에서 raw float 데이터만 읽고,
    /// GaussianSplatAsset 조립과 렌더러 생성은 모두 메인 스레드에서 수행.
    /// </summary>
    public class UnityGSBinaryLoader : MonoBehaviour
    {
        private const uint MAGIC   = 0x41534755;
        private const int  VERSION = 1;
        private const int  TEX_W   = 2048;

        // 백그라운드 스레드에서 채운 raw 데이터 컨테이너 (Unity API 없음)
        private class RawSplatData
        {
            public int        Count;
            public float[]    Positions;   // N*3
            public float[]    Scales;      // N*3
            public float[]    Rotations;   // N*4 xyzw
            public float[]    Colors;      // N*4 RGBA
            public bool       HasSH;
            public float[]    SHCoeffs;    // N*48 (null if !HasSH)
        }

        // ── 공개 진입점 ──────────────────────────────────────────────────────────

        public IEnumerator LoadFromBinary(string binaryPath, Action<GaussianSplatRenderer> onComplete)
        {
            if (!File.Exists(binaryPath))
            {
                Debug.LogError($"[UnityGSLoader] File not found: {binaryPath}");
                onComplete?.Invoke(null);
                yield break;
            }

            Debug.Log($"[UnityGSLoader] Loading: {binaryPath}");

            // ① 파일 I/O → 백그라운드 스레드 (Unity API 없음)
            RawSplatData raw  = null;
            Exception    err  = null;
            var task = System.Threading.Tasks.Task.Run(() =>
            {
                try   { raw = ReadRaw(binaryPath); }
                catch (Exception ex) { err = ex; }
            });
            while (!task.IsCompleted) yield return null;

            if (err != null)
            {
                Debug.LogError($"[UnityGSLoader] Read failed: {err.Message}\n{err.StackTrace}");
                onComplete?.Invoke(null);
                yield break;
            }

            // ② GaussianSplatAsset 조립 → 메인 스레드 (ScriptableObject, TextAsset)
            GaussianSplatAsset asset;
            try   { asset = BuildAsset(raw); }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityGSLoader] BuildAsset failed: {ex.Message}\n{ex.StackTrace}");
                onComplete?.Invoke(null);
                yield break;
            }

            // ③ 렌더러 생성 → 비활성 상태로 먼저 만들고 asset 세팅 후 활성화
            //    AddComponent 직후 OnEnable이 즉시 실행되므로 SetActive(false) 선행 필수
            var splatObj = new GameObject("ServerGaussianSplat");
            splatObj.SetActive(false);

            var renderer = splatObj.AddComponent<GaussianSplatRenderer>();
            renderer.m_Asset = asset;               // public 필드 직접 할당 (OnEnable 전에 세팅)

            splatObj.SetActive(true);               // 여기서 OnEnable → 렌더러 초기화

            Debug.Log($"[UnityGSLoader] Rendered {asset.splatCount:N0} splats");
            onComplete?.Invoke(renderer);
        }

        // ── 1단계: 파일 파싱 (백그라운드 스레드, Unity API 사용 금지) ─────────────

        private static RawSplatData ReadRaw(string path)
        {
            using (var fs     = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                uint magic = reader.ReadUInt32();
                if (magic != MAGIC)
                    throw new IOException($"Invalid magic: 0x{magic:X8}, expected 0x{MAGIC:X8}");

                uint ver = reader.ReadUInt32();
                if (ver != VERSION)
                    throw new IOException($"Unsupported version: {ver}");

                int n = (int)reader.ReadUInt32();

                var raw = new RawSplatData { Count = n };

                raw.Positions = ReadFloats(reader, n * 3);
                raw.Scales    = ReadFloats(reader, n * 3);
                raw.Rotations = ReadFloats(reader, n * 4);
                raw.Colors    = ReadFloats(reader, n * 4);

                raw.HasSH = reader.ReadBoolean();
                if (raw.HasSH)
                    raw.SHCoeffs = ReadFloats(reader, n * 48);

                return raw;
            }
        }

        private static float[] ReadFloats(BinaryReader r, int count)
        {
            var buf   = new float[count];
            var bytes = r.ReadBytes(count * 4);
            Buffer.BlockCopy(bytes, 0, buf, 0, bytes.Length);
            return buf;
        }

        // ── 2단계: GaussianSplatAsset 조립 (메인 스레드) ────────────────────────

        private static GaussianSplatAsset BuildAsset(RawSplatData raw)
        {
            int n = raw.Count;

            // 전체 bounds 계산
            var bMin = new Vector3(float.MaxValue,  float.MaxValue,  float.MaxValue);
            var bMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(raw.Positions[i*3], raw.Positions[i*3+1], raw.Positions[i*3+2]);
                bMin = Vector3.Min(bMin, p);
                bMax = Vector3.Max(bMax, p);
            }

            // === 포지션 데이터 (VectorFormat.Float32, 12 bytes/splat) ===
            byte[] posBytes = new byte[n * 12];
            Buffer.BlockCopy(raw.Positions, 0, posBytes, 0, posBytes.Length);

            // === Other 데이터 (packed rotation 4B + scale Float32 12B = 16 bytes/splat) ===
            // 서버 binary: [x, y, z, w] 순서 (convert_ply_to_unitygs.py에서 xyzw로 변환 후 저장)
            byte[] otherBytes = new byte[n * 16];
            for (int i = 0; i < n; i++)
            {
                int off = i * 16;
                var q = new Quaternion(raw.Rotations[i*4],    // x
                                       raw.Rotations[i*4+1],  // y
                                       raw.Rotations[i*4+2],  // z
                                       raw.Rotations[i*4+3]); // w
                WriteUInt(otherBytes, off, PackRotation(q));
                WriteFloat(otherBytes, off + 4,  raw.Scales[i*3]);
                WriteFloat(otherBytes, off + 8,  raw.Scales[i*3+1]);
                WriteFloat(otherBytes, off + 12, raw.Scales[i*3+2]);
            }

            // === 컬러 텍스처 (ColorFormat.Float32x4, Morton-swizzled, 16 bytes/pixel) ===
            var (texW2, texH2) = GaussianSplatAsset.CalcTextureSize(n);
            byte[] colorBytes = new byte[texW2 * texH2 * 16];
            for (int i = 0; i < n; i++)
            {
                int dst = SplatIndexToPixelIndex(i) * 16;
                int src = i * 4;
                WriteFloat(colorBytes, dst,      raw.Colors[src]);
                WriteFloat(colorBytes, dst + 4,  raw.Colors[src + 1]);
                WriteFloat(colorBytes, dst + 8,  raw.Colors[src + 2]);
                WriteFloat(colorBytes, dst + 12, raw.Colors[src + 3]);
            }

            // === SH 데이터 (SHTableItemFloat32, 192 bytes/splat = 48 floats) ===
            // HasSH=false일 때 SHFormat.Norm6를 쓰면 SH 없어도 최소 크기 버퍼가 필요하므로
            // Float32 포맷에서는 shData가 빈 배열이면 안 됨 → Norm6 사용시 dummy 1-entry 필요.
            // 가장 단순한 처리: SH 없으면 Norm6 1엔트리(32bytes) dummy를 제공
            var shFmt = raw.HasSH ? GaussianSplatAsset.SHFormat.Float32
                                   : GaussianSplatAsset.SHFormat.Norm6;
            byte[] shBytes;
            if (raw.HasSH)
            {
                shBytes = new byte[n * 192];
                Buffer.BlockCopy(raw.SHCoeffs, 0, shBytes, 0, shBytes.Length);
            }
            else
            {
                // Norm6: 16 ushorts/splat = 32 bytes/splat (shPadding 포함)
                shBytes = new byte[n * 32];
            }

            // === GaussianSplatAsset 생성 (메인 스레드 전용) ===
            // Float32 포맷 사용 시 chunk data는 null이어야 함.
            // non-null이면 HLSL이 chunk bounds로 위치/스케일을 denormalize해서 렌더링 불가.
            var asset = ScriptableObject.CreateInstance<GaussianSplatAsset>();
            asset.Initialize(n,
                GaussianSplatAsset.VectorFormat.Float32,
                GaussianSplatAsset.VectorFormat.Float32,
                GaussianSplatAsset.ColorFormat.Float32x4,
                shFmt,
                bMin, bMax,
                cameraInfos: null);

            asset.SetAssetFiles(
                null,                      // chunk data는 Float32 포맷에서 null
                new TextAsset(posBytes),
                new TextAsset(otherBytes),
                new TextAsset(colorBytes),
                new TextAsset(shBytes));

            return asset;
        }

        // ── 회전 패킹 (Smallest-Three, 10-10-10-2 bits) ──────────────────────────
        // HLSL DecodePacked_10_10_10_2: bits[0-9]=x, [10-19]=y, [20-29]=z, [30-31]=w
        // PackSmallest3Rotation 후 EncodeQuatToNorm10과 동일한 비트 배치여야 함:
        //   enc[0](첫 번째 non-max) → bits[0-9], enc[1] → bits[10-19], enc[2] → bits[20-29]

        private static uint PackRotation(Quaternion q)
        {
            float len = Mathf.Sqrt(q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w);
            if (len > 1e-6f) { q.x /= len; q.y /= len; q.z /= len; q.w /= len; }

            float[] c = { q.x, q.y, q.z, q.w };
            int maxIdx = 0;
            for (int i = 1; i < 4; i++)
                if (Mathf.Abs(c[i]) > Mathf.Abs(c[maxIdx])) maxIdx = i;

            float sign = c[maxIdx] >= 0f ? 1f : -1f;
            const float range = 0.70710678118f;
            uint[] enc = new uint[3];
            int j = 0;
            for (int i = 0; i < 4; i++)
            {
                if (i == maxIdx) continue;
                enc[j++] = (uint)Mathf.Clamp(Mathf.RoundToInt((c[i]*sign / range + 1f) * 511.5f), 0, 1023);
            }
            // enc[0] → bits[0-9], enc[1] → bits[10-19], enc[2] → bits[20-29], maxIdx → bits[30-31]
            return ((uint)maxIdx << 30) | (enc[2] << 20) | (enc[1] << 10) | enc[0];
        }

        // ── Morton 스위즐 ─────────────────────────────────────────────────────────
        // SplatIndexToPixelIndex: splat index → texture pixel index
        // HLSL/에디터와 동일한 로직: 하위 8비트 = 타일 내 Morton 코드, 상위 비트 = 타일 인덱스

        private static int SplatIndexToPixelIndex(int idx)
        {
            var (lx, ly) = DecodeMorton16x16(idx & 0xFF);
            int tileIdx = idx >> 8;
            int tx = tileIdx % (TEX_W / 16);
            int ty = tileIdx / (TEX_W / 16);
            int px = tx * 16 + lx;
            int py = ty * 16 + ly;
            return py * TEX_W + px;
        }

        // GaussianUtils.DecodeMorton2D_16x16 C# 포팅
        private static (int x, int y) DecodeMorton16x16(int t)
        {
            uint u = (uint)(t & 0xFF);
            u = (u & 0xFF) | ((u & 0xFE) << 7);
            u &= 0x5555;
            u = (u ^ (u >> 1)) & 0x3333;
            u = (u ^ (u >> 2)) & 0x0f0f;
            return ((int)(u & 0xF), (int)(u >> 8));
        }

        // ── 바이트 쓰기 ───────────────────────────────────────────────────────────

        private static void WriteFloat(byte[] buf, int off, float v)
        {
            var b = BitConverter.GetBytes(v);
            buf[off] = b[0]; buf[off+1] = b[1]; buf[off+2] = b[2]; buf[off+3] = b[3];
        }

        private static void WriteUInt(byte[] buf, int off, uint v)
        {
            buf[off] = (byte)v; buf[off+1] = (byte)(v>>8);
            buf[off+2] = (byte)(v>>16); buf[off+3] = (byte)(v>>24);
        }

    }
}
