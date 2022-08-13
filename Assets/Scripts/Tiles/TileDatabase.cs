using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Utils;
using Color = UnityEngine.Color;

namespace Tiles
{
    public class TileDatabase : MonoBehaviour
    {
        public static readonly string[] VARIANT_ROTATIONS = new string[] 
        {
            "", 
            "Y",
            "YY",
            "YYY",
        };
        
        public class Tile
        {
            public string Name;
            public int Size;
            public Mesh Mesh;
            public Vector3 MeshOffset;
            public Material[] Materials;
            public int[] Match;
            public int[] NotMatch;
            public int Rot;
            public Composite Composite;
            public int TileIndex;
        }

        public class Composite
        {
            public string Name;
            public Vector3Int Size;
            public Tile[] Tiles;
            public Vector3Int[] TileOffsets;
        }
    
        [SerializeField] public TileInstance TilePrefab;
        [SerializeField] public int TileSize = 2;
        
        [SerializeField] public List<BaseTile> BaseTiles;
        [SerializeField] public List<BaseTileComposite> BaseTileComposites;
        
        public List<Tile> Tiles;
        public List<Composite> Composites;

        public void Init()
        {
            Tiles = new List<Tile>();
            Composites = new List<Composite>();
            
            foreach (BaseTileComposite baseComposite in BaseTileComposites)
            {
                foreach (string rot in VARIANT_ROTATIONS)
                {
                    Composite composite = new Composite();
                    composite.Name = $"{baseComposite.name}_{rot}";
                    composite.Size = baseComposite.Size;
                    composite.Tiles = new Tile[baseComposite.BaseTiles.Length];
                    composite.TileOffsets = baseComposite.BaseTileOffsets;

                    for (int i = 0; i < baseComposite.BaseTiles.Length; i++)
                    {
                        BaseTile baseTile = baseComposite.BaseTiles[i];

                        Tile tile = CreateTileVariant(this, baseTile, rot, Vector3.zero);
                        tile.Composite = composite;
                        Tiles.Add(tile);
                        tile.TileIndex = Tiles.Count - 1;
                        composite.Tiles[i] = tile;
                    }

                    Composites.Add(composite);
                }
            }

            foreach (BaseTile baseTile in BaseTiles)
            {
                if (baseTile.Composite)
                    continue;

                // Add variants of this tile.
                foreach (string rot in VARIANT_ROTATIONS)
                {
                    Tile tile = CreateTileVariant(this, baseTile, rot, Vector3.zero);
                    tile.Composite = null;
                    Tiles.Add(tile);
                    tile.TileIndex = Tiles.Count - 1;
                }
            }
        }
        
        public static Tile CreateTileVariant(TileDatabase t, BaseTile baseTile, string rot, Vector3 debugSpawnPos, bool spawnDebugObj = false)
        {
            Tile tile = new Tile();
            tile.Mesh = baseTile.Mesh;
            tile.MeshOffset = baseTile.MeshOffset;
            tile.Materials = baseTile.Materials;
            tile.Match = new int[6];
            baseTile.Match.CopyTo(tile.Match.AsSpan());
            tile.NotMatch = new int[4];
            baseTile.NotMatch.CopyTo(tile.NotMatch.AsSpan());
            tile.Size = 1;
            foreach (char c in rot)
            {
                switch (c)
                {
                    case 'Y':
                        Rot90_Y(tile);
                        break;
                }
            }
            tile.Name = $"{baseTile.name}_{tile.Rot}";;

            if (spawnDebugObj)
            {
                TileInstance tileInstance = Instantiate(t.TilePrefab, t.transform);
                tileInstance.Init(tile, -1, null, debugSpawnPos);
            }

            return tile;
        }

        public static void Rot90_Y(Tile tile)
        {
            int[] match = new int[6];
            tile.Match.CopyTo(match, 0);
            tile.Match[0] = match[3];
            tile.Match[1] = match[0];
            tile.Match[2] = match[1];
            tile.Match[3] = match[2];
            int[] nots = new int[4];
            tile.NotMatch.CopyTo(nots, 0);
            tile.NotMatch[0] = nots[3];
            tile.NotMatch[1] = nots[0];
            tile.NotMatch[2] = nots[1];
            tile.NotMatch[3] = nots[2];
            tile.Rot++;
        }
    }
}
