using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tiles
{
    [CreateAssetMenu(menuName = "ScriptableObjects/BaseTileComposite", order = 1)]
    public class BaseTileComposite : ScriptableObject
    {
        [SerializeField] public BaseTile[] BaseTiles;
        [SerializeField] public Vector3Int[] BaseTileOffsets;
        [SerializeField] public bool Disabled;
    }
}