using System.Collections.Generic;
using Tiles;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

public class TileFactory : MonoBehaviour
{
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

    protected Dictionary<int, TileInstance> _cellToTile = new Dictionary<int, TileInstance>();
    protected Dictionary<int, int> _cellToProt = new Dictionary<int, int>();
    private List<TileInstance> _tilePoolUnused;
    private bool _initialized;

    private readonly ProfilerMarker _profileCreateTile = new ProfilerMarker("TileFactory.CreateTile");
    private readonly ProfilerMarker _profileActivateNewTile = new ProfilerMarker("TileFactory.ActivateNewTile");
    private readonly ProfilerMarker _profileTransformVerts = new ProfilerMarker("TileFactory.TransformVerts");

    private void Awake()
    {
    }

    protected void OnDestroy()
    {
        Dispose();
    }

    private void Dispose()
    {
        if (_initialized)
        {
        }
        
        _cellToProt.Clear();
        _cellToTile.Clear();
    }

    public void Init(Grid grid)
    {
        ResetTiles();
        Dispose();
        
        for (int c = transform.childCount - 1; c >= 0; c--)
        {
            Transform child = transform.GetChild(c);
            if (child && child.gameObject) 
            {
                Destroy(child.gameObject);
            }
        }

        _grid = grid;
        _tilePoolUnused = new List<TileInstance>();
        int poolSize = _grid.GridSize.x * _grid.GridSize.y * _grid.GridSize.z;
        for (int i = 0; i < poolSize; i++)
        {
            TileInstance tile = Instantiate(TilePrefab, transform, true);
            _tilePoolUnused.Add(tile);
            tile.gameObject.SetActive(false);
        }

        _initialized = true;
    }

    private void ResetTiles()
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

    public bool TileHasMesh(int tileIdx, TileDatabase tileDatabase)
    {
        TileDatabase.Tile tile = tileDatabase.Tiles[tileIdx];
        return tile.Mesh != null;
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
        tileInstance.Init(tile, cellIdx, grid, Vector3.zero, transform);
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
}
