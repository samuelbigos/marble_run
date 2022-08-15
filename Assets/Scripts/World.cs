using System;
using System.Collections;
using System.Collections.Generic;
using Tiles;
using TMPro;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Utils;

public class World : MonoBehaviour
{
    private static World _instance;
    public static World Instance => _instance;
    
    [SerializeField] private TileDatabase _tileDatabase;
    [SerializeField] private Grid _grid;
    [SerializeField] private Vector3Int _gridSize;
    [SerializeField] private WFC _wfc;
    [SerializeField] private TileFactory _tileFactory;
    [SerializeField] private Camera _mainCamera;
    [SerializeField] private GameObject _cameraRig;
    [SerializeField] private OrbitCameraController _orbitCameraController;
    [SerializeField] private GameObject _cameraTilter;
    [SerializeField] private GameObject _marblePrefab;
    [SerializeField] private float TiltForce = 1.0f;
    [SerializeField] private float VisualTiltSpeed = 3.0f;
    [SerializeField] private float VisualTiltAmount = 15.0f;
    [SerializeField] private float WinTime = 3.0f;
    [SerializeField] private int Seed = -1;

    [SerializeField] private Slider _sizeXSlider;
    [SerializeField] private Slider _sizeYSlider;
    [SerializeField] private Slider _sizeZSlider;
    [SerializeField] private TextMeshProUGUI _sizeXValue;
    [SerializeField] private TextMeshProUGUI _sizeYValue;
    [SerializeField] private TextMeshProUGUI _sizeZValue;
    
    private MeshFilter _errorCubeA;
    private MeshFilter _errorCubeB;
    
    public float WFCTimeslice = 1.0f / 60.0f;
    public int WFCStepsPerFrame = -1;
    public float WFCStepDelay = 0.0f;

   
    private bool _wfcFinished = false;
    private float _wfcDelayTimer;
    private double _wfcStarted = 0.0f;
    private double _wfcTimer = 0.0f;
    private bool _spawnedMarble = false;
    private Vector3 _marbleSpawnPos;
    private Rigidbody _marble;
    private bool _win;
    private float _winTimer;
    
    private Vector3 _camOriginalPos;
    private Quaternion _camOriginalRot;

    private void Awake()
    {
        _instance = this;
    }

    void Start()
    {
        _grid.Create(_gridSize);
        _tileDatabase.Init();

        Init();
        
        _mainCamera.transform.position += (Vector3)_grid.GridSize * _grid.CellSize * 0.5f;

        _sizeXSlider.value = _gridSize.x;
        _sizeYSlider.value = _gridSize.y;
        _sizeZSlider.value = _gridSize.z;
        OnSizeXChanged();
        OnSizeYChanged();
        OnSizeZChanged();
    }

    private void Init()
    {
        _grid.Create(_gridSize);
        
        transform.rotation = Quaternion.identity;
        
        Vector3Int start = new Vector3Int((int)(_grid.GridSize.x * 0.5f), _grid.GridSize.y - 1, (int)(_grid.GridSize.z * 0.5f));
        Vector3Int end = new Vector3Int((int)(_grid.GridSize.x * 0.5f), 0, (int)(_grid.GridSize.z * 0.5f));
        
        _wfc.Setup(_grid, _tileDatabase.Tiles, start, end, Seed);
        
        _marbleSpawnPos = start * (int) _grid.CellSize + Vector3.up * 2.0f;
        
        _tileFactory.Init(_grid);
        
        _errorCubeA = transform.Find("ErrorCubeA").GetComponent<MeshFilter>();
        _errorCubeB = transform.Find("ErrorCubeB").GetComponent<MeshFilter>();
        // _errorCubeA.gameObject.SetActive(false);
        // _errorCubeB.gameObject.SetActive(false);
        
        Vector3 camPos = _mainCamera.transform.position;
        camPos.y = _gridSize.y * _grid.CellSize + 10.0f;
        _mainCamera.transform.position = camPos;
        _camOriginalPos = _mainCamera.transform.position;
        _camOriginalRot = _mainCamera.transform.rotation;
    }

    private void Update()
    {
        UpdateWFC();

        if (_wfcFinished && !_spawnedMarble)
        {
            GameObject marble = Instantiate(_marblePrefab, _marbleSpawnPos, quaternion.identity);
            _marble = marble.GetComponent<Rigidbody>();
            _spawnedMarble = true;
            _cameraRig.SetActive(true);
        }

        if (_win)
        {
            _mainCamera.transform.position = Vector3.Lerp(_mainCamera.transform.position, _camOriginalPos, Time.deltaTime);
            _mainCamera.transform.rotation = Quaternion.Lerp(_mainCamera.transform.rotation, _camOriginalRot, Time.deltaTime);
            
            _winTimer -= Time.deltaTime;
            if (_winTimer < 0.0f)
            {
                OnRegenerate();
            }
        }
    }

    private void FixedUpdate()
    {
        if (_wfcFinished)
        {
            Vector3 forward = _mainCamera.transform.forward.normalized;
            Vector3 right = _mainCamera.transform.right.normalized;
            Vector3 dir = Vector3.zero;
            
            if (Keyboard.current.wKey.isPressed)
                dir += forward;
            if (Keyboard.current.aKey.isPressed)
                dir -= right;
            if (Keyboard.current.sKey.isPressed)
                dir -= forward;
            if (Keyboard.current.dKey.isPressed)
                dir += right;
            
            _marble.AddForceAtPosition(dir * Time.deltaTime * TiltForce, _marble.position, ForceMode.Impulse);
            
            Vector3 tiltAxis = Vector3.Cross(dir.normalized, Vector3.up);
            Quaternion desiredTilt = Quaternion.AngleAxis(VisualTiltAmount * dir.magnitude, tiltAxis);
            _cameraTilter.transform.rotation = Quaternion.Slerp(_cameraTilter.transform.rotation, desiredTilt, Time.deltaTime * VisualTiltSpeed);
        }
        
        if (_wfcFinished && !_win)
        {
            _cameraRig.transform.position = _marble.position;
        }
    }

    private void UpdateWFC()
    {
        if (_wfcFinished) return;
        
        int wfcSteps = 0;
        _wfcDelayTimer -= Time.deltaTime;

        List<int> collapsedCellQueue = new List<int>();
        List<int> collapsedTileQueue = new List<int>();

        double wfcStartTime = Time.realtimeSinceStartupAsDouble;
        if (_wfcStarted == 0.0f)
            _wfcStarted = wfcStartTime;

        while ((wfcSteps < WFCStepsPerFrame && _wfcDelayTimer <= 0.0f)
               || ((WFCStepsPerFrame == -1) && Time.realtimeSinceStartupAsDouble < wfcStartTime + WFCTimeslice))
        {
            WFC.StepResult result = _wfc.Step(out List<(int, int)> collapses, out int incompatibleStack, out int incompatibleNeighbor);

            switch (result)
            {
                case WFC.StepResult.WFCInProgress:
                    break;
                case WFC.StepResult.WFCFinished:
                    _wfcFinished = true;
                    break;
                case WFC.StepResult.WFCPropagateError:
                    DrawErrorCube(_errorCubeA, new List<int> { incompatibleNeighbor });
                    DrawErrorCube(_errorCubeB, new List<int> { incompatibleStack });
                    _wfcFinished = true;
                    Debug.LogError($"Could not find compatible tile for cell {incompatibleNeighbor} to {incompatibleStack}.");
                    break;
                case WFC.StepResult.WFCCollapseError:
                    _wfcFinished = true;
                    Debug.LogError("Error in WFC.");
                    break;
                default:
                    _wfcFinished = true;
                    Debug.LogError("Error in WFC.");
                    break;
            }

            bool addedMesh = false;
            if (result != WFC.StepResult.WFCFinished && result != WFC.StepResult.WFCCollapseError)
            {
                List<int> cubes = new();
                foreach ((int, int) collapse in collapses)
                {
                    collapsedCellQueue.Add(collapse.Item1);
                    collapsedTileQueue.Add(collapse.Item2);
                    cubes.Add(collapse.Item1);

                    if (_tileFactory.TileHasMesh(collapse.Item2, _tileDatabase))
                        addedMesh = true;
                }
                //DrawErrorCube(_errorCubeA, cubes);
            }

            if (addedMesh)
            {
                _wfcDelayTimer = WFCStepDelay;
                wfcSteps++;
            }

            if (_wfcFinished)
                break;
        }

        if (true)
        {
            if (wfcSteps > 0)
            {
                //Debug.Log($"{wfcSteps} WFC steps in {Time.realtimeSinceStartupAsDouble - wfcStartTime} this frame.");
            }
            _wfcTimer += Time.realtimeSinceStartupAsDouble - wfcStartTime;
            if (_wfcFinished)
            {
                Debug.Log($"WFC completed in: {Time.realtimeSinceStartupAsDouble - _wfcStarted}.");
                Debug.Log($"WFC only: {_wfcTimer}.");
            }
        }

        while (collapsedCellQueue.Count > 0 && collapsedTileQueue.Count > 0)
        {
            int cell = collapsedCellQueue[0];
            int prot = collapsedTileQueue[0];
            collapsedCellQueue.RemoveAt(0);
            collapsedTileQueue.RemoveAt(0);
            OnCellCollapsed(cell, prot);
        }
    }
    
    void DrawErrorCube(MeshFilter renderer, List<int> cells)
    {
        renderer.mesh = new Mesh();
        Mesh mesh = renderer.mesh;

        List<Vector3> verts = new();
        List<int> indices = new();

        List<Vector3> cellVerts = new List<Vector3>();
        cellVerts.Add(new Vector3(-1.0f, -1.0f, -1.0f) * _grid.CellSize * 0.5f);
        cellVerts.Add(new Vector3(1.0f, -1.0f, -1.0f) * _grid.CellSize * 0.5f);
        cellVerts.Add(new Vector3(1.0f, -1.0f, 1.0f) * _grid.CellSize * 0.5f);
        cellVerts.Add(new Vector3(-1.0f, -1.0f, 1.0f) * _grid.CellSize * 0.5f);
        cellVerts.Add(new Vector3(-1.0f, 1.0f, -1.0f) * _grid.CellSize * 0.5f);
        cellVerts.Add(new Vector3(1.0f, 1.0f, -1.0f) * _grid.CellSize * 0.5f);
        cellVerts.Add(new Vector3(1.0f, 1.0f, 1.0f) * _grid.CellSize * 0.5f);
        cellVerts.Add(new Vector3(-1.0f, 1.0f, 1.0f) * _grid.CellSize * 0.5f);
        
        foreach (int cell in cells)
        {
            if (cell == -1)
                continue;

            Vector3 pos = Grid.XYZFromIndex(cell, _grid.GridSize) * (int) _grid.CellSize;

            foreach (Vector3 v in cellVerts)
            {
                verts.Add(pos + v);
            }
            
            indices.Add(0); indices.Add(1);
            indices.Add(1); indices.Add(2);
            indices.Add(2); indices.Add(3);
            indices.Add(3); indices.Add(0);
            
            indices.Add(4); indices.Add(5);
            indices.Add(5); indices.Add(6);
            indices.Add(6); indices.Add(7);
            indices.Add(7); indices.Add(4);
            
            indices.Add(0); indices.Add(4);
            indices.Add(1); indices.Add(5);
            indices.Add(2); indices.Add(6);
            indices.Add(3); indices.Add(7);
        }
        mesh.vertices = verts.ToArray();
        mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
    }
    
    private bool OnCellCollapsed(int cellIdx, int tileIdx)
    {
        return _tileFactory.SetTile(cellIdx, tileIdx, _tileDatabase, _grid);
    }
    
    public void OnRegenerate()
    {
        _win = false;
        _wfcFinished = false;
        _cameraRig.SetActive(false);
        _mainCamera.transform.SetPositionAndRotation(_camOriginalPos, _camOriginalRot);
        _spawnedMarble = false;
        Destroy(_marble.gameObject);
        _marble = null;
        
        _wfc.Reset();
        Init();
    }

    public void OnSizeXChanged()
    {
        float value = _sizeXSlider.value;
        _sizeXValue.text = $"{value}";
        _gridSize.x = (int) value;
    }
    
    public void OnSizeYChanged()
    {
        float value = _sizeYSlider.value;
        _sizeYValue.text = $"{value}";
        _gridSize.y = (int) value;
    }
    
    public void OnSizeZChanged()
    {
        float value = _sizeZSlider.value;
        _sizeZValue.text = $"{value}";
        _gridSize.z = (int) value;
    }

    public void OnMarbleEnterWinArea()
    {
        _win = true;
        _winTimer = WinTime;
        _cameraRig.SetActive(false);
    }
}
