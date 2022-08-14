using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Unity.Profiling;
using UnityEditor;
using Utils;

public class Grid : MonoBehaviour
{
    public Vector3Int GridSize = new(10, 10, 10);
    public Vector3 GridSizeF => GridSize;
    public float CellSize;

    [SerializeField] private bool _showDebugVoxels;
    [SerializeField] private GameObject _debugVoxelSpherePrefab;

    private Dictionary<Vector3Int, GameObject> _debugVoxelSpheres = new();

    private readonly ProfilerMarker _profileBoxIntersection = new("GridBase.BoxIntersection");

    public void Create()
    {
    }

    public static int IndexFromXYZ(Vector3Int p, Vector3Int gridSize, out bool outOfBounds)
    {
        outOfBounds = false;
        if (p.x >= gridSize.x || p.x < 0 || p.y >= gridSize.y || p.y < 0 || p.z >= gridSize.z || p.z < 0)
            outOfBounds = true;
        return p.z * gridSize.x * gridSize.y + p.y * gridSize.x + p.x;
    }
    
    public static Vector3Int XYZFromIndex(int i, Vector3Int gridSize)
    {
        Vector3Int ret = new();
        ret.z = i / (gridSize.x * gridSize.y);
        i -= ret.z * gridSize.x * gridSize.y;
        ret.y = i / gridSize.x;
        ret.x = i % gridSize.x;
        return ret;
    }
    
    #if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (GridSize.x * GridSize.y * GridSize.z > 1000)
            return;
        
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.black;
        
        for (int x = 0; x < GridSize.x; x++)
        {
            for (int y = 0; y < GridSize.y; y++)
            {
                for (int z = 0; z < GridSize.z; z++)
                {
                    if (x == 0)
                    {
                        Vector3 from = new Vector3(x, y, z) - Vector3.one * CellSize * 0.5f;
                        Vector3 to = new Vector3(x + GridSizeF.x, y, z) - Vector3.one * CellSize * 0.5f;
                        Gizmos.DrawLine(from, to);
                    }
                    if (y == 0)
                    {
                        Vector3 from = new Vector3(x, y, z) - Vector3.one * CellSize * 0.5f;
                        Vector3 to = new Vector3(x, y + GridSizeF.y, z) - Vector3.one * CellSize * 0.5f;
                        Gizmos.DrawLine(from, to);
                    }
                    if (z == 0)
                    {
                        Vector3 from = new Vector3(x, y, z) - Vector3.one * CellSize * 0.5f;
                        Vector3 to = new Vector3(x, y, z + GridSizeF.z) - Vector3.one * CellSize * 0.5f;
                        Gizmos.DrawLine(from, to);
                    }
                    
                    Handles.Label(new Vector3(x, y, z), $"{IndexFromXYZ(new Vector3Int(x, y, z), GridSize, out bool _)}", style);
                }
            }
        }
    }
#endif
}
