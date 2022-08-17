using System.Collections.Generic;
using Tiles;
using UnityEngine;

namespace WFC
{
    public enum StepResult
    {
        InProgress,
        Finished,
        PropagateError,
        CollapseError
    }
    
   public abstract class WfcBase : MonoBehaviour
   {
       public abstract void Setup(Grid grid, List<TileDatabase.Tile> tileDatabaseTiles, Vector3Int start, Vector3Int end, int seed);
       public abstract StepResult Step(out List<(int, int)> valueTuples, out int i, out int i1);
       public abstract void Reset();
   } 
}
