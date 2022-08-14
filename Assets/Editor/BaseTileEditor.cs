using System.Collections;
using System.Collections.Generic;
using Tiles;
using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor(typeof(BaseTile))]
public class BaseTileEditor : UnityEditor.Editor
{
    private SerializedProperty _mesh;
    private SerializedProperty _match;
    
    void OnEnable()
    {
        _mesh = serializedObject.FindProperty("Mesh");
        _match = serializedObject.FindProperty("Match");
    }
    
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        if (GUILayout.Button("Update Name"))
        {
            string path = AssetDatabase.GetAssetPath(serializedObject.targetObject);
            string meshName = _mesh.objectReferenceValue != null ? _mesh.objectReferenceValue.name : "null";
            AssetDatabase.RenameAsset(path, $"{meshName}_{_match.GetArrayElementAtIndex(0).intValue}" +
                                            $"{_match.GetArrayElementAtIndex(1).intValue}" +
                                            $"{_match.GetArrayElementAtIndex(2).intValue}" +
                                            $"{_match.GetArrayElementAtIndex(3).intValue}");
        }
    }
}
