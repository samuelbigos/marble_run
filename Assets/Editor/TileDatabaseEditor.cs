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
            t.BaseTiles = new List<BaseTile>();
            t.BaseTileComposites = new List<BaseTileComposite>();
            t.Tiles = new List<TileDatabase.Tile>();
            t.Composites = new List<TileDatabase.Composite>();
            
            for (int c = t.transform.childCount - 1; c >= 0; c--)
            {
                DestroyImmediate(t.transform.GetChild(c).gameObject);
            }

            Vector3 debugSpawnPos = Vector3.zero;
            
            string[] compositeGuids = AssetDatabase.FindAssets("t:BaseTileComposite");
            foreach (string guid in compositeGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                BaseTileComposite baseComposite = AssetDatabase.LoadAssetAtPath<BaseTileComposite>(path);

                foreach (string rot in TileDatabase.VARIANT_ROTATIONS)
                {
                    TileDatabase.Composite composite = new TileDatabase.Composite();
                    composite.Name = $"{baseComposite.name}_{rot}";
                    composite.Size = baseComposite.Size;
                    composite.Tiles = new TileDatabase.Tile[baseComposite.BaseTiles.Length];
                    composite.TileOffsets = baseComposite.BaseTileOffsets;

                    for (int i = 0; i < baseComposite.BaseTiles.Length; i++)
                    {
                        BaseTile baseTile = baseComposite.BaseTiles[i];

                        TileDatabase.Tile tile = TileDatabase.CreateTileVariant(t, baseTile, rot, 
                            debugSpawnPos + composite.TileOffsets[i] * t.TileSize, true);
                        tile.Composite = composite;
                        t.Tiles.Add(tile);
                        tile.TileIndex = t.Tiles.Count - 1;
                        composite.Tiles[i] = tile;
                    }

                    debugSpawnPos.x += t.TileSize + 2.0f;

                    t.Composites.Add(composite);
                }
                
                t.BaseTileComposites.Add(baseComposite);
            }

            string[] baseTileGuids = AssetDatabase.FindAssets("t:BaseTile");
            foreach (string guid in baseTileGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                BaseTile baseTile = AssetDatabase.LoadAssetAtPath<BaseTile>(path);
                t.BaseTiles.Add(baseTile);
                
                if (baseTile.Composite)
                    continue;
                
                debugSpawnPos.z += t.TileSize * 2.0f;
                debugSpawnPos.x = 0.0f;

                // Add variants of this tile.
                foreach (string rot in TileDatabase.VARIANT_ROTATIONS)
                {
                    TileDatabase.Tile tile = TileDatabase.CreateTileVariant(t, baseTile, rot, debugSpawnPos, true);
                    tile.Composite = null;
                    t.Tiles.Add(tile);
                    tile.TileIndex = t.Tiles.Count - 1;
                    debugSpawnPos.x += t.TileSize + 2.0f;
                }
            }
            
            AssetDatabase.SaveAssets();
        }
    }
}
