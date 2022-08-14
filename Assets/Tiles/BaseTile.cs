using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ScriptableObjects/BaseTile", order = 1)]
public class BaseTile : ScriptableObject
{
    [SerializeField] public Mesh Mesh;
    [SerializeField] public Vector3 MeshOffset;
    [SerializeField] public float MeshRotation;
    [SerializeField] public BaseTileComposite Composite;
    [SerializeField] public float Weight = 1.0f;
    [SerializeField] public int[] Match = new int[6];
    [SerializeField] public int[] NotMatch = new int[4];
    [SerializeField] public bool Starter;
    [SerializeField] public bool FlipMaterials = false;
}
