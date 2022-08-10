using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ScriptableObjects/BaseTile", order = 1)]
public class BaseTile : ScriptableObject
{
    [SerializeField] public Mesh Mesh;
    [SerializeField] public Material[] Materials;
    [SerializeField] public int Top;
    [SerializeField] public int Right;
    [SerializeField] public int Bottom;
    [SerializeField] public int Left;
}
