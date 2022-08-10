using System.Collections;
using System.Collections.Generic;
using Tiles;
using UnityEngine;

public abstract class WFCBase : MonoBehaviour
{
    public enum StepResult
    {
        WFCInProgress,
        WFCFinished,
        WFCReset,
        WFCBacktrackLimit,
        WFCPropagateError,
        WFCCollapseError
    }
    
    protected Vector3Int[] _d = {
        new(0, 0, 1),
        new(1, 0, 0),
        new(0, 0, -1),
        new(-1, 0, 0),
        new(0, 1, 0),
        new(0, -1, 0)
    };

    public abstract void Setup(Grid grid, List<TileDatabase.Tile> tiles);
    public abstract StepResult Step(out int addedTile, out int tilePrototype, out int incompatibleStack, out int incompatibleNeighbor);
}
