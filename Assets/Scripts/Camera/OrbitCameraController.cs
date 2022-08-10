using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class OrbitCameraController : MonoBehaviour
{
    [SerializeField] private float _zoomSpeed = 1.0f;
    [SerializeField] private float _zoomDeceleration = 20.0f;
    [SerializeField] private float _panSpeed = 0.1f;
    [SerializeField] private float _deceleration = 5.0f;
    [SerializeField] private float _minZoom;
    [SerializeField] private float _maxZoom;
    [SerializeField] private float _fov = 25.0f;
    [SerializeField] private float _rotSpeed = 0.1f;
    [SerializeField] private float _rotDeceleration = 10.0f;

    [SerializeField] private Camera _camera;
    [SerializeField] private GameObject _gimbalH;
    [SerializeField] private GameObject _gimbalV;
    [SerializeField] private GameObject _gimbalA;
    
    private Vector2 _velocity;
    private float _zoomVelocity;
    private float _rotateVelocity;
    private bool _doPan;
    private bool _doRot;

    private float _gimbalVRot;
    
    private Transform _transform;
    private Transform _cameraTransform;
    
    private void Start()
    {
        _transform = transform;
        _cameraTransform = _camera.transform;
        
        Vector3 newPos = _transform.localPosition;
        newPos.z = -(_maxZoom + _minZoom) * 0.5f;
        _transform.localPosition = newPos;
    }

    private void Update()
    {
        if (_doPan)
        {
            _velocity += Pointer.current.delta.ReadValue();
        }

        _gimbalH.transform.Rotate(new Vector3(0.0f, 1.0f, 0.0f), _velocity.x * _panSpeed, Space.Self);

        _gimbalVRot = Mathf.Clamp(_gimbalVRot + _velocity.y * -_panSpeed, -90.0f, 90.0f);
        
        _gimbalV.transform.localPosition = Vector3.zero;
        _gimbalV.transform.localRotation = Quaternion.identity;
        _gimbalV.transform.Rotate(new Vector3(1.0f, 0.0f, 0.0f), _gimbalVRot);
        
        _velocity = Vector2.Lerp(_velocity, new Vector2(0.0f, 0.0f), Time.deltaTime * _deceleration);
        
        Vector3 newPos = _transform.localPosition;
        newPos += new Vector3(0.0f, 0.0f, _zoomVelocity * Mathf.Abs(_transform.localPosition.z) * Time.deltaTime);
        newPos.z = Mathf.Clamp(newPos.z, -_maxZoom, -_minZoom);
        _transform.localPosition = newPos;
        _zoomVelocity = Mathf.Lerp(_zoomVelocity, 0.0f, Time.deltaTime * _zoomDeceleration);
        
        // _transform.LookAt(_gimbalA.transform.position, _gimbalA.transform.position.normalized);

        _cameraTransform.position = _transform.position;
        _cameraTransform.rotation = _transform.rotation;
        _camera.fieldOfView = _fov;
        
        // Rotation
        if (_doRot)
        {
            _rotateVelocity += Pointer.current.delta.ReadValue().x;
        }
        _gimbalH.transform.Rotate(_gimbalA.transform.position.normalized, _rotateVelocity * _rotSpeed, Space.World);
        _rotateVelocity = Mathf.Lerp(_rotateVelocity, 0.0f, Time.deltaTime * _rotDeceleration);
    }

    public void LeftMouse(InputAction.CallbackContext context)
    {
        // _doRot = context.phase switch
        // {
        //     InputActionPhase.Started => true,
        //     InputActionPhase.Canceled => false,
        //     _ => _doRot
        // };
    }
    
    public void RightMouse(InputAction.CallbackContext context)
    {
        _doPan = context.phase switch
        {
            InputActionPhase.Started => true,
            InputActionPhase.Canceled => false,
            _ => _doPan
        };
    }

    public void MouseWheel(InputAction.CallbackContext context)
    {
        if (context.phase != InputActionPhase.Started) return;
        Vector2 value = context.ReadValue<Vector2>();
        value = Vector2.ClampMagnitude(value, 1.0f);
        _zoomVelocity += value.y * _zoomSpeed;
    }
}