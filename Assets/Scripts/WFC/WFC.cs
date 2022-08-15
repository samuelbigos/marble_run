using System;
using System.Collections.Generic;
using System.Diagnostics;
using Tiles;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Profiling;
using Utils;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

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
        MaxStackSize,
        COUNT
    }

    private enum TILE
    {
        TOP,
        BOT,
        SIDE_0, SIDE_1, SIDE_2, SIDE_3,
        NOT_SIDE_0, NOT_SIDE_1, NOT_SIDE_2, NOT_SIDE_3,
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

    private NativeArrayXD<ushort> _neighbors;
    private NativeArrayXD<ushort> _tiles;
    private NativeArray<float> _tileWeights;
    private NativeArrayXD<ushort> _wave;
    private NativeArrayXD<ushort> _waveCache;

    private ushort _stackSize;
    private NativeArray<ushort> _stack;

    private NativeArray<float> _entropy;
    private NativeArray<float> _entropyCache;

    private NativeArray<bool> _collapsed;
    private NativeArray<int> _incompatible;
    private NativeArray<int> _return;
    private NativeArray<int> _collapsedCells;
    private NativeArray<int> _collapsedTiles;

    private int _startCell;
    private int _endCell;
    private int _seed;
    private int _stepCount;
    private Vector3Int _gridSize;

    private readonly ProfilerMarker _profileSetup = new("WFC.Setup");
    private readonly ProfilerMarker _profileReset = new("WFC.Reset");
    private readonly ProfilerMarker _profileRunStep = new("WFC.RunStep");

    private static readonly int MAX_COLLAPSES = 20;
    private static readonly ushort NO_NEIGHBOUR = ushort.MaxValue;
    
    private System.Random _rng;
    private bool _initialised;
    private Stopwatch _stopwatch;

    public void OnDestroy()
    {
        Dispose();
    }

    private void Dispose()
    {
        if (_initialised)
        {
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

        _initialised = false;
    }

    public void Setup(Grid grid, List<TileDatabase.Tile> tiles, Vector3Int startCell, Vector3Int endTile, int seed)
    {
        _profileSetup.Begin();

        _stopwatch = new();
        _stopwatch.Start();
        
        Dispose();

        _startCell = Grid.IndexFromXYZ(startCell, grid.GridSize, out bool _);
        _endCell = Grid.IndexFromXYZ(endTile, grid.GridSize, out bool _);

        _stackSize = 0;
        _gridSize = grid.GridSize;
        C = grid.GridSize.x * grid.GridSize.y * grid.GridSize.z;
        T = tiles.Count;
        D = 6;
        
        Debug.Assert(C < short.MaxValue, "Too many cells for ushort capacity.");

        _neighbors = new NativeArrayXD<ushort>(C, D);
        _tiles = new NativeArrayXD<ushort>(T, (int) TILE.COUNT);
        _tileWeights = new NativeArray<float>(T, Allocator.Persistent);
        _wave = new NativeArrayXD<ushort>(C, T);
        _waveCache = new NativeArrayXD<ushort>(C, T);

        _stack = new NativeArray<ushort>(C + 1, Allocator.Persistent);

        _entropy = new NativeArray<float>(C, Allocator.Persistent);
        _entropyCache = new NativeArray<float>(C, Allocator.Persistent);
        _collapsed = new NativeArray<bool>(C, Allocator.Persistent);
        _incompatible = new NativeArray<int>(T, Allocator.Persistent);
        _return = new NativeArray<int>((int) StepReturnParams.COUNT, Allocator.Persistent);
        _collapsedCells = new NativeArray<int>(MAX_COLLAPSES, Allocator.Persistent);
        _collapsedTiles = new NativeArray<int>(MAX_COLLAPSES, Allocator.Persistent);

        // create tile array
        for (int i = 0; i < tiles.Count; i++)
        {
            TileDatabase.Tile tile = tiles[i];

            _tiles[i, (int) TILE.BOT] = (ushort) tile.Match[4];
            _tiles[i, (int) TILE.TOP] = (ushort) tile.Match[5];
            _tiles[i, (int) TILE.SIDE_0] = (ushort) tile.Match[0];
            _tiles[i, (int) TILE.SIDE_1] = (ushort) tile.Match[1];
            _tiles[i, (int) TILE.SIDE_2] = (ushort) tile.Match[2];
            _tiles[i, (int) TILE.SIDE_3] = (ushort) tile.Match[3];
            _tiles[i, (int) TILE.NOT_SIDE_0] = (ushort) tile.NotMatch[0];
            _tiles[i, (int) TILE.NOT_SIDE_1] = (ushort) tile.NotMatch[1];
            _tiles[i, (int) TILE.NOT_SIDE_2] = (ushort) tile.NotMatch[2];
            _tiles[i, (int) TILE.NOT_SIDE_3] = (ushort) tile.NotMatch[3];

            _tileWeights[i] = tile.Weight;
        }

        // create cell arrays
        for (ushort c = 0; c < C; c++)
        {
            bool didBanTile = false;
            
            _collapsed[c] = false;
            
            Vector3Int cPos = Grid.XYZFromIndex(c, grid.GridSize);
            for (int n = 0; n < D; n++)
            {
                Vector3Int test = cPos + _d[n];
                if (test.x < 0 || test.x > grid.GridSize.x - 1 ||
                    test.y < 0 || test.y > grid.GridSize.y - 1 ||
                    test.z < 0 || test.z > grid.GridSize.z - 1)
                {
                    _neighbors[c, n] = NO_NEIGHBOUR;
                    continue;
                }

                _neighbors[c, n] = (ushort) Grid.IndexFromXYZ(test, grid.GridSize, out bool outOfBounds);
                Debug.Assert(outOfBounds == false, "outOfBounds == false");
            }

            Vector3Int xyz = Grid.XYZFromIndex(c, _gridSize);
            for (ushort t = 0; t < tiles.Count; t++)
            {
                _wave[c, t] = 1;
                
                // ban tiles that are part of a composite that would go off the grid
                if (tiles[t].Composite != null)
                {
                    for (int i = 0; i < tiles[t].Composite.TileOffsets.Length; i++)
                    {
                        Vector3Int xyzAtOffset = Grid.XYZFromIndex(c, _gridSize) 
                            + tiles[t].Composite.TileOffsets[i] - tiles[t].Composite.TileOffsets[tiles[i].IndexInComposite];
                        Grid.IndexFromXYZ(xyzAtOffset, _gridSize, out bool outOfBounds);
                        
                        if (outOfBounds)
                        {
                            BanTileAndComposite(c, t, tiles[t]);
                            break;
                        }
                    }
                }

                // ban tiles that would go off the edge of the grid
                if (xyz.x == 0 && _tiles[t, (int) TILE.SIDE_3] != 0)
                    BanTileAndComposite(c, t, tiles[t]);

                if (xyz.x == grid.GridSize.x - 1 && _tiles[t, (int) TILE.SIDE_1] != 0)
                    BanTileAndComposite(c, t, tiles[t]);

                if (xyz.z == 0 && _tiles[t, (int) TILE.SIDE_2] != 0)
                    BanTileAndComposite(c, t, tiles[t]);

                if (xyz.z == grid.GridSize.z - 1 && _tiles[t, (int) TILE.SIDE_0] != 0)
                    BanTileAndComposite(c, t, tiles[t]);

                // place the starting tile
                if (c == _startCell)
                {
                    if (!tiles[t].Starter)
                    {
                        BanTileAndComposite(c, t, tiles[t]);
                    }
                }
                // place the ending tile
                // if (c == _endCell)
                // {
                //     if (!tiles[t].Ender)
                //     {
                //         didBanTile = true;
                //         _wave[c, t] = 0;
                //     }
                // }

                // prevent any path going off the top of the grid
                if (xyz.y == _gridSize.y - 1 && _tiles[t, (int) TILE.TOP] != 0)
                {
                    BanTileAndComposite(c, t, tiles[t]);
                }

                // prevent any path going off the bottom of the grid
                // if (xyz.y == 0 && SlopesDown(_tiles, t))
                // {
                //     //didBanTile = true;
                //     _wave[c, t] = 0;
                // }
            }

            _entropy[c] = CalcEntropy(c, _wave, _tiles, T, _tileWeights);
            
            if (c == _startCell)
            {
                _entropy[c] = 0.0f;
            }
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

        System.Random rng = new System.Random();
        _seed = seed == -1 ? Mathf.Abs(rng.Next()) : seed;
        _rng = new System.Random(_seed);

        _stopwatch.Stop();
        Debug.Log($"# WFC Setup Completed in {_stopwatch.ElapsedMilliseconds / 1000.0f} seconds.\n" +
                  $"# Seed: {_seed}\n" +
                  $"# Tiles: {T}\n" +
                  $"# Cells: {C}\n");

        _initialised = true;
        
        _profileSetup.End();
    }

    private void BanTileAndComposite(ushort c, ushort t, TileDatabase.Tile tile)
    {
        _wave[c, t] = 0;
        if (!StackContainsCell(_stack, _stackSize, c))
            _stack[_stackSize++] = c;
        
        if (tile.Composite != null)
        {
            for (int i = 0; i < tile.Composite.TileOffsets.Length; i++)
            {
                Vector3Int xyzAtOffset = Grid.XYZFromIndex(c, _gridSize) 
                                         + tile.Composite.TileOffsets[i] - tile.Composite.TileOffsets[tile.IndexInComposite];
                ushort cell = (ushort) Grid.IndexFromXYZ(xyzAtOffset, _gridSize, out bool outOfBounds);
                if (!outOfBounds)
                {
                    _wave[cell, tile.Composite.Tiles[i].TileIndex] = 0;
                    if (!StackContainsCell(_stack, _stackSize, cell))
                        _stack[_stackSize++] = cell;
                }
            }
        }
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
        _stopwatch.Restart();
        
        WFCStepJob job = new()
        {
            C = C,
            T = T,
            D = D,
            _neighbors = _neighbors,
            _tiles = _tiles,
            _tileWeights = _tileWeights,
            _wave = _wave,
            _entropy = _entropy,
            _stackSize = _stackSize,
            _stack = _stack,
            _collapsed = _collapsed,
            _incompatible = _incompatible,
            _return = _return,
            _collapsedCells = _collapsedCells,
            _collapsedTiles = _collapsedTiles,
            _seed = _rng.Next(),
            _startCell = _startCell,
            _stepCount = _stepCount
        };

        _profileRunStep.Begin();
        job.Run();
        _profileRunStep.End();

        _stepCount++;
        _stackSize = 0;
        
        incompatibleStack = _return[(int) StepReturnParams.IncompatibleStack];
        incompatibleNeighbor = _return[(int) StepReturnParams.IncompatibleNeighbor];

        collapses = new List<(int, int)>();
        for (int i = 0; i < _return[(int) StepReturnParams.CollapsedCount]; i++)
        {
            collapses.Add((_collapsedCells[i], _collapsedTiles[i]));
        }
        
        _stopwatch.Stop();
        Debug.Log($"# WFC Step completed in {_stopwatch.ElapsedMilliseconds / 1000.0f} seconds.\n" +
                  $"# Max stack size: {_return[(int) StepReturnParams.MaxStackSize]}\n" +
                  $"# Collapsed tile {_collapsedTiles[0]} in cell {_collapsedCells[0]}\n");

        return (StepResult)_return[(int) StepReturnParams.Result];
    }

    [BurstCompile(CompileSynchronously = true)]
    private struct WFCStepJob : IJob
    {
        [ReadOnly] public int C;
        [ReadOnly] public int T;
        [ReadOnly] public int D;
        [ReadOnly] public NativeArrayXD<ushort> _neighbors;
        [ReadOnly] public NativeArrayXD<ushort> _tiles;
        [ReadOnly] public NativeArray<float> _tileWeights;

        [ReadOnly] public int _startCell;
        [ReadOnly] public int _seed;
        [ReadOnly] public int _stepCount;

        public NativeArrayXD<ushort> _wave;
        public NativeArray<float> _entropy;
        public ushort _stackSize;
        public NativeArray<ushort> _stack;
        public NativeArray<bool> _collapsed;
        public NativeArray<int> _incompatible;
        public NativeArray<int> _return;
        public NativeArray<int> _collapsedCells;
        public NativeArray<int> _collapsedTiles;

        private Unity.Mathematics.Random _rng;
        private int _collapses;

        public void Execute()
        {
            _rng = new Unity.Mathematics.Random((uint) Mathf.Abs(_seed));
            StepResult result = StepResult.WFCInProgress;
            
            // if (_stepCount == 0)
            // {
            //     while (_stackSize > 0)
            //     {
            //         if (Propagate(out int incompatibleStack, out int incompatibleNeighbor)) continue;
            //         _return[(int)StepReturnParams.IncompatibleStack] = incompatibleStack;
            //         _return[(int)StepReturnParams.IncompatibleNeighbor] = incompatibleNeighbor;
            //         result = StepResult.WFCPropagateError;
            //     }
            // }

            int maxStackSize = 0;
            if (result != StepResult.WFCPropagateError)
            {
                _collapses = 0;
                result = Collapse();
                if (result == StepResult.WFCInProgress)
                {
                    while (_stackSize > 0)
                    {
                        maxStackSize = Mathf.Max(_stackSize, maxStackSize);
                        if (Propagate(out int incompatibleStack, out int incompatibleNeighbor)) continue;
                        _return[(int)StepReturnParams.IncompatibleStack] = incompatibleStack;
                        _return[(int)StepReturnParams.IncompatibleNeighbor] = incompatibleNeighbor;
                        result = StepResult.WFCPropagateError;
                        break;
                    }
                }
            }

            _return[(int) StepReturnParams.MaxStackSize] = maxStackSize;
            _return[(int) StepReturnParams.Result] = (int)result;
            _return[(int) StepReturnParams.CollapsedCount] = _collapses;
        }

        private StepResult Collapse()
        {
            int current = Observe();
            if (current == -1)
            {
                return StepResult.WFCFinished;
            }

            float sumOfWeights = 0.0f;
            int randTile = -1;
            
            // hack to allow the first cell to be collapse even though the tile weight is 0.
            if (current == _startCell)
            {
                int count = 0;
                for (int i = 0; i < T; i++)
                {
                    if (_wave[current, i] != 0)
                        count++;
                }
                if (count == 0)
                {
                    _collapsed[current] = true;
                    return StepResult.WFCCollapseError;
                }
                
                float rng = Mathf.Abs(_rng.NextInt()) % count;
                for (int i = 0; i < T; i++)
                {
                    if (_wave[current, i] == 0)
                        continue;

                    if (rng == 0)
                    {
                        randTile = i;
                        break;
                    }
                    rng--;
                }
            }
            else // regular random collapse
            {
                for (int i = 0; i < T; i++)
                {
                    if (_wave[current, i] == 0)
                        continue;

                    sumOfWeights += _tileWeights[i];
                }
                if (sumOfWeights <= 0.0f)
                {
                    _collapsed[current] = true;
                    return StepResult.WFCCollapseError;
                }
                
                float rng = _rng.NextFloat(0.0f, 1.0f);
                float rnd = rng * (float)sumOfWeights;
                for (int i = 0; i < T; i++)
                {
                    if (_wave[current, i] == 0)
                        continue;

                    if (rnd < _tileWeights[i])
                    {
                        randTile = i;
                        break;
                    }
                    rnd -= _tileWeights[i];
                }
            }

            if (randTile == -1)
            {
                Debug.Log("randTile == -1");
                return StepResult.WFCCollapseError;
            }

            Debug.Log($"Collapsing cell: {current} with tile {randTile}");
            CollapseCellToTile((ushort) current, (ushort) randTile);

            return StepResult.WFCInProgress;
        }

        private void CollapseCellToTile(ushort cell, ushort tile)
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
            
            if (!StackContainsCell(_stack, _stackSize, cell))
                _stack[_stackSize++] = cell;
            
            //Debug.Log($"Collapsing cell [{cell}] with tile [{tile}]");

            _collapsedCells[_collapses] = cell;
            _collapsedTiles[_collapses] = tile;
            _collapses++;
        }

        private int Observe()
        {
            float lowestEntropy = float.MaxValue;
            int lowestIdx = -1;
            for (int i = 0; i < C; i++)
            {
                if (_collapsed[i])
                    continue;

                if (_entropy[i] == 0.0f)
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
        
        private bool Propagate(out int incompatibleStack, out int incompatibleNeighbor)
        {
            ushort sCell = _stack[_stackSize - 1];
            _stackSize--;

            // For each of the 6 neighboring cells...
            for (ushort n = 0; n < D; n++)
            {
                ushort nCell = _neighbors[sCell, n];
                if (nCell == NO_NEIGHBOUR)
                    continue;
                
                if (_collapsed[nCell])
                    continue;

                ushort incompatibleCount = 0;

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
                
                for (ushort i = 0; i < incompatibleCount; i++)
                {
                    Ban(nCell, (ushort)_incompatible[i], true);
                }

                float weight = 0.0f;
                for (ushort i = 0; i < T; i++)
                {
                    if (_wave[nCell, i] == 1)
                        weight +=_tileWeights[i];
                }
                if (weight == 0.0f) 
                {
                    incompatibleStack = sCell;
                    incompatibleNeighbor = nCell;
                    return false;
                }

                //_entropy[nCell] = Mathf.Min(_entropy[nCell], CalcEntropy(nCell, _wave, _tiles, T, _collapsed[sCell] && connects));
                _entropy[nCell] = CalcEntropy(nCell, _wave, _tiles, T, _tileWeights);

                if (!StackContainsCell(_stack, _stackSize, nCell))
                {
                    _stack[_stackSize++] = nCell;
                }
            }
            incompatibleStack = 0;
            incompatibleNeighbor = 0;

            return true;
        }
        
        private bool IsAnyTileCompatibleWithCellInDirection(ushort tile, ushort cell, ushort dir)
        {
            bool compatible = false;
            
            for (ushort st = 0; st < T; st++)
            {
                if (_wave[cell, st] == 0)
                    continue;

                switch (dir)
                {
                    case 4:
                        compatible = _tiles[st, (int) TILE.TOP] == _tiles[tile, (int) TILE.BOT];
                        break;
                    case 5:
                        compatible = _tiles[st, (int) TILE.BOT] == _tiles[tile, (int) TILE.TOP];
                        break;
                    default:
                        compatible = Compatible(_tiles, st, tile, dir);
                        break;
                }

                if (compatible)
                    break;
            }

            return compatible;
        }

        private void Ban(int c, int t, bool addToStack = false)
        {
            _wave[c, t] = 0;
        }
    }
    
    private static bool Compatible(NativeArrayXD<ushort> tiles, ushort tile1, ushort tile2, ushort n)
    {
        int sSlot = tiles[tile1, (int)TILE.SIDE_0 + n];
        int nSlot = tiles[tile2, (int)TILE.SIDE_0 + (n + 2) % 4];
        int sNotSlot = tiles[tile1, (int)TILE.NOT_SIDE_0 + n];
        int nNotSlot = tiles[tile2, (int)TILE.NOT_SIDE_0 + (n + 2) % 4];
        return sSlot == nSlot && (sNotSlot == 0 || nNotSlot == 0 || sNotSlot != nNotSlot);
    }

    private static float CalcEntropy(int cell, NativeArrayXD<ushort> wave, NativeArrayXD<ushort> tiles, int tileCount, NativeArray<float> weights)
    {
        float sumOfWeights = 0.0f;
        float sumOfWeightsLogWeights = 0.0f;
        for (int i = 0; i < tileCount; i++)
        {
            if (wave[cell, i] == 0)
                continue;
            
            if (weights[i] == 0.0f)
                continue;

            float p = weights[i];
            sumOfWeights += p;
            sumOfWeightsLogWeights += p * Mathf.Log(p);
        }
        float entropy = Mathf.Log(sumOfWeights) - sumOfWeightsLogWeights / sumOfWeights;
        return entropy;
    }

    private static bool SlopesDown(NativeArrayXD<int> tiles, int tile)
    {
        return tiles[tile, (int) TILE.SIDE_0] == 2 
               || tiles[tile, (int) TILE.SIDE_1] == 2 
               || tiles[tile, (int) TILE.SIDE_2] == 2 
               || tiles[tile, (int) TILE.SIDE_3] == 2 
               || tiles[tile, (int) TILE.BOT] != 0;
    }

    private static bool StackContainsCell(NativeArray<ushort> stack, ushort stackSize, ushort cell)
    {
        for (int i = 0; i < stackSize; i++)
        {
            if (stack[i] == cell)
                return true;
        }

        return false;
    }
}