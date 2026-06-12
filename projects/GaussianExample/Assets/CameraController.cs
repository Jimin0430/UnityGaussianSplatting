using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("데스크탑")]
    public float moveSpeed = 10f;
    public float rotateSensitivity = 0.15f;
    public float scrollZoomSpeed = 5f;
    public float trackpadRotateSensitivity = 3f;

    [Header("모바일")]
    public float touchRotateSensitivity = 0.15f;
    public float pinchZoomSpeed = 0.05f;
    public float panSpeed = 0.02f;

    [Header("오빗")]
    public Vector3 orbitTarget = Vector3.zero;
    public float orbitDistance = 5f;

    float _yaw;
    float _pitch;

    void Start()
    {
        Debug.Log($"Max Compute Group X: {SystemInfo.maxComputeWorkGroupSizeX}");
        Debug.Log($"Max Compute Group Y: {SystemInfo.maxComputeWorkGroupSizeY}");
        Debug.Log($"Max Compute Group Z: {SystemInfo.maxComputeWorkGroupSizeZ}");

        _yaw = transform.eulerAngles.y;
        _pitch = transform.eulerAngles.x;
        if (_pitch > 180f) _pitch -= 360f;

        // 현재 카메라 위치 기준으로 orbitTarget/orbitDistance 자동 설정
        orbitDistance = Vector3.Distance(transform.position, orbitTarget);
    }

    void ApplyOrbit()
    {
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        transform.position = orbitTarget + rot * new Vector3(0f, 0f, -orbitDistance);
        transform.rotation = rot;
    }

    void Update()
    {
#if UNITY_IOS || UNITY_ANDROID
        HandleTouch();
#else
        HandleDesktop();
#endif
        ApplyOrbit();
    }

    void HandleDesktop()
    {
        // 좌클릭 또는 우클릭 드래그 → 회전
        if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
        {
            _yaw += Input.GetAxis("Mouse X") * rotateSensitivity * 100f * Time.deltaTime;
            _pitch -= Input.GetAxis("Mouse Y") * rotateSensitivity * 100f * Time.deltaTime;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);
        }

        // WASD / 방향키 → 타겟 이동
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        float up = 0f;
        if (Input.GetKey(KeyCode.E)) up = 1f;
        if (Input.GetKey(KeyCode.Q)) up = -1f;

        Vector3 dir = transform.right * h + transform.forward * v + Vector3.up * up;
        orbitTarget += dir * moveSpeed * Time.deltaTime;

        // 두 손가락 트랙패드 드래그 → 회전
        Vector2 scrollDelta = Input.mouseScrollDelta;
        if (Mathf.Abs(scrollDelta.x) > 0.01f || Mathf.Abs(scrollDelta.y) > 0.01f)
        {
            _yaw += scrollDelta.x * trackpadRotateSensitivity;
            _pitch -= scrollDelta.y * trackpadRotateSensitivity;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);
        }

        // Ctrl + 스크롤 → 줌 (거리 조절)
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            orbitDistance -= scroll * scrollZoomSpeed;
            orbitDistance = Mathf.Max(0.1f, orbitDistance);
        }
    }

    void HandleTouch()
    {
        if (Input.touchCount == 1)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Moved)
            {
                _yaw += t.deltaPosition.x * touchRotateSensitivity;
                _pitch -= t.deltaPosition.y * touchRotateSensitivity;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            }
        }
        else if (Input.touchCount >= 2)
        {
            Touch t0 = Input.GetTouch(0);
            Touch t1 = Input.GetTouch(1);

            Vector2 t0Prev = t0.position - t0.deltaPosition;
            Vector2 t1Prev = t1.position - t1.deltaPosition;

            // 핀치 → 줌 (거리 조절)
            float prevDist = (t0Prev - t1Prev).magnitude;
            float currDist = (t0.position - t1.position).magnitude;
            float pinchDelta = currDist - prevDist;
            orbitDistance -= pinchDelta * pinchZoomSpeed;
            orbitDistance = Mathf.Max(0.1f, orbitDistance);

            // 두 손가락 평균 이동 → 패닝
            Vector2 panDelta = (t0.deltaPosition + t1.deltaPosition) * 0.5f;
            orbitTarget -= (transform.right * panDelta.x + transform.up * panDelta.y) * panSpeed;
        }
    }
}
