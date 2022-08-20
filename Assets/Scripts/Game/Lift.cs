using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

public class Lift : MonoBehaviour
{
    [SerializeField] private GameObject _platformPrefab;
    [SerializeField] private float _platformSpacing = 1.0f;
    [SerializeField] private float _platformSpeed = 5.0f;
    [SerializeField] private float _liftRadius = 5.0f;
    [SerializeField] private float _liftHeight = 10.0f;

    [SerializeField] private GameObject _runTop;
    [SerializeField] private GameObject _runBot;

    private List<Rigidbody> _platforms = new List<Rigidbody>();
    private List<float> _platformLocAroundPerimeter = new List<float>();
    private float _liftPerimeter;

    public void Setup(Vector3 start, Vector3 end, Vector3 facing)
    {
        transform.position = start;
        _liftHeight = start.y - end.y;
        Quaternion rot = Quaternion.Euler(0.0f, Vector3.Angle(facing, Vector3.right), 0.0f);
        transform.rotation = rot;

        _runTop.transform.position = start + Vector3.up * _liftRadius;
        _runBot.transform.position = end - Vector3.up * _liftRadius;
    }
    
    private void Start()
    {
        //Setup(new Vector3(0.0f, 5.0f, 0.5f), new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 0.0f));
        
        _liftPerimeter = 2.0f * (Mathf.PI * _liftRadius + _liftHeight);
        int platformCount = (int) (_liftPerimeter / _platformSpacing);
        
        for (int i = 0; i < platformCount; i++)
        {
            float loc = _liftPerimeter / platformCount;
            _platformLocAroundPerimeter.Add(loc * i);
        }

        for (int i = 0; i < platformCount; i++)
        {
            GameObject platform = Instantiate(_platformPrefab, transform);
            _platforms.Add(platform.GetComponent<Rigidbody>());
        }
    }

    private void Update()
    {
        // Spread platforms around the perimeter.
        for (int i = 0; i < _platforms.Count; i++)
        {
            Rigidbody platform = _platforms[i];
            _platformLocAroundPerimeter[i] = (_platformLocAroundPerimeter[i] + _platformSpeed * Time.deltaTime) % _liftPerimeter;
            
            Vector3 pos = TransformAroundPerimeter(_platformLocAroundPerimeter[i], out Quaternion rot);
            platform.MovePosition(transform.TransformPoint(pos));

            Quaternion rotation = transform.rotation * rot;
            platform.MoveRotation(rotation);
        }
    }

    private Vector3 TransformAroundPerimeter(float loc, out Quaternion rot)
    {
        Vector3 start = Vector3.zero;
        rot = Quaternion.identity;
        
        float p1 = Mathf.PI * _liftRadius;
        float p2 = p1 + _liftHeight;
        float p3 = p2 + Mathf.PI * _liftRadius;
        float p4 = p3 + _liftHeight;
        
        float t = Mathf.Clamp01(loc / p1) * Mathf.PI;
        start += new Vector3(Mathf.Cos(t), Mathf.Sin(t), 0.0f) * _liftRadius;

        if (loc <= p1)
        {
            rot = Quaternion.Euler(new Vector3(0.0f, 0.0f, t * Mathf.Rad2Deg));
            return start;
        }
            

        t =  Mathf.Clamp01((loc - p1) / (p2 - p1));
        start += new Vector3(0.0f, t * -_liftHeight, 0.0f);

        if (loc <= p2)
        {
            rot = Quaternion.Euler(new Vector3(0.0f, 0.0f, Mathf.PI * Mathf.Rad2Deg));
            return start;
        }

        t =  Mathf.Clamp01((loc - p2) / (p3 - p2)) * Mathf.PI;
        start = new Vector3(-Mathf.Cos(t), -Mathf.Sin(t), 0.0f) * _liftRadius;
        start += Vector3.down * _liftHeight;

        if (loc <= p3)
        {
            rot = Quaternion.Euler(new Vector3(0.0f, 0.0f, (t + Mathf.PI) * Mathf.Rad2Deg));
            return start;
        }

        t =  Mathf.Clamp01((loc - p3) / (p4 - p3));
        start += new Vector3(0.0f, t * _liftHeight, 0.0f);

        return start;
    }
}
