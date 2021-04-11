using System.Collections;
using System.Collections.Generic;
using UnityEngine;



[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]

public class RaytracedObject : MonoBehaviour
{
    // Start is called before the first frame update
    private void OnEnable()
    {
        RayTracingInOneWeekend.RegisterObject(this);
        Debug.Log("in");
    }

    private void OnDisable()
    {
        RayTracingInOneWeekend.UnregisterObject(this);
        Debug.Log("on");
    }
}
