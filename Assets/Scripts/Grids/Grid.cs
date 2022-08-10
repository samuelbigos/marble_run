using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Unity.Profiling;
using Utils;

public class Grid : MonoBehaviour
{
    public Vector3Int GridSize = new(10, 10, 10);
    public Vector3 GridSizeF;
    public float CellSize;

    [SerializeField] private bool _showDebugVoxels;
    [SerializeField] private GameObject _debugVoxelSpherePrefab;

    private Dictionary<Vector3Int, GameObject> _debugVoxelSpheres = new();

    private readonly ProfilerMarker _profileBoxIntersection = new("GridBase.BoxIntersection");

    public void Create()
    {
        GridSizeF.x = GridSize.x;
        GridSizeF.y = GridSize.y;
        GridSizeF.z = GridSize.z;
    }

    public int IndexFromXYZ(Vector3Int p)
    {
        return p.z * GridSize.x * GridSize.y + p.y * GridSize.x + p.x;
    }
    
    public Vector3Int XYZFromIndex(int i)
    {
        Vector3Int ret = new();
        ret.z = i / (GridSize.x * GridSize.y);
        i -= ret.z * GridSize.x * GridSize.y;
        ret.y = i / GridSize.x;
        ret.x = i % GridSize.x;
        return ret;
    }
}
