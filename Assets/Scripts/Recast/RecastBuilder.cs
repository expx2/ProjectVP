using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Serialization;

public class RecastBuilder : MonoBehaviour
{
    /// <summary>
    /// 이웃의 X 인덱스 오프셋<br/>
    /// 서, 북, 동, 남 시계 방향
    /// </summary>
    public static readonly int[] DirectionOffsetX = { -1, 0, 1, 0 };

    /// <summary>
    /// 이웃의 Z 인덱스 오프셋
    /// 서, 북, 동, 남 시계 방향
    /// </summary>
    public static readonly int[] DirectionOffsetZ = { 0, 1, 0, -1 };
    
    [SerializeField] private Vector3 _size = new(50f, 30f, 50f);
    [SerializeField] private float _voxelSize = 0.5f;
    [SerializeField] private  float _voxelHeight = 0.25f;
    
    [SerializeField] private  float _walkableHeight = 2f;
    // 아래 방향으로 조금 더 많이 이동할 수 있도록 구분 
    // [SerializeField] private float _walkableClimb = 0.5f;
    [SerializeField] private float _upWalkableClimb = 0.5f;
    [SerializeField] private float _downWalkableClimb = 1f;
    [SerializeField] private float _walkableSlopeAngle = 45f;
    [SerializeField] private float _agentRadius = 0.5f;
    
    private int Width => Mathf.CeilToInt(_size.x / _voxelSize);
    private int Height => Mathf.CeilToInt(_size.y / _voxelHeight);
    private int Depth => Mathf.CeilToInt(_size.z / _voxelSize);
    private Vector3 ZeroPosition
    {
        get
        {
            var halfSize = _voxelSize * 0.5f;
            var halfHeight = _voxelHeight * 0.5f;
            return -new Vector3(Width * halfSize, Height * halfHeight, Depth * halfSize);
        }
    }
    
    /// <summary>
    /// 서 있을 수 있는 최소 복셀 수
    /// </summary>
    private int WalkableHeightVoxels => (int)math.ceil(_walkableHeight / _voxelHeight);
    /// <summary>
    /// 걸어 올라갈 수 있는 최대 복셀 수
    /// </summary>
    // public int WalkableClimb => (int)math.floor(WalkableClimb / VoxelHeight);
    private int UpWalkableClimbVoxels => (int)math.floor(_upWalkableClimb / _voxelHeight);
    /// <summary>
    /// 걸어 내려갈 수 있는 최대 복셀 수
    /// </summary>
    private int DownWalkableClimbVoxels => (int)math.floor(_downWalkableClimb / _voxelHeight);
    private int WalkableRadiusVoxels => (int)math.ceil(_agentRadius / _voxelSize);
    
    public RecastGraph RecastGraph { get; private set; }
    
    
    public void Build()
    {
        // 1. Gather meshes
        var gatheredMesh = MeshGatherer.GatherMeshes();

        // 2. rasterization & build heightfield
        var heightfield = new Heightfield(Width, Depth);
        new JobRasterize()
        {
            Width = Width,
            Height =  Height,
            Depth = Depth,
            VoxelSize = _voxelSize,
            VoxelHeight = _voxelHeight,
            WalkableSlopeAngle = _walkableSlopeAngle,
            GatheredMeshes = gatheredMesh,
            Heightfield = heightfield,
        }.Schedule().Complete();
        
        gatheredMesh.Dispose();
        
        // 3. filter low hanging walkable obstacles
        new JobFilterLowHangingWalkableObstacles()
        {
            Heightfield = heightfield,
            UpWalkableClimb =  UpWalkableClimbVoxels,
            DownWalkableClimb = DownWalkableClimbVoxels,
        }.Schedule().Complete();

        // 4. filter ledge spans
        new JobFilterLedgeSpans()
        {
            Heightfield = heightfield,
            WalkableHeight = WalkableHeightVoxels,
            UpWalkableClimb =  UpWalkableClimbVoxels,
            DownWalkableClimb = DownWalkableClimbVoxels,
        }.Schedule().Complete();
        
        // 5. filter walkable low height spans
        new JobFilterWalkableLowHeightSpans()
        {
            Heightfield = heightfield,
            WalkableHeight = WalkableHeightVoxels,
        }.Schedule().Complete();

        // 6. build compact heightfield
        var compactHeightfield = new CompactHeightfield(heightfield);
        
        new JobBuildCompactHeightfield()
        {
            heightfield = heightfield,
            compactHeightfield = compactHeightfield,
            walkableHeight = WalkableHeightVoxels,
            upWalkable = UpWalkableClimbVoxels,
            downWalkable = DownWalkableClimbVoxels,
        }.Schedule().Complete();
        
        heightfield.Dispose();
        
        // 7. erode compact spans
        // 벽에 붙어 생성되는 것을 허용하도록 주석 처리함
        // new JobErode()
        // {
        //     CompactHeightfield = compactHeightfield,
        //     WalkableRadius = WalkableRadiusVoxels,
        // }.Schedule().Complete();
        
        // 8. Copy to graph
        var cellCount = compactHeightfield.Cells.Length;
        var spanCount = compactHeightfield.CompactSpans.Length;
        RecastGraph = new RecastGraph()
        {
            Width = Width,
            Height = Height,
            Depth = Depth,
            VoxelSize = _voxelSize,
            VoxelHeight = _voxelHeight,
            Cells = new CompactCell[cellCount],
            CompactSpans = new CompactSpan[spanCount],
            IsWalkables = new bool[spanCount],
        };

        for (var i = 0; i < cellCount; i++)
        {
            RecastGraph.Cells[i] = compactHeightfield.Cells[i];
        }

        for (var i = 0; i < spanCount; i++)
        {
            RecastGraph.CompactSpans[i] = compactHeightfield.CompactSpans[i];
            RecastGraph.IsWalkables[i] = compactHeightfield.IsWalkables[i];
        }

        RecastGraph.DrawDebugLines(false, VoxelToWorldMatrix, Color.cyan, 5f);
        
        // 9. end
        compactHeightfield.Dispose();
    }

    public float4x4 VoxelToWorldMatrix => math.mul(float4x4.Translate(ZeroPosition),
        float4x4.Scale(_voxelSize, _voxelHeight, _voxelSize));
    
    private void OnDrawGizmos()
    {
        var width = _voxelSize * Width;
        var height = _voxelHeight * Height;
        var depth =  _voxelSize * Depth;

        var size = new Vector3(width, height, depth);
        DebugUtility.DrawCube(transform.position, size, Color.white);
    }
}

