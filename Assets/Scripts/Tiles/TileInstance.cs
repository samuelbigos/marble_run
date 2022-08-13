using Tiles;
using UnityEngine;
using UnityEditor;

public class TileInstance : MonoBehaviour
{
    [SerializeField] private MeshFilter MeshFilter;
    [SerializeField] private MeshRenderer MeshRenderer;

    private TileDatabase _tileDatabase = null;
    private Grid _grid = null;
    [SerializeField] private int _tileSize;
    [SerializeField] private TileDatabase.Tile _tile;
    [SerializeField] private int _cellIdx;

    public Mesh GetMesh()
    {
        return MeshFilter.mesh;
    }

    public void Init(TileDatabase.Tile tile, int cellIdx, Grid grid, Vector3 position)
    {
        MeshRenderer.transform.position += tile.MeshOffset;

        _tile = tile;
        name = tile.Name;

        if (tile.Materials != null)
        {
            MeshRenderer.sharedMaterials = tile.Materials;
        }
        if (tile.Mesh)
        {
            MeshFilter.sharedMesh = tile.Mesh;
        }

        _grid = grid;
        Transform cachedTransform = transform;
        if (grid)
        {
            cachedTransform.position = (Vector3)Grid.XYZFromIndex(cellIdx, grid.GridSize) * grid.CellSize;
                                       // + (Vector3.one * (grid.CellSize * 0.5f)) 
                                       // - (grid.GridSizeF * (grid.CellSize * 0.5f));
            cachedTransform.localScale = Vector3.one * (grid.CellSize / tile.Size);
        }
        else
        {
            cachedTransform.position = position;
        }
        
        cachedTransform.rotation = Quaternion.Euler(0.0f, 90.0f * tile.Rot, 0.0f);

        _cellIdx = cellIdx;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying && !_tileDatabase)
        {
            _tileDatabase = transform.parent.gameObject.GetComponent<TileDatabase>();
        }
        _tileSize = !Application.isPlaying ? _tileDatabase.TileSize : _tile.Size;

        float o = _tileSize * 0.5f;
        Vector3 p = transform.position;
        
        Vector3[] bot = new Vector3[4];
        bot[0] = new Vector3(-o, -o, -o);
        bot[1] = new Vector3(o, -o, -o);
        bot[2] = new Vector3(o, -o, o);
        bot[3] = new Vector3(-o, -o, o);
        Vector3[] top = new Vector3[4];
        top[0] = new Vector3(-o, o, -o);
        top[1] = new Vector3(o, o, -o);
        top[2] = new Vector3(o, o, o);
        top[3] = new Vector3(-o, o, o);
        
        for (int i = 0; i < 4; i++)
        {
            Gizmos.color = Color.green;

            float s = transform.localScale.x;
            Gizmos.DrawLine(p + bot[i] * s, p + bot[i] * s + (top[i] - bot[i]) * 0.25f * s);
            Gizmos.DrawLine(p + top[i] * s, p + top[i] * s + (bot[i] - top[i]) * 0.25f * s);
            Gizmos.DrawLine(p + bot[i] * s, p + bot[i] * s + (bot[(i + 1) % 4] - bot[i]) * 0.25f * s);
            Gizmos.DrawLine(p + bot[i] * s, p + bot[i] * s + (bot[(i + 3) % 4] - bot[i]) * 0.25f * s);
            Gizmos.DrawLine(p + top[i] * s, p + top[i] * s + (top[(i + 1) % 4] - top[i]) * 0.25f * s);
            Gizmos.DrawLine(p + top[i] * s, p + top[i] * s + (top[(i + 3) % 4] - top[i]) * 0.25f * s);

            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.magenta;

            if (_tile != null)
            {
                //Handles.Label(transform.position, $"Coord: {_grid.XYZFromIndex(_cellIdx)}", style);
                //Handles.Label(transform.position + top[0] * 0.25f, $"Idx: {_cellIdx}", style);
                
                Handles.Label(transform.position + Vector3.forward * 0.5f, $"{_tile.Sides[0]}", style);
                Handles.Label(transform.position + Vector3.right * 0.5f, $"{_tile.Sides[1]}", style);
                Handles.Label(transform.position - Vector3.forward * 0.5f, $"{_tile.Sides[2]}", style);
                Handles.Label(transform.position - Vector3.right * 0.5f, $"{_tile.Sides[3]}", style);
            }
            
            // Handles.Label(p + v[_cell.vBot[i]] + (v[_cell.vBot[(i + 1) % 4]] + v[_cell.vBot[(i + 3) % 4]] - v[_cell.vBot[i]] * 2.0f) * 0.15f, $"{_prot._cornerMasksBot[i, 0]}", style);
            // Handles.Label(p + v[_cell.vBot[i]] + (v[_cell.vTop[i]] + v[_cell.vBot[(i + 3) % 4]] - v[_cell.vBot[i]] * 2.0f) * 0.15f, $"{_prot._cornerMasksBot[i, 1]}", style);
            // Handles.Label(p + v[_cell.vBot[i]] + (v[_cell.vTop[i]] + v[_cell.vBot[(i + 1) % 4]] - v[_cell.vBot[i]] * 2.0f) * 0.15f, $"{_prot._cornerMasksBot[i, 2]}", style);
            //
            // Handles.Label(p + v[_cell.vTop[i]] + (v[_cell.vTop[(i + 1) % 4]] + v[_cell.vTop[(i + 3) % 4]] - v[_cell.vTop[i]] * 2.0f) * 0.15f, $"{_prot._cornerMasksTop[i, 0]}", style);
            // Handles.Label(p + v[_cell.vTop[i]] + (v[_cell.vBot[i]] + v[_cell.vTop[(i + 3) % 4]] - v[_cell.vTop[i]] * 2.0f) * 0.15f, $"{_prot._cornerMasksTop[i, 1]}", style);
            // Handles.Label(p + v[_cell.vTop[i]] + (v[_cell.vBot[i]] + v[_cell.vTop[(i + 1) % 4]] - v[_cell.vTop[i]] * 2.0f) * 0.15f, $"{_prot._cornerMasksTop[i, 2]}", style);
        }
    }
#endif
}
