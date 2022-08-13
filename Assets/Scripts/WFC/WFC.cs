using System;
using System.Collections.Generic;
using Tiles;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Profiling;
using Utils;

public class WFC : MonoBehaviour
{
    public enum StepResult
    {
        WFCInProgress,
        WFCFinished,
        WFCPropagateError,
        WFCCollapseError
    }

    public enum StepReturnParams
    {
        Result,
        IncompatibleStack,
        IncompatibleNeighbor,
        CollapsedCount,
        COUNT
    }

    private enum TILE
    {
        TOP,
        BOT,
        SIDE_0, SIDE_1, SIDE_2, SIDE_3,
        NOT_SIDE_0, NOT_SIDE_1, NOT_SIDE_2, NOT_SIDE_3,
        COMPOSITE,
        INDEX_IN_COMPOSITE,
        WEIGHT,
        COUNT
    }
    
    protected Vector3Int[] _d = 
    {
        new(0, 0, 1),
        new(1, 0, 0),
        new(0, 0, -1),
        new(-1, 0, 0),
        new(0, 1, 0),
        new(0, -1, 0)
    };

    private int C;
    private int T;
    private int D;

    private NativeArray<ushort> _cellsActive;
    private NativeArrayXD<int> _neighbors;
    private NativeArrayXD<int> _tiles;
    private NativeArrayXD<int> _compositeTiles;
    private NativeArrayXD<Vector3Int> _compositeOffsets;
    private NativeArrayXD<ushort> _wave;
    private NativeArrayXD<ushort> _waveCache;

    private NativeArray<int> _stack;

    private NativeArray<float> _entropy;
    private NativeArray<float> _entropyCache;

    private NativeArray<bool> _collapsed;
    private NativeArray<int> _incompatible;
    private NativeArray<int> _return;
    private NativeArray<int> _collapsedCells;
    private NativeArray<int> _collapsedTiles;

    private Vector3 _startPos;
    private int _seed;
    private Vector3Int _gridSize;

    private readonly ProfilerMarker _profileSetup = new("WFC.Setup");
    private readonly ProfilerMarker _profileReset = new("WFC.Reset");
    private readonly ProfilerMarker _profileRunStep = new("WFC.RunStep");

    private static readonly float WEIGHT_TO_INT = 10000000.0f;
    private static readonly int COMPOSITE_MAX_TILES = 10;
    private static readonly int MAX_COLLAPSES = 20;
    
    private System.Random _rng;

    public void OnDestroy()
    {
        _cellsActive.Dispose();
        _neighbors.Dispose();
        _tiles.Dispose();
        _stack.Dispose();
        _wave.Dispose();
        _waveCache.Dispose();
        _entropy.Dispose();
        _entropyCache.Dispose();
        _collapsed.Dispose();
        _return.Dispose();
        _incompatible.Dispose();
    }

    public void Setup(Grid grid, List<TileDatabase.Tile> tiles, List<TileDatabase.Composite> composites)
    {
        _profileSetup.Begin();

        _gridSize = grid.GridSize;
        C = grid.GridSize.x * grid.GridSize.y * grid.GridSize.z;
        T = tiles.Count;
        D = 6;

        _cellsActive = new NativeArray<ushort>(C, Allocator.Persistent);
        _neighbors = new NativeArrayXD<int>(C, D);
        _tiles = new NativeArrayXD<int>(T, (int) TILE.COUNT);
        _compositeTiles = new NativeArrayXD<int>(composites.Count, COMPOSITE_MAX_TILES);
        _compositeOffsets = new NativeArrayXD<Vector3Int>(composites.Count, COMPOSITE_MAX_TILES);
        _wave = new NativeArrayXD<ushort>(C, T);
        _waveCache = new NativeArrayXD<ushort>(C, T);

        _stack = new NativeArray<int>(C, Allocator.Persistent);

        _entropy = new NativeArray<float>(C, Allocator.Persistent);
        _entropyCache = new NativeArray<float>(C, Allocator.Persistent);
        _collapsed = new NativeArray<bool>(C, Allocator.Persistent);
        _incompatible = new NativeArray<int>(T, Allocator.Persistent);
        _return = new NativeArray<int>((int) StepReturnParams.COUNT, Allocator.Persistent);
        _collapsedCells = new NativeArray<int>(MAX_COLLAPSES, Allocator.Persistent);
        _collapsedTiles = new NativeArray<int>(MAX_COLLAPSES, Allocator.Persistent);
        
        _startPos = new Vector3(0.0f, 10.0f, 0.0f);
        
        // create composite array
        Dictionary<string, int> compositeToIndex = new();
        for (int i = 0; i < composites.Count; i++)
        {
            TileDatabase.Composite composite = composites[i];
            
            for (int t = 0; t < COMPOSITE_MAX_TILES; t++)
            {
                if (t < composite.Tiles.Length)
                {
                    _compositeTiles[i, t] = tiles.IndexOf(composite.Tiles[t]);
                    _compositeOffsets[i,t] = composite.TileOffsets[t];
                }
                else
                {
                    _compositeTiles[i, t] = -1;
                }
            }
            
            compositeToIndex.Add(composite.Name, i);
        }

        // create tile array
        for (int i = 0; i < tiles.Count; i++)
        {
            TileDatabase.Tile tile = tiles[i];

            _tiles[i, (int) TILE.BOT] = tile.Match[4];
            _tiles[i, (int) TILE.TOP] = tile.Match[5];
            _tiles[i, (int) TILE.SIDE_0] = tile.Match[0];
            _tiles[i, (int) TILE.SIDE_1] = tile.Match[1];
            _tiles[i, (int) TILE.SIDE_2] = tile.Match[2];
            _tiles[i, (int) TILE.SIDE_3] = tile.Match[3];
            _tiles[i, (int) TILE.NOT_SIDE_0] = tile.NotMatch[0];
            _tiles[i, (int) TILE.NOT_SIDE_1] = tile.NotMatch[1];
            _tiles[i, (int) TILE.NOT_SIDE_2] = tile.NotMatch[2];
            _tiles[i, (int) TILE.NOT_SIDE_3] = tile.NotMatch[3];
            if (tile.Composite != null && tile.Composite.Name != "")
            {
                _tiles[i, (int) TILE.COMPOSITE] = compositeToIndex[tile.Composite.Name];
                bool found = false;
                for (int j = 0; j < tile.Composite.Tiles.Length; j++)
                {
                    if (tile.Composite.Tiles[j] == tile)
                    {
                        _tiles[i, (int) TILE.INDEX_IN_COMPOSITE] = j;
                        found = true;
                        break;
                    }
                }
                if (!found)
                    Debug.Assert(false, "Is b0rked.");
            }
            else
            {
                _tiles[i, (int) TILE.COMPOSITE] = -1;
            }

            _tiles[i, (int) TILE.WEIGHT] = (int) (1.0f * WEIGHT_TO_INT);
        }

        // create cell arrays
        for (int c = 0; c < C; c++)
        {
            _cellsActive[c] = 0;
            
            Vector3Int cPos = Grid.XYZFromIndex(c, grid.GridSize);
            for (int n = 0; n < D; n++)
            {
                Vector3Int test = cPos + _d[n];
                if (test.x < 0 || test.x > grid.GridSize.x - 1 ||
                    test.y < 0 || test.y > grid.GridSize.y - 1 ||
                    test.z < 0 || test.z > grid.GridSize.z - 1)
                {
                    _neighbors[c, n] = -1;
                    continue;
                }

                _neighbors[c, n] = Grid.IndexFromXYZ(test, grid.GridSize, out bool outOfBounds);
                Debug.Assert(outOfBounds == false, "outOfBounds == false");
            }

            for (int t = 0; t < tiles.Count; t++)
            {
                _wave[c, t] = 1;
                
                // make any composites that go out of bounds invalid.
                // int composite = _tiles[t, (int) TILE.COMPOSITE];
                // if (composite != -1)
                // {
                //     int tileIndexInComposite = _tiles[t, (int) TILE.INDEX_IN_COMPOSITE];
                //     Vector3Int baseOffset = _compositeOffsets[composite, tileIndexInComposite];
                //     Vector3Int cellXyz = Grid.XYZFromIndex(c, _gridSize);
                //
                //     for (int i = 0; i < COMPOSITE_MAX_TILES; i++)
                //     {
                //         int tileInComp = _compositeTiles[composite, i];
                //         if (tileInComp == -1)
                //             break;
                //     
                //         Vector3Int offset = _compositeOffsets[composite, i];
                //         Vector3Int xyz = cellXyz - baseOffset + offset;
                //         int cellInComposite = Grid.IndexFromXYZ(xyz, _gridSize, out bool outOfBounds);
                //         if (outOfBounds)
                //         {
                //             _wave[c, t] = 0;
                //             break;
                //         }
                //     }
                // }
            }

            _entropy[c] = CalcEntropy(c, _wave, _tiles, T, _startPos);
            _collapsed[c] = false;
        }

        unsafe
        {
            void* dst = _waveCache.GetUnsafePtr();
            void* src = _wave.GetUnsafePtr();
            UnsafeUtility.MemCpy(dst, src, sizeof(ushort) * C * T);

            dst = NativeArrayUnsafeUtility.GetUnsafePtr(_entropyCache);
            src = NativeArrayUnsafeUtility.GetUnsafePtr(_entropy);
            UnsafeUtility.MemCpy(dst, src, sizeof(float) * C);
        }

        _seed = System.DateTime.Now.Millisecond;
        Debug.Log($"WFC Seed: {_seed}");
        _seed = 659;
        _rng = new System.Random(_seed);
        
        _profileSetup.End();
    }

    public void Reset()
    {
        _profileReset.Begin();
        unsafe
        {
            void* dst = _wave.GetUnsafePtr();
            void* src = _waveCache.GetUnsafePtr();
            UnsafeUtility.MemCpy(dst, src, sizeof(ushort) * C * T);

            dst = _entropy.GetUnsafePtr();
            src = _entropyCache.GetUnsafePtr();
            UnsafeUtility.MemCpy(dst, src, sizeof(float) * C);

            dst = _collapsed.GetUnsafePtr();
            UnsafeUtility.MemSet(dst, 0, sizeof(bool) * C);
        }
        _profileReset.End();
    }

    public StepResult Step(out List<(int, int)> collapses, out int incompatibleStack, out int incompatibleNeighbor)
    {
        WFCStepJob job = new()
        {
            C = C,
            T = T,
            D = D,
            _neighbors = _neighbors,
            _tiles = _tiles,
            _compositeTiles = _compositeTiles,
            _compositeOffsets = _compositeOffsets,
            _wave = _wave,
            _startPos = _startPos,
            _entropy = _entropy,
            _stack = _stack,
            _collapsed = _collapsed,
            _incompatible = _incompatible,
            _return = _return,
            _collapsedCells = _collapsedCells,
            _collapsedTiles = _collapsedTiles,
            _weightToInt = WEIGHT_TO_INT,
            _seed = _rng.Next(),
            _gridSize = _gridSize
        };

        _profileRunStep.Begin();
        job.Run();
        _profileRunStep.End();
        
        incompatibleStack = _return[(int)StepReturnParams.IncompatibleStack];
        incompatibleNeighbor = _return[(int)StepReturnParams.IncompatibleNeighbor];

        collapses = new List<(int, int)>();
        for (int i = 0; i < _return[(int)StepReturnParams.CollapsedCount]; i++)
        {
            collapses.Add((_collapsedCells[i], _collapsedTiles[i]));
        }

        return (StepResult)_return[(int)StepReturnParams.Result];
    }

    [BurstCompile(CompileSynchronously = true)]
    private struct WFCStepJob : IJob
    {
        [ReadOnly] public int C;
        [ReadOnly] public int T;
        [ReadOnly] public int D;
        [ReadOnly] public NativeArrayXD<int> _neighbors;
        [ReadOnly] public NativeArrayXD<int> _tiles;
        [ReadOnly] public NativeArrayXD<int> _compositeTiles;
        [ReadOnly] public NativeArrayXD<Vector3Int> _compositeOffsets;

        [ReadOnly] public Vector3Int _gridSize;
        [ReadOnly] public Vector3 _startPos;
        [ReadOnly] public int _seed;

        public NativeArrayXD<ushort> _wave;
        public NativeArray<float> _entropy;
        public int _stackSize;
        public NativeArray<int> _stack;
        public NativeArray<bool> _collapsed;
        public NativeArray<int> _incompatible;
        public NativeArray<int> _return;
        public NativeArray<int> _collapsedCells;
        public NativeArray<int> _collapsedTiles;
        public float _weightToInt;

        private Unity.Mathematics.Random _rng;
        private int _collapses;

        public void Execute()
        {
            _rng = new Unity.Mathematics.Random((uint) Mathf.Abs(_seed));

            _collapses = 0;
            StepResult result = Collapse();
            if (result == StepResult.WFCInProgress)
            {
                while (_stackSize > 0)
                {
                    if (Propagate(out int incompatibleStack, out int incompatibleNeighbor)) continue;
                    _return[(int)StepReturnParams.IncompatibleStack] = incompatibleStack;
                    _return[(int)StepReturnParams.IncompatibleNeighbor] = incompatibleNeighbor;
                    result = StepResult.WFCPropagateError;
                    break;
                }
            }

            _return[(int)StepReturnParams.Result] = (int)result;
            _return[(int) StepReturnParams.CollapsedCount] = _collapses;
        }

        private bool Propagate(out int incompatibleStack, out int incompatibleNeighbor)
        {
            int sCell = _stack[_stackSize - 1];
            _stackSize--;

            // For each of the 6 neighboring cells...
            for (int n = 0; n < D; n++)
            {
                int nCell = _neighbors[sCell, n];
                if (nCell == -1)
                    continue;

                int incompatibleCount = 0;

                // for each still possible tile in the neighboring cell...
                for (ushort nt = 0; nt < T; nt++)
                {
                    if (_wave[nCell, nt] == 0)
                        continue;
                    
                    bool compatible = IsAnyTileCompatibleWithCellInDirection(nt, sCell, n);

                    if (compatible) continue;
                    _incompatible[incompatibleCount] = nt;
                    incompatibleCount++;
                }

                if (incompatibleCount <= 0) continue;
                
                for (int i = 0; i < incompatibleCount; i++)
                {
                    Ban(nCell, (ushort)_incompatible[i], true);
                }

                int count = 0;
                for (int i = 0; i < T; i++)
                {
                    if (_wave[nCell, i] == 1)
                        count++;
                }
                if (count == 0)
                {
                    incompatibleStack = sCell;
                    incompatibleNeighbor = nCell;
                    return false;
                }

                _entropy[nCell] = CalcEntropy(nCell, _wave, _tiles, T, _startPos);
                //Debug.Log($"Cell {nCell} entropy: {_entropy[nCell]}");
                _stack[_stackSize] = nCell;
                _stackSize++;
            }
            incompatibleStack = 0;
            incompatibleNeighbor = 0;

            return true;
        }

        private bool IsAnyTileCompatibleWithCellInDirection(int tile, int cell, int dir)
        {
            bool compatible = false;
            
            for (ushort st = 0; st < T; st++)
            {
                if (_wave[cell, st] == 0)
                    continue;

                compatible = dir switch
                {
                    4 => _tiles[st, (int) TILE.TOP] == _tiles[tile, (int) TILE.BOT],
                    5 => _tiles[st, (int) TILE.BOT] == _tiles[tile, (int) TILE.TOP],
                    _ => Compatible(st, tile, dir)
                };

                if (compatible)
                    break;
            }

            return compatible;
        }

        private StepResult Collapse()
        {
            int current = Observe();
            if (current == -1)
            {
                return StepResult.WFCFinished;
            }

            double sumOfWeights = 0.0;
            for (int i = 0; i < T; i++)
            {
                if (_wave[current, i] == 0)
                    continue;

                sumOfWeights += _tiles[i, (int)TILE.WEIGHT] / _weightToInt;
            }

            if (sumOfWeights < (1.0f / _weightToInt))
            {
                _collapsed[current] = true;
                return StepResult.WFCCollapseError;
            }

            int randTile = -1;
            float rng = _rng.NextFloat(0.0f, 1.0f);
            float rnd = rng * (float)sumOfWeights;
            for (int i = 0; i < T; i++)
            {
                if (_wave[current, i] == 0)
                    continue;

                if (rnd < _tiles[i, (int)TILE.WEIGHT] / _weightToInt)
                {
                    randTile = i;
                    break;
                }
                rnd -= _tiles[i, (int)TILE.WEIGHT] / _weightToInt;
            }

            if (randTile == -1)
            {
                Debug.Log("randTile == -1");
                return StepResult.WFCCollapseError;
            }

            // collapse rest of composite if composite
            int composite = _tiles[randTile, (int) TILE.COMPOSITE];
            if (composite != -1)
            {
                int tileIndexInComposite = _tiles[randTile, (int) TILE.INDEX_IN_COMPOSITE];
                Vector3Int baseOffset = _compositeOffsets[composite, tileIndexInComposite];
                Vector3Int cellXyz = Grid.XYZFromIndex(current, _gridSize);
                
                for (int i = 0; i < COMPOSITE_MAX_TILES; i++)
                {
                    int tileInComp = _compositeTiles[composite, i];
                    if (tileInComp == -1)
                        break;
                    
                    Vector3Int offset = _compositeOffsets[composite, i];
                    Vector3Int xyz = cellXyz - baseOffset + offset;
                    int cellInComposite = Grid.IndexFromXYZ(xyz, _gridSize, out bool outOfBounds);
                    if (outOfBounds)
                        continue;
                    
                    CollapseCellToTile(cellInComposite, tileInComp);
                }
            }
            else
            {
                CollapseCellToTile(current, randTile);
            }

            return StepResult.WFCInProgress;
        }

        private void CollapseCellToTile(int cell, int tile)
        {
            if (_collapsed[cell])
            {
                Debug.LogError($"Cell {cell} is already collapsed!");
                return;
            }

            for (int i = 0; i < T; i++)
            {
                if (i == tile)
                    continue;
                
                Ban(cell, (ushort)i, true);
            }
            _collapsed[cell] = true;
            _stack[_stackSize] = cell;
            _stackSize++;
            
            Debug.Log($"Collapsing cell [{cell}] with tile [{tile}]");

            _collapsedCells[_collapses] = cell;
            _collapsedTiles[_collapses] = tile;
            _collapses++;
        }

        private int Observe()
        {
            double lowestEntropy = 9999999.9;
            int lowestIdx = -1;
            for (int i = 0; i < C; i++)
            {
                if (_collapsed[i])
                    continue;

                if (_entropy[i] == 0.0)
                {
                    lowestIdx = i;
                    break;
                }

                if ((_entropy[i] >= lowestEntropy)) continue;
                lowestEntropy = _entropy[i];
                lowestIdx = i;
            }
            return lowestIdx;
        }

        private void Ban(int cell, int tile, bool addToStack = false)
        {
            _wave[cell, tile] = 0;

            //if (cell == 21)
            {
                //Debug.Log($"Banning tile {tile} on cell {cell}");
            }

            // if the tile is composite, ban all other tiles in that composite
            int composite = _tiles[tile, (int) TILE.COMPOSITE];
            if (_tiles[tile, (int) TILE.COMPOSITE] != -1)
            {
                int tileIndexInComposite = _tiles[tile, (int) TILE.INDEX_IN_COMPOSITE];
                Vector3Int baseOffset = _compositeOffsets[composite, tileIndexInComposite];
                Vector3Int cellXyz = Grid.XYZFromIndex(cell, _gridSize);
                for (int i = 0; i < COMPOSITE_MAX_TILES; i++)
                {
                    int tileInComposite = _compositeTiles[composite, i];
                    if (tileInComposite == -1)
                        break;

                    Vector3Int offset = _compositeOffsets[composite, i];
                    int cellInComposite = Grid.IndexFromXYZ(cellXyz - baseOffset + offset, _gridSize, out bool outOfBounds);
                    if (outOfBounds)
                        continue; 
                    
                    if (cellInComposite == cell)
                        continue;

                    //if (cell == 21)
                    {
                        //Debug.Log($"Banning composite tile {tileInComposite} on cell {cellInComposite}");
                    }
                    
                    _wave[cellInComposite, tileInComposite] = 0;

                    if (addToStack)
                    {
                        _stack[_stackSize] = cellInComposite;
                        _stackSize++;
                    }
                }
            }
        }

        private bool Compatible(int tile1, int tile2, int n)
        {
            int sSlot = _tiles[tile1, (int)TILE.SIDE_0 + n];
            int nSlot = _tiles[tile2, (int)TILE.SIDE_0 + (n + 2) % 4];
            int sNotSlot = _tiles[tile1, (int)TILE.NOT_SIDE_0 + n];
            int nNotSlot = _tiles[tile2, (int)TILE.NOT_SIDE_0 + (n + 2) % 4];
            
            return sSlot == nSlot && 
                   (sNotSlot == 0 || nNotSlot == 0 || sNotSlot != nNotSlot);
        }
    }

    private static float CalcEntropy(int cell, NativeArrayXD<ushort> wave, NativeArrayXD<int> tiles, int waveHeight, Vector3 start)
    {
        float sumOfWeights = 0.0f;
        float sumOfWeightsLogWeights = 0.0f;
        for (int i = 0; i < waveHeight; i++)
        {
            if (wave[cell, i] == 0)
                continue;

            float p = tiles[i, (int)TILE.WEIGHT] / WEIGHT_TO_INT;
            sumOfWeights += p;
            sumOfWeightsLogWeights += p * Mathf.Log(p);
        }
        float entropy = Mathf.Log(sumOfWeights) - sumOfWeightsLogWeights / sumOfWeights;
        return entropy;
    }
}