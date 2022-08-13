using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ScriptableObjects/BaseTileComposite", order = 1)]
public class BaseTileComposite : ScriptableObject
{
    [SerializeField] public Vector3Int Size;
    [SerializeField] public BaseTile[] BaseTiles;
    [SerializeField] public Vector3Int[] BaseTileOffsets;
}