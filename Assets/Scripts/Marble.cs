using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Marble : MonoBehaviour
{
    public void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("WinArea"))
        {
            World.Instance.OnMarbleEnterWinArea();
        }
    }
}
