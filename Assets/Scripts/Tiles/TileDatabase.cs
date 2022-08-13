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
            public int[] Sides;
            public int Rot;
            public Composite Composite;
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
                }
            }
        }
        
        public static Tile CreateTileVariant(TileDatabase t, BaseTile baseTile, string rot, Vector3 debugSpawnPos, bool spawnDebugObj = false)
        {
            Tile tile = new Tile();
            tile.Mesh = baseTile.Mesh;
            tile.MeshOffset = baseTile.MeshOffset;
            tile.Materials = baseTile.Materials;
            tile.Sides = new[] { baseTile.Top, baseTile.Right, baseTile.Bottom, baseTile.Left };
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
            int[] sides = new int[4];
            tile.Sides.CopyTo(sides, 0);
            tile.Sides[0] = sides[3];
            tile.Sides[1] = sides[0];
            tile.Sides[2] = sides[1];
            tile.Sides[3] = sides[2];
            tile.Rot++;
        }
    }
}
