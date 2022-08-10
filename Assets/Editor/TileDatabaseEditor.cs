using System.Collections.Generic;
using System.IO;
using MarchingCubesProject;
using Tiles;
using UnityEditor;
using UnityEngine;
using Utils;

namespace Editor
{
    [CustomEditor(typeof(TileDatabase))]
    public class TileDatabaseEditor : UnityEditor.Editor
    {
        private readonly string[] VariantRotations = new string[] 
        {
            "", 
            "Y",
            "YY",
            "YYY",
            
        };

        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("PopulateTiles"))
            {
                TileDatabase t = (target as TileDatabase);
                PopulateTiles(t);
            }
            
            base.OnInspectorGUI();
        }

        private void PopulateTiles(TileDatabase t)
        {
            string[] files = AssetDatabase.FindAssets("t:BaseTile");

            t.Tiles = new List<TileDatabase.Tile>();
            
            for (int c = t.transform.childCount - 1; c >= 0; c--)
            {
                DestroyImmediate(t.transform.GetChild(c).gameObject);
            }

            Vector3 debugSpawnPos = Vector3.zero;
            
            foreach (string guid in files)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                BaseTile baseTile = AssetDatabase.LoadAssetAtPath<BaseTile>(path);

                debugSpawnPos.z += t.TileSize * 2.0f;
                debugSpawnPos.x = 0.0f;
                
                // Add variants of this tile.
                {
                    foreach (string rot in VariantRotations)
                    {
                        TileDatabase.Tile tile = new TileDatabase.Tile();
                        tile.Mesh = baseTile.Mesh;
                        tile.Materials = baseTile.Materials;
                        tile.Sides = new[] { baseTile.Top, baseTile.Right, baseTile.Bottom, baseTile.Left };
                        tile.Size = 1;
                        foreach (char c in rot)
                        {
                            switch (c)
                            {
                                case 'Y':
                                    tile = Rot90_Y(tile);
                                    break;
                            }
                        }
                        tile.Name = $"{baseTile.name}_{tile.Rot}";;
                        t.Tiles.Add(tile);
                        
                        TileInstance tileInstance = Instantiate(t.TilePrefab, t.transform);
                        tileInstance.Init(tile, -1, null);
                        tileInstance.transform.position = debugSpawnPos;
                        tileInstance.name = tile.Name;
                        
                        debugSpawnPos.x += t.TileSize + 2.0f;
                    }
                }
            }
            
            AssetDatabase.SaveAssets();
        }

        private TileDatabase.Tile Rot90_Y(TileDatabase.Tile tile)
        {
            TileDatabase.Tile newTile = tile;
            newTile.Sides = new int[4];
            newTile.Sides[0] = tile.Sides[3];
            newTile.Sides[1] = tile.Sides[0];
            newTile.Sides[2] = tile.Sides[1];
            newTile.Sides[3] = tile.Sides[2];
            newTile.Rot++;
            return newTile;
        }
    }
}
