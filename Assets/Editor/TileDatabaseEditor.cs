using System.Collections.Generic;
using System.IO;
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
            for (int c = t.transform.childCount - 1; c >= 0; c--)
            {
                DestroyImmediate(t.transform.GetChild(c).gameObject);
            }
            
            t.BaseTileComposites.Clear();
            t.BaseTiles.Clear();
            
            string[] compositeGuids = AssetDatabase.FindAssets("t:BaseTileComposite");
            foreach (string guid in compositeGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                BaseTileComposite baseComposite = AssetDatabase.LoadAssetAtPath<BaseTileComposite>(path);
                if (!baseComposite.Disabled)
                {
                    t.BaseTileComposites.Add(baseComposite);
                }
            }
            
            string[] tileGuids = AssetDatabase.FindAssets("t:BaseTile");
            foreach (string guid in tileGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                BaseTile baseTile = AssetDatabase.LoadAssetAtPath<BaseTile>(path);
                if (!baseTile.Disabled)
                {
                    t.BaseTiles.Add(baseTile);
                }
            }

            t.Init(true);
        }
    }
}
