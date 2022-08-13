using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CanEditMultipleObjects]
[CustomEditor(typeof(BaseTile))]
public class BaseTileEditor : UnityEditor.Editor
{
    private SerializedProperty _mesh;
    private SerializedProperty _top;
    private SerializedProperty _right;
    private SerializedProperty _bottom;
    private SerializedProperty _left;
    
    void OnEnable()
    {
        _mesh = serializedObject.FindProperty("Mesh");
        _top = serializedObject.FindProperty("Top");
        _right = serializedObject.FindProperty("Right");
        _bottom = serializedObject.FindProperty("Bottom");
        _left = serializedObject.FindProperty("Left");
    }
    
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        if (GUILayout.Button("Update Name"))
        {
            string path = AssetDatabase.GetAssetPath(serializedObject.targetObject);
            string meshName = _mesh.objectReferenceValue != null ? _mesh.objectReferenceValue.name : "null";
            AssetDatabase.RenameAsset(path, $"{meshName}_{_top.intValue}{_right.intValue}{_bottom.intValue}{_left.intValue}");
        }
    }
}
