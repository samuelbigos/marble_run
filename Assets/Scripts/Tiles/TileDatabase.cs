using System;
using System.Collections.Generic;
using UnityEngine;

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
            public float MeshRotation;
            public Material[] Materials;
            public int[] Match;
            public int[] NotMatch;
            public int Rot;
            public float Weight;
            public bool Starter;
            public bool Ender;
            public Composite Composite;
            public int IndexInComposite;
            public int TileIndex;
        }

        public class Composite
        {
            public string Name;
            public Tile[] Tiles;
            public Vector3Int[] TileOffsets;
        }
    
        [SerializeField] public TileInstance TilePrefab;
        [SerializeField] public int TileSize = 2;
        
        [SerializeField] public List<BaseTile> BaseTiles;
        [SerializeField] public List<BaseTileComposite> BaseTileComposites;

        [SerializeField] private Material _matInner;
        [SerializeField] private Material _matOuter;
        
        public List<Tile> Tiles;
        public List<Composite> Composites;

        private int _idGen;

        public void Init(bool spawnDebugTiles = false)
        {
            _idGen = 1000;
            
            Tiles = new List<Tile>();
            Composites = new List<Composite>();
            
            Vector3 debugSpawnPos = Vector3.zero;
            
            // composite tiles
            foreach (BaseTileComposite baseComposite in BaseTileComposites)
            {
                GameObject compositeObj = null;
                if (spawnDebugTiles)
                {
                    compositeObj = new GameObject(baseComposite.name);
                    compositeObj.transform.parent = transform;
                }

                for (int r = 0; r < VARIANT_ROTATIONS.Length; r++)
                {
                    string rot = VARIANT_ROTATIONS[r];
                    Composite composite = new Composite();
                    composite.Name = $"{baseComposite.name}_{rot}";
                    composite.Tiles = new Tile[baseComposite.BaseTiles.Length];
                    composite.TileOffsets = new Vector3Int[baseComposite.BaseTileOffsets.Length];
                    baseComposite.BaseTileOffsets.CopyTo(composite.TileOffsets, 0);

                    foreach (char c in rot)
                    {
                        switch (c)
                        {
                            case 'Y':
                            {
                                for (int i = 0; i < composite.TileOffsets.Length; i++)
                                {
                                    composite.TileOffsets[i] = Rot90(composite.TileOffsets[i]);
                                }

                                break;
                            }
                        }
                    }

                    GameObject rotationObj = null;
                    if (spawnDebugTiles)
                    {
                        rotationObj = new GameObject(composite.Name);
                        rotationObj.transform.parent = compositeObj.transform;
                    }

                    for (int i = 0; i < baseComposite.BaseTiles.Length; i++)
                    {
                        BaseTile baseTile = baseComposite.BaseTiles[i];

                        Tile tile = CreateTileVariant(this, baseTile, rot);
                        tile.Composite = composite;
                        tile.IndexInComposite = i;
                        Tiles.Add(tile);
                        tile.TileIndex = Tiles.Count - 1;
                        composite.Tiles[i] = tile;
                    }

                    List<Vector3Int> d = new()
                    {
                        new(0, 0, -1),
                        new(-1, 0, 0),
                        new(0, 0, 1),
                        new(1, 0, 0),
                        new(0, 1, 0),
                        new(0, -1, 0)
                    };

                    // for each tile pair in the composite, generate a unique matching value.
                    for (int i = 0; i < composite.Tiles.Length; i++)
                    {
                        for (int j = 0; j < composite.Tiles.Length; j++)
                        {
                            if (i == j)
                                continue;

                            // if adjacent
                            Vector3Int delta = composite.TileOffsets[i] - composite.TileOffsets[j];
                            if (d.Contains(delta))
                            {
                                int dIndex = d.IndexOf(delta);
                                int dInvIndex = d.IndexOf(-delta);
                                Tile t1 = composite.Tiles[i];
                                Tile t2 = composite.Tiles[j];
                                t1.Match[dIndex] = GetUnique();
                                t2.Match[dInvIndex] = t1.Match[dIndex];
                            }
                        }
                    }

                    if (spawnDebugTiles)
                    {
                        Vector3 centre = Vector3.zero;
                        for (int i = 0; i < composite.Tiles.Length; i++)
                        {
                            centre += composite.TileOffsets[i];
                        }
                        centre /= composite.Tiles.Length;

                        for (int i = 0; i < composite.Tiles.Length; i++)
                        {
                            Vector3 pos = debugSpawnPos + (composite.TileOffsets[i]) * TileSize;;
                            pos -= centre - composite.TileOffsets[0];
                            TileInstance tileInstance = Instantiate(TilePrefab, transform);
                            tileInstance.Init(composite.Tiles[i], Tiles.IndexOf(composite.Tiles[i]), null, pos, rotationObj.transform);
                        }
                        
                        debugSpawnPos.x += TileSize * 4.0f;
                    }

                    Composites.Add(composite);
                }

                debugSpawnPos.z += TileSize * 4.0f;
                debugSpawnPos.x = 0.0f;
            }

            // standard tiles
            foreach (BaseTile baseTile in BaseTiles)
            {
                if (baseTile.Composite)
                    continue;
                
                GameObject rotationObj = null;
                if (spawnDebugTiles)
                {
                    rotationObj = new GameObject(baseTile.name);
                    rotationObj.transform.parent = transform;
                }

                // Add variants of this tile.
                foreach (string rot in VARIANT_ROTATIONS)
                {
                    Tile tile = CreateTileVariant(this, baseTile, rot);
                    tile.Composite = null;
                    Tiles.Add(tile);
                    tile.TileIndex = Tiles.Count - 1;
                    
                    if (spawnDebugTiles)
                    {
                        TileInstance tileInstance = Instantiate(TilePrefab, transform);
                        tileInstance.Init(tile, Tiles.IndexOf(tile), null, debugSpawnPos, rotationObj.transform);
                    }
                    
                    debugSpawnPos.x += TileSize * 2.0f;
                }
                
                debugSpawnPos.z += TileSize * 2.0f;
                debugSpawnPos.x = 0.0f;
            }
        }

        private Vector3Int Rot90(Vector3Int vec)
        {
            return new Vector3Int(vec.z, vec.y, -vec.x);
        }
        
        private static Tile CreateTileVariant(TileDatabase t, BaseTile baseTile, string rot)
        {
            Tile tile = new()
            {
                Mesh = baseTile.Mesh,
                MeshOffset = baseTile.MeshOffset,
                MeshRotation = baseTile.MeshRotation,
                Materials = new[] {t._matOuter, t._matInner},
                Weight = baseTile.Weight,
                Starter = baseTile.Starter,
                Ender = baseTile.Ender,
                Match = new int[6]
            };
            if (baseTile.Mesh == null)
                tile.Mesh = null;
            
            if (baseTile.FlipMaterials)
            {
                tile.Materials = new[] {tile.Materials[1], tile.Materials[0]};
            }
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

            return tile;
        }

        private static void Rot90_Y(Tile tile)
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

        private int GetUnique()
        {
            return _idGen++;
        }
    }
}
