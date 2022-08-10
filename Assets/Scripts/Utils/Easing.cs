using UnityEngine;

namespace Utils
{
    public class Easing : MonoBehaviour
    {
        public static float In(float k) 
        {
            return k*k;
        }
		
        public static float Out(float k) 
        {
            return k*(2f - k);
        }
		
        public static float InOut(float k) 
        {
            if ((k *= 2f) < 1f) return 0.5f*k*k;
            return -0.5f*((k -= 1f)*(k - 2f) - 1f);
        }

        public static float Bezier(float k, float c) 
        {
            return c*2*k*(1 - k) + k*k;
        }
    }
}
