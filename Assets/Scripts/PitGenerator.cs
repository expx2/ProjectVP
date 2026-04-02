using Unity.Collections;
using UnityEngine;

public class PitGenerator : MonoBehaviour
{
    [SerializeField] private RecastBuilder _recastBuilder;

    [Header("Flood Fill")]
    [SerializeField] private int _volume = 100;
    
    [Header("Marching Squares")]
    [SerializeField] private MeshFilter _meshFilter;
    [SerializeField] private float _bottomY = -0.8f;
    [SerializeField] private float _height = 3f;
    [SerializeField] private float _contourRound = 0.15f;
    [SerializeField] [Range(0f, 0.249f)] private float _interpolation = 0.15f;
    [SerializeField] private int _flattenCount = 1;
    [SerializeField] private float _wallUVMultiplier = 1f;
    
    private void Update()
    {
        if (!_recastBuilder || _recastBuilder.RecastGraph.Equals(default(RecastGraph)))
        {
            return;
        }
        
        if (Input.GetMouseButtonUp(0))
        {
            if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out var hit))
            {
                Generate(hit.point);
            }
        }
    }

    private void Generate(Vector3 position)
    {
        // flood fill
        var fillDatas = FloodFiller.GetFloodFillData(position, _volume, _recastBuilder.RecastGraph);
        if (fillDatas == null)
        {
            return;
        }
                
        // Copy to native
        var native = new NativeArray<FloodFillData>(fillDatas.Length, Allocator.TempJob);
        for (var i = 0; i < fillDatas.Length; i++)
        {
            native[i] = fillDatas[i];
        }
        
        // marching squares
        MarchingSquaresGenerator.Generate(native, _recastBuilder.VoxelToWorldMatrix, _meshFilter, 
            _bottomY, _height, _contourRound, _interpolation, _flattenCount, _wallUVMultiplier);
        
        native.Dispose();
    }
    
    private void OnGUI()
    {
        if (GUILayout.Button("Build", GUILayout.Width(200), GUILayout.Height(100)))
        {
            _recastBuilder.Build();
        }
    }
}
