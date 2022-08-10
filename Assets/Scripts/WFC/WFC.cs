using System.Collections.Generic;
using Tiles;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Profiling;
using Utils;

public class WFC : WFCBase
{
    public enum Entropy
    {
        Distance,
        Shannon
    }

    public enum StepReturnParams
    {
        RetResult,
        RetAddedTile,
        RetAddedPrototype,
        RetBacktracks,
        RetIncompatibleStack,
        RetIncompatibleNeighbor,
        COUNT
    }

    public Entropy EntropyMode = Entropy.Shannon;
    public int BacktrackAttemptCount = 100;

    private enum TILE
    {
        TOP,
        BOT,
        SIDE_0, SIDE_1, SIDE_2, SIDE_3,
        SIDE_0_INV, SIDE_1_INV, SIDE_2_INV, SIDE_3_INV,
        TOP_0, TOP_1, TOP_2, TOP_3,
        BOT_0, BOT_1, BOT_2, BOT_3,
        WEIGHT,
        COUNT
    }

    private int C;
    private int T;
    private int D;
    
    private NativeArray<ushort> _cellsActive;
    private NativeArrayXD<int> _neighbors;
    private NativeArrayXD<uint> _tiles;
    private NativeArrayXD<ushort> _wave;
    private NativeArrayXD<ushort> _waveCache;

    private NativeArray<int> _stack;

    private NativeArray<float> _entropy;
    private NativeArray<float> _entropyCache;

    private NativeArray<bool> _collapsed;
    private NativeArray<int> _incompatible;
    private NativeArray<int> _return;

    private Vector3 _startPos;
    private int _seed;

    private readonly ProfilerMarker _profileSetup = new("WFC.Setup");
    private readonly ProfilerMarker _profileReset = new("WFC.Reset");
    private readonly ProfilerMarker _profileRunStep = new("WFC.RunStep");

    private static readonly float WEIGHT_TO_INT = 10000000.0f;

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

    public override void Setup(Grid grid, List<TileDatabase.Tile> tiles)
    {
        _profileSetup.Begin();

        C = grid.GridSize.x * grid.GridSize.y * grid.GridSize.z;
        T = tiles.Count;
        D = 6;

        _cellsActive = new NativeArray<ushort>(C, Allocator.Persistent);
        _neighbors = new NativeArrayXD<int>(C, D);
        _tiles = new NativeArrayXD<uint>(T, (int) TILE.COUNT);
        _wave = new NativeArrayXD<ushort>(C, T);
        _waveCache = new NativeArrayXD<ushort>(C, T);

        _stack = new NativeArray<int>(C, Allocator.Persistent);

        _entropy = new NativeArray<float>(C, Allocator.Persistent);
        _entropyCache = new NativeArray<float>(C, Allocator.Persistent);
        _collapsed = new NativeArray<bool>(C, Allocator.Persistent);
        _incompatible = new NativeArray<int>(T, Allocator.Persistent);
        _return = new NativeArray<int>((int) StepReturnParams.COUNT, Allocator.Persistent);

        _startPos = new Vector3(0.0f, 10.0f, 0.0f);

        // create prototype array
        for (int i = 0; i < tiles.Count; i++)
        {
            TileDatabase.Tile tile = tiles[i];
            int s = tile.Size - 1;

            _tiles[i, (int) TILE.TOP] = 1;
            _tiles[i, (int) TILE.BOT] = 1;
            _tiles[i, (int) TILE.SIDE_0] = (uint) tile.Sides[0];
            _tiles[i, (int) TILE.SIDE_1] = (uint) tile.Sides[1];
            _tiles[i, (int) TILE.SIDE_2] = (uint) tile.Sides[2];
            _tiles[i, (int) TILE.SIDE_3] = (uint) tile.Sides[3];
            _tiles[i, (int) TILE.SIDE_0_INV] = (uint) tile.Sides[0];
            _tiles[i, (int) TILE.SIDE_1_INV] = (uint) tile.Sides[1];
            _tiles[i, (int) TILE.SIDE_2_INV] = (uint) tile.Sides[2];
            _tiles[i, (int) TILE.SIDE_3_INV] = (uint) tile.Sides[3];

            _tiles[i, (int) TILE.WEIGHT] = (uint) (1.0f * WEIGHT_TO_INT);
        }

        // create cell arrays
        for (int c = 0; c < C; c++)
        {
            _cellsActive[c] = 0;
            
            Vector3Int cPos = grid.XYZFromIndex(c);
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

                _neighbors[c, n] = grid.IndexFromXYZ(test);
            }

            for (int t = 0; t < tiles.Count; t++)
            {
                _wave[c, t] = 1;
            }

            _entropy[c] = CalcEntropy(c, EntropyMode, _wave, _tiles, T, _startPos);
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

        Random.InitState(System.DateTime.Now.Millisecond);
        _seed = Random.Range(int.MinValue, int.MaxValue);
        Debug.Log(_seed);
        //_seed = -550756000;
        
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

    public override StepResult Step(out int addedTile, out int tilePrototype, out int incompatibleStack, out int incompatibleNeighbor)
    {
        WFCStepJob job = new()
        {
            C = C,
            T = T,
            D = D,
            _neighbors = _neighbors,
            _tiles = _tiles,
            _wave = _wave,
            _startPos = _startPos,
            _entropy = _entropy,
            _stack = _stack,
            _collapsed = _collapsed,
            _incompatible = _incompatible,
            _return = _return,
            _weightToInt = WEIGHT_TO_INT,
            _seed = _seed,
        };

        _profileRunStep.Begin();
        job.Run();
        _profileRunStep.End();

        addedTile = _return[(int)StepReturnParams.RetAddedTile];
        tilePrototype = _return[(int)StepReturnParams.RetAddedPrototype];
        incompatibleStack = _return[(int)StepReturnParams.RetIncompatibleStack];
        incompatibleNeighbor = _return[(int)StepReturnParams.RetIncompatibleNeighbor];

        return (StepResult)_return[(int)StepReturnParams.RetResult];
    }

    [BurstCompile(CompileSynchronously = true)]
    private struct WFCStepJob : IJob
    {
        [ReadOnly] public int C;
        [ReadOnly] public int T;
        [ReadOnly] public int D;
        [ReadOnly] public NativeArrayXD<int> _neighbors;
        [ReadOnly] public NativeArrayXD<uint> _tiles;

        [ReadOnly] public Vector3 _startPos;
        [ReadOnly] public int _seed;

        public NativeArrayXD<ushort> _wave;
        public NativeArray<float> _entropy;
        public int _stackSize;
        public NativeArray<int> _stack;
        public NativeArray<bool> _collapsed;
        public NativeArray<int> _incompatible;
        public NativeArray<int> _return;
        public float _weightToInt;
        public Entropy _entropyMode;
        public int _backtracks;

        private Unity.Mathematics.Random _rng;

        public void Execute()
        {
            _rng = new Unity.Mathematics.Random((uint)_seed);

            StepResult result = Collapse(out int addedTile, out int collapsedPrototype);
            if (result == StepResult.WFCInProgress)
            {
                _stackSize = 1;
                while (_stackSize > 0)
                {
                    if (Propagate(out int incompatibleStack, out int incompatibleNeighbor)) continue;
                    _return[(int)StepReturnParams.RetIncompatibleStack] = incompatibleStack;
                    _return[(int)StepReturnParams.RetIncompatibleNeighbor] = incompatibleNeighbor;
                    result = StepResult.WFCPropagateError;
                    break;
                }
            }

            if (result == StepResult.WFCPropagateError)
            {
                _backtracks--;
                result = _backtracks < 0 ? StepResult.WFCBacktrackLimit : StepResult.WFCReset;
            }
            _return[(int)StepReturnParams.RetResult] = (int)result;
            _return[(int)StepReturnParams.RetAddedTile] = addedTile;
            _return[(int)StepReturnParams.RetAddedPrototype] = collapsedPrototype;
            _return[(int)StepReturnParams.RetBacktracks] = _backtracks;
        }

        private bool Propagate(out int incompatibleStack, out int incompatibleNeighbor)
        {
            int sCell = _stack[_stackSize - 1];
            _stackSize--;

            // For each of the 6 neighboring cells...
            for (int n = 0; n < 6; n++)
            {
                int nCell = _neighbors[sCell, n];
                if (nCell == -1)
                    continue;

                int incompatibleCount = 0;

                // for each still possible prototype in the neighboring cell...
                for (ushort nt = 0; nt < T; nt++)
                {
                    if (_wave[nCell, nt] == 0)
                        continue;

                    bool compatible = false;
                    // check that prototype against all possible prototypes in the stack cell...
                    for (ushort st = 0; st < T; st++)
                    {
                        if (_wave[sCell, st] == 0)
                            continue;

                        compatible = n switch
                        {
                            5 => _tiles[st, (int) TILE.BOT] == _tiles[nt, (int) TILE.TOP],
                            4 => _tiles[st, (int) TILE.TOP] == _tiles[nt, (int) TILE.BOT],
                            _ => Compatible(st, nt, n)
                        };

                        if (compatible)
                            break;
                    }

                    if (compatible) continue;
                    _incompatible[incompatibleCount] = nt;
                    incompatibleCount++;
                }

                if (incompatibleCount <= 0) continue;
                
                for (int i = 0; i < incompatibleCount; i++)
                {
                    Ban(nCell, (ushort)_incompatible[i]);
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

                _entropy[nCell] = CalcEntropy(nCell, _entropyMode, _wave, _tiles, T, _startPos);
                _stack[_stackSize] = nCell;
                _stackSize++;
            }
            incompatibleStack = 0;
            incompatibleNeighbor = 0;

            return true;
        }

        private StepResult Collapse(out int addedTile, out int collapsedPrototype)
        {
            addedTile = 0;
            collapsedPrototype = 0;

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

            for (int i = 0; i < T; i++)
            {
                _wave[current, i] = 0;
            }
            _wave[current, randTile] = 1;
            _collapsed[current] = true;
            _stack[_stackSize] = current;
            _stackSize++;

            addedTile = current;
            collapsedPrototype = randTile;

            return StepResult.WFCInProgress;
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

        private void Ban(int cell, ushort prototype)
        {
            _wave[cell, prototype] = 0;
        }

        private bool Compatible(int prot_1, int prot_2, int n)
        {
            uint s_slot = _tiles[prot_1, (int)TILE.SIDE_0 + n];
            uint n_slot = _tiles[prot_2, (int)TILE.SIDE_0_INV + (n + 2) % 4];
            return s_slot == n_slot;
        }
    }

    private static float CalcEntropy(int cell, Entropy mode, NativeArrayXD<ushort> wave, NativeArrayXD<uint> prots, int waveHeight, Vector3 start)
    {
        switch (mode)
        {
            case Entropy.Distance:
            {
                return 0.0f;
                //return Vector3.Distance(cellPositions[cell], start);
            }
            case Entropy.Shannon:
            default:
                {
                    float sumOfWeights = 0.0f;
                    float sumOfWeightsLogWeights = 0.0f;
                    for (int i = 0; i < waveHeight; i++)
                    {
                        if (wave[cell, i] == 0)
                            continue;

                        float p = prots[i, (int)TILE.WEIGHT] / WEIGHT_TO_INT;
                        sumOfWeights += p;
                        sumOfWeightsLogWeights += p * Mathf.Log(p);
                    }
                    float entropy = Mathf.Log(sumOfWeights) - sumOfWeightsLogWeights / sumOfWeights;
                    return entropy;
                }
        }
    }

    private bool TestConstraint(int cellIdx, IReadOnlyList<int> top, IReadOnlyList<int> bot)
    {
        bool match = false;
        for (int p = 0; p < T; p++)
        {
            match |= Match(p, top, bot);
        }

        return match;
    }

    private bool Match(int p, IReadOnlyList<int> top, IReadOnlyList<int> bot)
    {
        if (_tiles[p, (int)TILE.BOT_0] != (ushort)bot[0]) return false;
        if (_tiles[p, (int)TILE.BOT_1] != (ushort)bot[1]) return false;
        if (_tiles[p, (int)TILE.BOT_2] != (ushort)bot[2]) return false;
        if (_tiles[p, (int)TILE.BOT_3] != (ushort)bot[3]) return false;
        if (_tiles[p, (int)TILE.TOP_0] != (ushort)top[0]) return false;
        if (_tiles[p, (int)TILE.TOP_1] != (ushort)top[1]) return false;
        if (_tiles[p, (int)TILE.TOP_2] != (ushort)top[2]) return false;
        return _tiles[p, (int)TILE.TOP_3] == (ushort)top[3];
    }
}