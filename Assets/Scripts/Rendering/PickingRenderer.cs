using System;
using UnityEngine;

public class PickingRenderer : MonoBehaviour
{
    public enum State
    {
        Hidden,
        Valid,
        Invalid
    }
    
    private MeshRenderer _meshRenderer;
    private MeshFilter _meshFilter;
    private Transform _transform;

    private State _state = State.Valid;

    private void Start()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        _meshFilter = GetComponent<MeshFilter>();
        _transform = transform;
        _transform.position = new Vector3(0.0f, 0.0f, 0.0f);
        _transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
    }

    public void SetState(State state)
    {
        if (state == _state) return;
        _state = state;
        switch (state)
        {
            case State.Hidden:
                gameObject.SetActive(false);
                break;
            case State.Valid:
                gameObject.SetActive(true);
                _meshRenderer.material.SetColor("_Color", Color.white);
                break;
            case State.Invalid:
                gameObject.SetActive(true);
                _meshRenderer.material.SetColor("_Color", Color.red);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }
}