using System.Collections.Generic;
using System.Diagnostics;
using Tiles;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using Utils;
using Debug = UnityEngine.Debug;

namespace WFC
{
    public class Wfc : WfcBase
    { 
        internal enum TILE
        {
            TOP,
            BOT,
            SIDE_0,
            SIDE_1,
            SIDE_2,
            SIDE_3,
            NOT_SIDE_0,
            NOT_SIDE_1,
            NOT_SIDE_2,
            NOT_SIDE_3,
            COUNT
        }

        private Vector3Int[] _d =
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
        private NativeArrayXD<bool> _wave;
        private NativeArrayXD<bool> _waveCache;

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

        public override void Setup(Grid grid, List<TileDatabase.Tile> tiles, Vector3Int startCell, Vector3Int endTile,
            int seed)
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
            _wave = new NativeArrayXD<bool>(C, T);
            _waveCache = new NativeArrayXD<bool>(C, T);

            _stack = new NativeArray<ushort>(C + 1, Allocator.Persistent);

            _entropy = new NativeArray<float>(C, Allocator.Persistent);
            _entropyCache = new NativeArray<float>(C, Allocator.Persistent);
            _collapsed = new NativeArray<bool>(C, Allocator.Persistent);
            _incompatible = new NativeArray<int>(T, Allocator.Persistent);
            _return = new NativeArray<int>((int) WfcStepJob.StepReturnParams.COUNT, Allocator.Persistent);

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
                _collapsed[c] = false;

                Vector3Int cPos = Grid.XYZFromIndex(c, grid.GridSize);
                for (int n = 0; n < D; n++)
                {
                    Vector3Int test = cPos + _d[n];
                    if (test.x < 0 || test.x > grid.GridSize.x - 1 ||
                        test.y < 0 || test.y > grid.GridSize.y - 1 ||
                        test.z < 0 || test.z > grid.GridSize.z - 1)
                    {
                        _neighbors[c, n] = WfcStepJob.NO_NEIGHBOUR;
                        continue;
                    }

                    _neighbors[c, n] = (ushort) Grid.IndexFromXYZ(test, grid.GridSize, out bool outOfBounds);
                    Debug.Assert(outOfBounds == false, "outOfBounds == false");
                }

                Vector3Int xyz = Grid.XYZFromIndex(c, _gridSize);
                for (ushort t = 0; t < tiles.Count; t++)
                {
                    _wave[c, t] = true;

                    // ban tiles that are part of a composite that would go off the grid
                    if (tiles[t].Composite != null)
                    {
                        for (int i = 0; i < tiles[t].Composite.TileOffsets.Length; i++)
                        {
                            Vector3Int xyzAtOffset = Grid.XYZFromIndex(c, _gridSize)
                                                     + tiles[t].Composite.TileOffsets[i] -
                                                     tiles[t].Composite.TileOffsets[tiles[t].IndexInComposite];
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
                UnsafeUtility.MemCpy(dst, src, sizeof(bool) * C * T);

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
            _wave[c, t] = false;
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
                        _wave[cell, tile.Composite.Tiles[i].TileIndex] = false;
                        if (!StackContainsCell(_stack, _stackSize, cell))
                            _stack[_stackSize++] = cell;
                    }
                }
            }
        }

        public override void Reset()
        {
            _profileReset.Begin();
            unsafe
            {
                void* dst = _wave.GetUnsafePtr();
                void* src = _waveCache.GetUnsafePtr();
                UnsafeUtility.MemCpy(dst, src, sizeof(bool) * C * T);

                dst = _entropy.GetUnsafePtr();
                src = _entropyCache.GetUnsafePtr();
                UnsafeUtility.MemCpy(dst, src, sizeof(float) * C);

                dst = _collapsed.GetUnsafePtr();
                UnsafeUtility.MemSet(dst, 0, sizeof(bool) * C);
            }

            _profileReset.End();
        }

        public override StepResult Step(out List<(int, int)> collapses, out int incompatibleStack,
            out int incompatibleNeighbor)
        {
            _stopwatch.Restart();

            WfcStepJob job = new()
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
                _seed = _rng.Next(),
                _startCell = _startCell,
                _stepCount = _stepCount
            };

            _profileRunStep.Begin();
            job.Run();
            _profileRunStep.End();

            _stepCount++;
            _stackSize = 0;

            incompatibleStack = _return[(int) WfcStepJob.StepReturnParams.IncompatibleStack];
            incompatibleNeighbor = _return[(int) WfcStepJob.StepReturnParams.IncompatibleNeighbor];

            collapses = new()
            {
                (_return[(int) WfcStepJob.StepReturnParams.CollapsedCell], _return[(int) WfcStepJob.StepReturnParams.CollapsedTile])
            };

            _stopwatch.Stop();
            // Debug.Log($"# WFC Step completed in {_stopwatch.ElapsedMilliseconds / 1000.0f} seconds.\n" +
            //           $"# Max stack size: {_return[(int) StepReturnParams.MaxStackSize]}\n" +
            //           $"# Collapsed tile {_collapsedTiles[0]} in cell {_collapsedCells[0]}\n");

            return (StepResult) _return[(int) WfcStepJob.StepReturnParams.Result];
        }

        public static bool Compatible(NativeArrayXD<ushort> tiles, ushort tile1, ushort tile2, ushort n)
        {
            int sSlot = tiles[tile1, (int) TILE.SIDE_0 + n];
            int nSlot = tiles[tile2, (int) TILE.SIDE_0 + (n + 2) % 4];
            int sNotSlot = tiles[tile1, (int) TILE.NOT_SIDE_0 + n];
            int nNotSlot = tiles[tile2, (int) TILE.NOT_SIDE_0 + (n + 2) % 4];
            return sSlot == nSlot && (sNotSlot == 0 || nNotSlot == 0 || sNotSlot != nNotSlot);
        }

        public static float CalcEntropy(int cell, NativeArrayXD<bool> wave, NativeArrayXD<ushort> tiles,
            int tileCount, NativeArray<float> weights)
        {
            float sumOfWeights = 0.0f;
            float sumOfWeightsLogWeights = 0.0f;
            for (int i = 0; i < tileCount; i++)
            {
                if (wave[cell, i] == false)
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

        public static bool StackContainsCell(NativeArray<ushort> stack, ushort stackSize, ushort cell)
        {
            int x = 0;
            for (int i = 0; i < stackSize; i++)
            {
                if (stack[i] == cell)
                    x++;
            }

            return x > 0;
        }
    }
}