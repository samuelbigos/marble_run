using System;
using System.Collections;
using System.Collections.Generic;
using AStar;
using UnityEngine;

public class PathConstraint
{
    private static System.Random _rng;
    
    private static List<Location> _banned = new();
    private static List<Location> _forced = new();
    private static List<Location> _open = new();
    
    // https://www.boristhebrave.com/2022/03/20/chiseled-paths-revisited/
    public static List<Location> FindPath(Vector3Int gridSize, Vector3Int start, Vector3Int end, int seed)
    {
        _rng = new System.Random(seed);
        
        for (int x = 0; x < gridSize.x; x++)
        {
            for (int y = 0; y < gridSize.y; y++)
            {
                for (int z = 0; z < gridSize.z; z++)
                {
                    _open.Add(new Location(new Vector3Int(x, y, z)));
                }
            }
        }
        
        Grid3D grid = new Grid3D(gridSize);
        Location belowStart = new Location(start + Vector3Int.down);
        Location aboveEnd = new Location(end + Vector3Int.up);
        if (belowStart.Pos.y >= 0)
        {
            grid.Ban(belowStart);
            CellToBanned(belowStart);
        }
        
        if (aboveEnd.Pos.y < gridSize.y)
        {
            grid.Ban(aboveEnd);
            CellToBanned(aboveEnd);
        }

        Location startLoc = new Location(start);
        Location endLoc = new Location(end);
        CellToForced(startLoc);
        CellToForced(endLoc);
        
        AStarSearch search = new AStarSearch();
        List<Location> witness = search.FindPath(grid, new Location(start), new Location(end));
        if (witness == null)
            return null;
        
        while (true)
        {
            if (_open.Count == 0)
                break;

            Location banLoc = _open[_rng.Next() % _open.Count];
            grid.Ban(banLoc);

            if (witness.Contains(banLoc))
            {
                List<Location> newWitness = search.FindPath(grid, new Location(start), new Location(end));
                if (newWitness == null)
                {
                    grid.Unban(banLoc);
                    CellToForced(banLoc);
                }
                else
                {
                    witness = newWitness;
                    CellToBanned(banLoc);
                }
            }
            else
            {
                CellToBanned(banLoc);
            }
        }

        return witness;
    }

    private static void CellToBanned(Location cell)
    {
        _open.Remove(cell);
        _banned.Add(cell);
    }

    private static void CellToForced(Location cell)
    {
        _open.Remove(cell);
        _forced.Add(cell);
    }
}
