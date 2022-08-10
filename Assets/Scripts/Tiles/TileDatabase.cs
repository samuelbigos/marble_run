using System;
using System.Collections.Generic;
using UnityEngine;
using Utils;
using Color = UnityEngine.Color;

namespace Tiles
{
    public class TileDatabase : MonoBehaviour
    {
        [Serializable]
        public struct Tile
        {
            public string Name;
            public int Size;
            public Mesh Mesh;
            public Material[] Materials;
            public int[] Sides;
            public int Rot;
        }
    
        [SerializeField] public TileInstance TilePrefab;
        [SerializeField] public List<Tile> Tiles;
        [SerializeField] public int TileSize = 2;
    }
}
