using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Utils;

namespace WFC
{
    [BurstCompile(CompileSynchronously = true)]
    public struct WfcStepJob : IJob
    {
        public enum StepReturnParams
        {
            Result,
            CollapsedCell,
            CollapsedTile,
            IncompatibleStack,
            IncompatibleNeighbor,
            MaxStackSize,
            COUNT
        }
        
        [ReadOnly] public int C;
        [ReadOnly] public int T;
        [ReadOnly] public int D;
        [ReadOnly] public NativeArrayXD<ushort> _neighbors;
        [ReadOnly] public NativeArrayXD<ushort> _tiles;
        [ReadOnly] public NativeArray<float> _tileWeights;

        [ReadOnly] public NativeArray<int> _startCells;
        [ReadOnly] public int _seed;
        [ReadOnly] public int _stepCount;

        public NativeArrayXD<bool> _wave;
        public NativeArray<float> _entropy;
        public ushort _stackSize;
        public NativeArray<ushort> _stack;
        public NativeArray<bool> _collapsed;
        public NativeArray<int> _incompatible;
        public NativeArray<int> _return;

        private Unity.Mathematics.Random _rng;

        public const ushort NO_NEIGHBOUR = ushort.MaxValue;

        public void Execute()
        {
            _rng = new Unity.Mathematics.Random((uint) Mathf.Abs(_seed));
            StepResult result = StepResult.InProgress;

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
            if (result != StepResult.PropagateError)
            {
                result = Collapse();
                if (result == StepResult.InProgress)
                {
                    while (_stackSize > 0)
                    {
                        maxStackSize = Mathf.Max(_stackSize, maxStackSize);
                        if (Propagate(out int incompatibleStack, out int incompatibleNeighbor)) continue;
                        _return[(int) StepReturnParams.IncompatibleStack] = incompatibleStack;
                        _return[(int) StepReturnParams.IncompatibleNeighbor] = incompatibleNeighbor;
                        result = StepResult.PropagateError;
                        break;
                    }
                }
            }

            _return[(int) StepReturnParams.MaxStackSize] = maxStackSize;
            _return[(int) StepReturnParams.Result] = (int) result;
        }

        private StepResult Collapse()
        {
            int current = Observe();
            if (current == -1)
            {
                return StepResult.Finished;
            }

            float sumOfWeights = 0.0f;
            int randTile = -1;

            // hack to allow the first cell to be collapse even though the tile weight is 0.
            bool isStartCell = false;
            for (int i = 0; i < _startCells.Length; i++)
            {
                if (_startCells[i] == current)
                    isStartCell = true;
            }
            if (isStartCell)
            {
                int count = 0;
                for (int i = 0; i < T; i++)
                {
                    if (_wave[current, i] != false)
                        count++;
                }

                if (count == 0)
                {
                    _collapsed[current] = true;
                    return StepResult.CollapseError;
                }

                float rng = Mathf.Abs(_rng.NextInt()) % count;
                for (int i = 0; i < T; i++)
                {
                    if (_wave[current, i] == false)
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
                    if (_wave[current, i] == false)
                        continue;

                    sumOfWeights += _tileWeights[i];
                }

                if (sumOfWeights <= 0.0f)
                {
                    _collapsed[current] = true;
                    return StepResult.CollapseError;
                }

                float rng = _rng.NextFloat(0.0f, 1.0f);
                float rnd = rng * (float) sumOfWeights;
                for (int i = 0; i < T; i++)
                {
                    if (_wave[current, i] == false)
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
                //Debug.Log("randTile == -1");
                return StepResult.CollapseError;
            }

            //Debug.Log($"Collapsing cell: {current} with tile {randTile}");
            CollapseCellToTile((ushort) current, (ushort) randTile);

            return StepResult.InProgress;
        }

        private void CollapseCellToTile(ushort cell, ushort tile)
        {
            if (_collapsed[cell])
            {
                //Debug.LogError($"Cell {cell} is already collapsed!");
                return;
            }

            for (int i = 0; i < T; i++)
            {
                if (i == tile)
                    continue;

                _wave[cell, i] = false;
            }

            _collapsed[cell] = true;

            if (!Wfc.StackContainsCell(_stack, _stackSize, cell))
                _stack[_stackSize++] = cell;

            //Debug.Log($"Collapsing cell [{cell}] with tile [{tile}]");
            _return[(int) StepReturnParams.CollapsedCell] = cell;
            _return[(int) StepReturnParams.CollapsedTile] = tile;
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
                    if (_wave[nCell, nt] == false)
                        continue;

                    bool compatible = IsAnyTileCompatibleWithCellInDirection(nt, sCell, n);
                    
                    if (compatible) continue;
                    _incompatible[incompatibleCount] = nt;
                    incompatibleCount++;
                }

                if (incompatibleCount <= 0) continue;
                
                for (ushort i = 0; i < incompatibleCount; i++)
                {
                    _wave[nCell, _incompatible[i]] = false;
                }

#if UNITY_EDITOR
                // float weight = 0.0f;
                // for (ushort i = 0; i < T; i++)
                // {
                //     if (_wave[nCell, i] == 1)
                //         weight += _tileWeights[i];
                // }
                //
                // if (weight == 0.0f)
                // {
                //     incompatibleStack = sCell;
                //     incompatibleNeighbor = nCell;
                //     return false;
                // }
#endif

                //_entropy[nCell] = Mathf.Min(_entropy[nCell], CalcEntropy(nCell, _wave, _tiles, T, _collapsed[sCell] && connects));
                _entropy[nCell] = Wfc.CalcEntropy(nCell, _wave, _tiles, T, _tileWeights);

                if (!Wfc.StackContainsCell(_stack, _stackSize, nCell))
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
                if (_wave[cell, st] == false)
                    continue;

                switch (dir)
                {
                    case 4:
                        compatible = _tiles[st, (int) Wfc.TILE.TOP] == _tiles[tile, (int) Wfc.TILE.BOT];
                        break;
                    case 5:
                        compatible = _tiles[st, (int) Wfc.TILE.BOT] == _tiles[tile, (int) Wfc.TILE.TOP];
                        break;
                    default:
                        compatible = Wfc.Compatible(_tiles, st, tile, dir);
                        break;
                }

                if (compatible)
                    break;
            }

            return compatible;
        }
    }
}