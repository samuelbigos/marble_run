using System.Collections.Generic;
using Tiles;
using Unity.Collections;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine.Rendering;
using Unity.Profiling;
using UnityEngine.Tilemaps;

public class TileFactory : MonoBehaviour
{
    public const int TilePoolSize = 1000;
    public TileInstance TilePrefab;

    private Grid _grid;
    
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct VertexDesc
    {
        public Vector3 pos;
        public Vector3 normal;
        public Vector2 uv;
    }
    private readonly VertexAttributeDescriptor[] _vertexLayout = new[]
    {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
    };

    private const int TEMP_CELL_MESH_BUFFER_SIZE = 2048;
    private NativeArray<VertexDesc> _protMeshVerts;
    private NativeArray<ushort> _protMeshIndices;
    private NativeArray<Vector3> _sdfVerts;
    private NativeArray<Vector3> _cornerVertsBot;
    private NativeArray<Vector3> _cornerVertsTop;

    private const int TEMP_PROT_MESH_BUFFER_SIZE = 1024;
    private NativeArray<Vector3> _meshVerts;
    private NativeArray<Vector3> _meshNormals;
    private NativeArray<Vector2> _meshUvs;
    private NativeArray<ushort> _meshIndices;

    protected Dictionary<int, TileInstance> _cellToTile = new Dictionary<int, TileInstance>();
    protected Dictionary<int, int> _cellToProt = new Dictionary<int, int>();
    private List<TileInstance> _tilePoolUnused;

    private readonly ProfilerMarker _profileCreateTile = new ProfilerMarker("TileFactory.CreateTile");
    private readonly ProfilerMarker _profileActivateNewTile = new ProfilerMarker("TileFactory.ActivateNewTile");
    private readonly ProfilerMarker _profileTransformVerts = new ProfilerMarker("TileFactory.TransformVerts");

    private void Awake()
    {
        _protMeshVerts = new NativeArray<VertexDesc>(TEMP_CELL_MESH_BUFFER_SIZE, Allocator.Persistent);
        _protMeshIndices = new NativeArray<ushort>(TEMP_CELL_MESH_BUFFER_SIZE, Allocator.Persistent);
        _sdfVerts = new NativeArray<Vector3>(TEMP_CELL_MESH_BUFFER_SIZE, Allocator.Persistent);
        _cornerVertsBot = new NativeArray<Vector3>(4, Allocator.Persistent);
        _cornerVertsTop = new NativeArray<Vector3>(4, Allocator.Persistent);

        _meshVerts = new NativeArray<Vector3>(TEMP_PROT_MESH_BUFFER_SIZE, Allocator.Persistent);
        _meshNormals = new NativeArray<Vector3>(TEMP_PROT_MESH_BUFFER_SIZE, Allocator.Persistent);
        _meshUvs = new NativeArray<Vector2>(TEMP_PROT_MESH_BUFFER_SIZE, Allocator.Persistent);
        _meshIndices = new NativeArray<ushort>(TEMP_PROT_MESH_BUFFER_SIZE, Allocator.Persistent);
    }

    protected void OnDestroy()
    {
        _protMeshVerts.Dispose();
        _protMeshIndices.Dispose();
        _sdfVerts.Dispose();
        _cornerVertsBot.Dispose();
        _cornerVertsTop.Dispose();
        _meshVerts.Dispose();
        _meshNormals.Dispose();
        _meshUvs.Dispose();
        _meshIndices.Dispose();
    }

    public void Init(Grid grid)
    {
        _grid = grid;
        _tilePoolUnused = new List<TileInstance>();
        for (int i = 0; i < TilePoolSize; i++)
        {
            TileInstance tile = Instantiate(TilePrefab, transform, true);
            _tilePoolUnused.Add(tile);
            tile.gameObject.SetActive(false);
        }
    }

    public void ResetTiles()
    {
        List<int> tiles = new List<int>();
        foreach (int idx in _cellToTile.Keys)
        {
            tiles.Add(idx);
        }
        foreach (int idx in tiles)
        {
            ReturnToPool(idx);
            _cellToProt.Remove(idx);
        }
    }

    public bool SetTile(int cellIdx, int tileIdx, TileDatabase tileDatabase, Grid grid)
    {
        TileDatabase.Tile tile = tileDatabase.Tiles[tileIdx];
        if (tile.Mesh == null)
            return true;
            
        TileInstance tileInstance = null;
        if (_cellToProt.ContainsKey(cellIdx))
        {
            bool recreateMesh = _cellToProt[cellIdx] != tileIdx;
            if (recreateMesh)
            {
                if (_cellToTile.ContainsKey(cellIdx))
                {
                    tileInstance = _cellToTile[cellIdx];
                }
                _cellToProt.Remove(cellIdx);
            }
            else
            {
                return false;
            }
        }

        _cellToProt.Add(cellIdx, tileIdx);
        
        if (tileInstance == null)
        {
            tileInstance = FetchFromPool(cellIdx);
        }
        tileInstance.Init(tile, cellIdx, grid);
        return true;
    }

    private TileInstance FetchFromPool(int cellIdx)
    {
        _profileActivateNewTile.Begin();
        TileInstance tile = _tilePoolUnused[^1];
        _tilePoolUnused.RemoveAt(_tilePoolUnused.Count - 1);
        _cellToTile[cellIdx] = tile;
        _cellToTile[cellIdx].gameObject.SetActive(true);
        _profileActivateNewTile.End();
        return tile;
    }

    private void ReturnToPool(int cellIdx)
    {
        TileInstance tile = _cellToTile[cellIdx];
        tile.gameObject.SetActive(false);
        _tilePoolUnused.Add(tile);
        _cellToTile.Remove(cellIdx);
    }

    // private bool GenerateMeshForCollapsedCell(Mesh mesh, VoxelGrid.Cell cell, TileDatabase.Tile tile)
    // {
    //     Vector3[] gridVertsBot = _grid.VoxelVerts;
    //     Vector3[] gridVertsTop = _grid.VoxelVerts;
    //     for (int i = 0; i < 4; i++)
    //     {
    //         _cornerVertsBot[i] = gridVertsBot[cell.vBot[i]];
    //         _cornerVertsTop[i] = gridVertsTop[cell.vTop[i]];
    //     }
    //
    //     int vertCount = 0;
    //     int indexCount = 0;
    //     
    //     mesh = tile.Mesh;
    //     
    //     return true;
    // }
}
