using System.Collections.Generic;
using UnityEngine;

public class SpiralVisualizer : MonoBehaviour
{
    public static SpiralVisualizer Instance => Services.Get<SpiralVisualizer>();

    [Header("Visualizer Settings")]
    public GameObject cubePrefab;
    public Transform cubeContainer;
    public float rotationSpeed = 10.0f;

    [HideInInspector]
    public Vector3 spiralCenter = Vector3.zero;

    private List<GameObject> spiralCubes = new List<GameObject>();

    void Awake()
    {
        if (!Services.Register(this))
        {
            Destroy(gameObject);
        }
        else
        {
            DontDestroyOnLoad(gameObject);
        }

        if (cubeContainer == null)
        {
            cubeContainer = new GameObject("SpiralCubeContainer").transform;
            cubeContainer.SetParent(this.transform);
        }
    }
    
    void OnDestroy() => Services.Unregister(this);

    void Start()
    {
        Hide();
    }

    void Update()
    {
        if (cubeContainer != null && cubeContainer.gameObject.activeSelf)
        {
            cubeContainer.RotateAround(spiralCenter, Vector3.up, rotationSpeed * Time.deltaTime);
        }
    }


    public void Show()
    {
        if (cubeContainer != null)
        {
            cubeContainer.gameObject.SetActive(true);
        }
    }

    public void Hide()
    {
        if (cubeContainer != null)
        {
            cubeContainer.gameObject.SetActive(false);
        }
    }

    public void AddSpiralStep(Vector3 worldPos)
    {
        if (cubePrefab == null)
        {
            Debug.LogWarning("SpiralVisualizer: Cube Prefab is not assigned.");
            return;
        }

        GameObject newCube = Instantiate(cubePrefab, worldPos, Quaternion.identity, cubeContainer);
        spiralCubes.Add(newCube);
    }

    public void ClearSpiral()
    {
        foreach (var cube in spiralCubes)
        {
            Destroy(cube);
        }
        spiralCubes.Clear();
    }
}
