using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Utils
{
    public struct NativeArrayXD<T> where T : struct
    {
#if UNITY_EDITOR
        int _dims;
#endif
        int _x, _y, _z, _w;
        int _sY, _sZ, _sW;

        NativeArray<T> _internal;

        public int InternalSize { get { return _internal.Length; } }
        unsafe public void* GetUnsafePtr()
        {
            return NativeArrayUnsafeUtility.GetUnsafePtr(_internal);
        }
        public int X => _x;
        public int Y => _y;
        public int Z => _z;
        public int W => _w;
    
        public NativeArrayXD(int x, int y)
        {
#if UNITY_EDITOR
            _dims = 2;
#endif
            _x = x;
            _y = y;
            _z = 0;
            _w = 0;
            _sY = y;
            _sZ = 0;
            _sW = 0;
            _internal = new NativeArray<T>(_x * _y, Allocator.Persistent);
        }

        public NativeArrayXD(int x, int y, int z)
        {
#if UNITY_EDITOR
            _dims = 3;
#endif
            _x = x;
            _y = y;
            _z = z;
            _w = 0;
            _sY = y * z;
            _sZ = z;
            _sW = 0;
            _internal = new NativeArray<T>(_x * _y * _z, Allocator.Persistent);
        }

        public NativeArrayXD(int x, int y, int z, int w)
        {
#if UNITY_EDITOR
            _dims = 4;
#endif
            _x = x;
            _y = y;
            _z = z;
            _w = w;
            _sY = y * z * w;
            _sZ = z * w;
            _sW = w;
            _internal = new NativeArray<T>(_x * _y * _z * _w, Allocator.Persistent);
        }

        // Accessing with a single value returns the actual value from the non-directional array.
        public T this[int x]
        {
            get { return _internal[x]; }
            set { _internal[x] = value; }
        }

        public T this[int x, int y]
        {
            get { return _internal[ToNonDimensional(x, y)]; }
            set { _internal[ToNonDimensional(x, y)] = value; }
        }

        public T this[int x, int y, int z]
        {
            get { return _internal[ToNonDimensional(x, y, z)]; }
            set { _internal[ToNonDimensional(x, y, z)] = value; }
        }

        public T this[int x, int y, int z, int w]
        {
            get { return _internal[ToNonDimensional(x, y, z, w)]; }
            set { _internal[ToNonDimensional(x, y, z, w)] = value; }
        }

        [Conditional("UNITY_EDITOR")]
        void Check(int dims, int x, int y)
        {
#if UNITY_EDITOR
            if (dims != _dims) UnityEngine.Debug.LogError($"Incorrect index count for this array's dimensions (gave {dims} of {_dims}).");
            if (x >= _x) UnityEngine.Debug.LogError($"Out of range (access {x} of {_x}");
            if (y >= _y) UnityEngine.Debug.LogError($"Out of range (access {y} of {_y}");
#endif
        }
        [Conditional("UNITY_EDITOR")]
        void Check(int dims, int x, int y, int z)
        {
#if UNITY_EDITOR
            if (dims != _dims) UnityEngine.Debug.LogError($"Incorrect index count for this array's dimensions (gave {dims} of {_dims}).");
            if (x >= _x) UnityEngine.Debug.LogError($"Out of range (access {x} of {_x}");
            if (y >= _y) UnityEngine.Debug.LogError($"Out of range (access {y} of {_y}");
            if (z >= _z) UnityEngine.Debug.LogError($"Out of range (access {z} of {_z}");
#endif
        }
        [Conditional("UNITY_EDITOR")]
        void Check(int dims, int x, int y, int z, int w)
        {
#if UNITY_EDITOR
            if (dims != _dims) UnityEngine.Debug.LogError($"Incorrect index count for this array's dimensions (gave {dims} of {_dims}).");
            if (x >= _x) UnityEngine.Debug.LogError($"Out of range (access {x} of {_x}");
            if (y >= _y) UnityEngine.Debug.LogError($"Out of range (access {y} of {_y}");
            if (z >= _z) UnityEngine.Debug.LogError($"Out of range (access {z} of {_z}");
            if (w >= _w) UnityEngine.Debug.LogError($"Out of range (access {w} of {_w}");
#endif
        }

        public void Dispose()
        {
            if (_internal.IsCreated)
                _internal.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public int ToNonDimensional(int x, int y) 
        { 
            Check(2, x, y); 
            return x * _sY + y; 
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public int ToNonDimensional(int x, int y, int z) 
        { 
            Check(3, x, y, z);  
            return x * _sY + y * _sZ + z; 
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public int ToNonDimensional(int x, int y, int z, int w) 
        { 
            Check(4, x, y, z, w); 
            return x * _sY + y * _sZ + z * _sW + w; 
        }
    }
}
