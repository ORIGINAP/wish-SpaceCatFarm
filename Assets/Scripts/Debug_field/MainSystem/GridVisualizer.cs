using UnityEngine;

public class GridVisualizer : MonoBehaviour
{
    [Header("표시 설정")]
    [Tooltip("게임 시작 시 그리드 표시 여부")]
    [SerializeField] private bool startVisible = true;

    [Tooltip("토글 단축키 (기본 G)")]
    [SerializeField] private KeyCode toggleKey = KeyCode.G;

    [Header("색상")]
    [Tooltip("내부 격자선 색상")]
    [SerializeField] private Color cellColor   = new Color(1f, 1f, 1f, 0.07f);

    [Tooltip("그리드 외곽선 색상")]
    [SerializeField] private Color borderColor = new Color(0f, 0.87f, 0.67f, 0.75f);

    [Tooltip("셀 좌표 텍스트 표시 여부")]
    [SerializeField] private bool showCoordinates = false;

    private bool     isVisible;
    private Material glMat;

    private void Awake()
    {
        isVisible = startVisible;
        CreateMaterial();
    }

    private void Update()
    {
        if (!TextInputFocus.Active && (GameInput.GetKeyDown(toggleKey) || (InputManager.Instance != null && InputManager.Instance.GridButton)))
        {
            isVisible = !isVisible;
        }
    }

    private void OnRenderObject()
    {
        if (!isVisible)        return;
        if (glMat == null)     return;
        if (GridManager.Instance == null) return;

        int     w      = GridManager.Instance.GridWidth;
        int     h      = GridManager.Instance.GridHeight;
        float   cs     = GridManager.Instance.CellSize;
        Vector2 origin = GridManager.Instance.GridOrigin;

        int halfW = w / 2;
        int halfH = h / 2;
        int startX = -halfW;
        int endX   = w - halfW;
        int startY = -halfH;
        int endY   = h - halfH;

        int vx0 = startX, vx1 = endX, vy0 = startY, vy1 = endY;
        var cam = Camera.current != null ? Camera.current : Camera.main;
        // 화면에 보이는 칸만 그림
        if (cam != null && cam.orthographic)
        {
            float   hh = cam.orthographicSize, hw = hh * cam.aspect;
            Vector3 c  = cam.transform.position;
            vx0 = Mathf.Max(startX, Mathf.FloorToInt((c.x - hw - origin.x) / cs));
            vx1 = Mathf.Min(endX,   Mathf.CeilToInt ((c.x + hw - origin.x) / cs));
            vy0 = Mathf.Max(startY, Mathf.FloorToInt((c.y - hh - origin.y) / cs));
            vy1 = Mathf.Min(endY,   Mathf.CeilToInt ((c.y + hh - origin.y) / cs));
        }

        glMat.SetPass(0);

        GL.PushMatrix();
        GL.Begin(GL.LINES);
        GL.Color(cellColor);

        if (vx0 <= vx1 && vy0 <= vy1)
        {
            for (int x = vx0; x <= vx1; x++)
            {
                float wx = origin.x + x * cs;
                GL.Vertex3(wx, origin.y + vy0 * cs, 0f);
                GL.Vertex3(wx, origin.y + vy1 * cs, 0f);
            }

            for (int y = vy0; y <= vy1; y++)
            {
                float wy = origin.y + y * cs;
                GL.Vertex3(origin.x + vx0 * cs, wy, 0f);
                GL.Vertex3(origin.x + vx1 * cs, wy, 0f);
            }
        }

        GL.End();

        GL.Begin(GL.LINES);
        GL.Color(borderColor);

        float x0 = origin.x + startX * cs, y0 = origin.y + startY * cs;
        float x1 = origin.x + endX * cs,   y1 = origin.y + endY * cs;

        GL.Vertex3(x0, y0, 0f); GL.Vertex3(x1, y0, 0f);
        GL.Vertex3(x1, y0, 0f); GL.Vertex3(x1, y1, 0f);
        GL.Vertex3(x1, y1, 0f); GL.Vertex3(x0, y1, 0f);
        GL.Vertex3(x0, y1, 0f); GL.Vertex3(x0, y0, 0f);

        GL.End();
        GL.PopMatrix();
    }

    private void OnDestroy()
    {
        if (glMat != null)
            Destroy(glMat);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!startVisible) return;
        if (GridManager.Instance == null) return;

        int     w      = GridManager.Instance.GridWidth;
        int     h      = GridManager.Instance.GridHeight;
        float   cs     = GridManager.Instance.CellSize;
        Vector2 origin = GridManager.Instance.GridOrigin;

        int halfW = w / 2;
        int halfH = h / 2;
        int startX = -halfW;
        int endX   = w - halfW;
        int startY = -halfH;
        int endY   = h - halfH;

        float wx0 = origin.x + startX * cs, wy0 = origin.y + startY * cs;
        float wx1 = origin.x + endX * cs,   wy1 = origin.y + endY * cs;

        Gizmos.color = borderColor;
        Vector3 tl = new(wx0, wy1, 0f);
        Vector3 tr = new(wx1, wy1, 0f);
        Vector3 bl = new(wx0, wy0, 0f);
        Vector3 br = new(wx1, wy0, 0f);
        Gizmos.DrawLine(bl, br);
        Gizmos.DrawLine(br, tr);
        Gizmos.DrawLine(tr, tl);
        Gizmos.DrawLine(tl, bl);

        Gizmos.color = cellColor;
        for (int x = startX + 1; x < endX; x++)
        {
            float wx = origin.x + x * cs;
            Gizmos.DrawLine(new Vector3(wx, wy0, 0f), new Vector3(wx, wy1, 0f));
        }
        for (int y = startY + 1; y < endY; y++)
        {
            float wy = origin.y + y * cs;
            Gizmos.DrawLine(new Vector3(wx0, wy, 0f), new Vector3(wx1, wy, 0f));
        }

        if (showCoordinates)
        {
            UnityEditor.Handles.color = new Color(1f, 1f, 1f, 0.35f);
            for (int x = startX; x < endX; x++)
            for (int y = startY; y < endY; y++)
            {
                Vector3 pos = new(origin.x + x * cs + cs * 0.5f,
                                  origin.y + y * cs + cs * 0.25f, 0f);
                UnityEditor.Handles.Label(pos, $"{x},{y}");
            }
        }
    }
#endif

    private void CreateMaterial()
    {
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            Debug.LogError("[GridVisualizer] 'Hidden/Internal-Colored' 셰이더를 찾을 수 없습니다.");
            return;
        }

        glMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        glMat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        glMat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glMat.SetInt("_Cull",      (int)UnityEngine.Rendering.CullMode.Off);
        glMat.SetInt("_ZWrite",    0);
        glMat.SetInt("_ZTest",     (int)UnityEngine.Rendering.CompareFunction.Always);
    }
}
