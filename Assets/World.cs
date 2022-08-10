using System;
using System.Collections;
using System.Collections.Generic;
using Tiles;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Utils;

public class World : MonoBehaviour
{
    [SerializeField] private TileDatabase _tileDatabase;
    [SerializeField] private Grid _grid;
    [SerializeField] private WFCBase _wfc;
    [SerializeField] private TileFactory _tileFactory;
    [SerializeField] private Camera _mainCamera;
    [SerializeField] private PickingRenderer _pickingCube;
    [SerializeField] private GameObject _cameraRig;
    
    private MeshFilter _errorCubeA;
    private MeshFilter _errorCubeB;
    
    public float WFCTimeslice = 1.0f / 60.0f;
    public int WFCStepsPerFrame = -1;
    public float WFCStepDelay = 0.0f;
    
    private bool _wfcFinished = false;
    private float _wfcDelayTimer;
    private double _wfcStarted = 0.0f;
    private double _wfcTimer = 0.0f;

    private Coroutine _cameraCoroutine;

    void Start()
    {
        _grid.Create();
        _wfc.Setup(_grid, _tileDatabase.Tiles);
        _tileFactory.Init(_grid);
        
        _errorCubeA = transform.Find("ErrorCubeA").GetComponent<MeshFilter>();
        _errorCubeB = transform.Find("ErrorCubeB").GetComponent<MeshFilter>();

        _pickingCube = Instantiate(_pickingCube, transform);
    }

    private void Update()
    {
        UpdateWFC();
    }

    private IEnumerator CameraRigLerp(Vector3 start, Vector3 end)
    {
        float t = 0.0f;
        while (t <= 1.0) 
        {
            t += Time.deltaTime * 2.0f;
            _cameraRig.transform.position = Vector3.Lerp(start, end,Easing.Out(t));
            yield return null;
        }
    }

    private void ResetWFC()
    {
        _wfcFinished = false;
        _wfcStarted = Time.realtimeSinceStartupAsDouble;
    }

    private void UpdateWFC()
    {
        if (_wfcFinished) return;
        
        int wfcSteps = 0;
        _wfcDelayTimer -= Time.deltaTime;

        List<int> lastCollapseQueue = new List<int>();
        List<int> collapsedProtQueue = new List<int>();

        double wfcStartTime = Time.realtimeSinceStartupAsDouble;
        if (_wfcStarted == 0.0f)
            _wfcStarted = wfcStartTime;

        while ((wfcSteps < WFCStepsPerFrame && _wfcDelayTimer <= 0.0f)
               || ((WFCStepsPerFrame == -1) && Time.realtimeSinceStartupAsDouble < wfcStartTime + WFCTimeslice))
        {
            _wfcDelayTimer = WFCStepDelay;

            WFCBase.StepResult result = _wfc.Step(out int lastCollapse, out int collapsedPrototype, out int incompatibleStack, out int incompatibleNeighbor);

            switch (result)
            {
                case WFCBase.StepResult.WFCInProgress:
                    wfcSteps++;
                    break;
                case WFCBase.StepResult.WFCFinished:
                    _wfcFinished = true;
                    break;
                case WFCBase.StepResult.WFCReset:
                    break;
                case WFCBase.StepResult.WFCBacktrackLimit:
                    _wfcFinished = true;
                    Debug.LogError("WFC backtrack limit reached.");
                    break;
                case WFCBase.StepResult.WFCPropagateError:
                    DrawErrorCube(_errorCubeB, new List<int> { incompatibleStack });
                    DrawErrorCube(_errorCubeA, new List<int> { incompatibleNeighbor });
                    _wfcFinished = true;
                    Debug.LogError($"Could not find compatible tile for cell {incompatibleStack}.");
                    break;
                case WFCBase.StepResult.WFCCollapseError:
                    _wfcFinished = true;
                    Debug.LogError("Error in WFC.");
                    break;
                default:
                    _wfcFinished = true;
                    Debug.LogError("Error in WFC.");
                    break;
            }

            if (result != WFCBase.StepResult.WFCCollapseError &&
                result != WFCBase.StepResult.WFCFinished)
            {
                lastCollapseQueue.Add(lastCollapse);
                collapsedProtQueue.Add(collapsedPrototype);
                DrawErrorCube(_errorCubeA, new List<int> { lastCollapse });
                
                //Debug.Log($"Collapsed cell {lastCollapse} with tile {collapsedPrototype}.");
            }

            if (_wfcFinished)
                break;
        }

        if (true)
        {
            if (wfcSteps > 0)
            {
                Debug.Log($"{wfcSteps} WFC steps in {Time.realtimeSinceStartupAsDouble - wfcStartTime} this frame.");
            }
            _wfcTimer += Time.realtimeSinceStartupAsDouble - wfcStartTime;
            if (_wfcFinished)
            {
                Debug.Log($"WFC completed in: {Time.realtimeSinceStartupAsDouble - _wfcStarted}.");
                Debug.Log($"WFC only: {_wfcTimer}.");
            }
        }

        while (lastCollapseQueue.Count > 0 && collapsedProtQueue.Count > 0)
        {
            int cell = lastCollapseQueue[0];
            int prot = collapsedProtQueue[0];
            lastCollapseQueue.RemoveAt(0);
            collapsedProtQueue.RemoveAt(0);
            OnCellCollapsed(cell, prot);
        }
    }
    
    void DrawErrorCube(MeshFilter renderer, List<int> cells)
    {
        renderer.mesh = new Mesh();
        Mesh mesh = renderer.mesh;

        List<Vector3> verts = new();
        List<int> indices = new();

        //Vector3[] gridVerts = _grid.VoxelVerts;
        foreach (int cell in cells)
        {
            if (cell == -1)
                continue;
            
            // VoxelGrid.Cell gridCell = _grid.Cells[cell];
            // for (int i = 0; i < 4; i++)
            // {
            //     verts.Add(gridVerts[gridCell.vTop[i]]);
            //     indices.Add(verts.Count - 1);
            //     verts.Add(gridVerts[gridCell.vBot[i]]);
            //     indices.Add(verts.Count - 1);
            // }
            // for (int i = 0; i < 4; i++)
            // {
            //     verts.Add(gridVerts[gridCell.vTop[i]]);
            //     indices.Add(verts.Count - 1);
            //     verts.Add(gridVerts[gridCell.vTop[(i + 1) % 4]]);
            //     indices.Add(verts.Count - 1);
            // }
            // for (int i = 0; i < 4; i++)
            // {
            //     verts.Add(gridVerts[gridCell.vBot[i]]);
            //     indices.Add(verts.Count - 1);
            //     verts.Add(gridVerts[gridCell.vBot[(i + 1) % 4]]);
            //     indices.Add(verts.Count - 1);
            // }
        }
        mesh.vertices = verts.ToArray();
        mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
    }
    
    private bool OnCellCollapsed(int cellIdx, int tileIdx)
    {
        return _tileFactory.SetTile(cellIdx, tileIdx, _tileDatabase, _grid);
    }
}
