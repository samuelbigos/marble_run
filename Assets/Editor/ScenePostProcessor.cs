using Tiles;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ScenePostProcessor : IProcessSceneWithReport
{
    public int callbackOrder { get; }
    public void OnProcessScene(Scene scene, BuildReport report)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject t in roots)
        {
            TileDatabase tileDatabase = t.GetComponentInChildren<TileDatabase>();
            if (tileDatabase == null)
                continue;
            
            for (int c = tileDatabase.transform.childCount - 1; c >= 0; c--)
            {
                Object.DestroyImmediate(tileDatabase.transform.GetChild(c).gameObject);
            }
        }
    }
}
