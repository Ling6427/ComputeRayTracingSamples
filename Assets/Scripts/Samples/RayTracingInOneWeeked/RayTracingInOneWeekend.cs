using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RayTracingInOneWeekend : MonoBehaviour
{
    public enum MaterialType
    {
        MAT_LAMBERTIAN  = 0,
        MAT_METAL       = 1,
        MAT_DIELECTRIC  = 2,
    }

    public struct SphereData
    {
        public Vector3 Center;
        public float Radius;
        public int MaterialType;
        public Vector3 MaterialAlbedo;  //材質反照率
        public Vector4 MaterialData;
    }

    public struct MeshObject
    {
        public Matrix4x4 localToWorldMatrix;
        public int indices_offset;
        public int indices_count;
    }
    private static List<MeshObject> _meshObjects = new List<MeshObject>();
    private static List<Vector3> _vertices = new List<Vector3>();
    private static List<int> _indices = new List<int>();
    private ComputeBuffer _meshObjectBuffer;
    private ComputeBuffer _vertexBuffer;
    private ComputeBuffer _indexBuffer;


    public Material m_QuadMaterial;
    public ComputeShader m_ComputeShader;
    public Vector2Int m_RTSize;
    //public Texture2D SkyboxTexture;

    RenderTexture m_RTTarget;   //can be used to implement image based rendering effects, dynamic shadows, projectors, reflections or surveillance(監視) cameras.
    ComputeBuffer m_SimpleAccelerationStructureDataBuffer;  //計算緩衝區，mostly for use with compute shaders
    int m_NumSpheres = 0;
    SphereData[] m_SphereArray = new SphereData[512];
    float[] m_SphereTimeOffset = new float[512];
    public Texture SkyboxTexture { get { return m_skyboxTexture; } }
    public float SkyboxFactor { get { return m_skyboxFactor; } }

    private static bool _meshObjectsNeedRebuilding = true;
    private static List<RaytracedObject> _rayTracingObjects = new List<RaytracedObject>();

    public static void RegisterObject(RaytracedObject obj)
    {
        _rayTracingObjects.Add(obj);
        _meshObjectsNeedRebuilding = true;
        Debug.Log("1");
    }
    public static void UnregisterObject(RaytracedObject obj)
    {
        _rayTracingObjects.Remove(obj);
        _meshObjectsNeedRebuilding = true;
        Debug.Log("2");
    }

    private void RebuildMeshObjectBuffers()
    {
        if (!_meshObjectsNeedRebuilding)
        {
            Debug.Log("3");
            return;
        }
        _meshObjectsNeedRebuilding = false;
        //_currentSample = 0;
        // Clear all lists
        _meshObjects.Clear();
        _vertices.Clear();
        _indices.Clear();
        // Loop over all objects and gather their data
        foreach (RaytracedObject obj in _rayTracingObjects)
        {
            Debug.Log("4");
            Mesh mesh = obj.GetComponent<MeshFilter>().sharedMesh;
            // Add vertex data
            int firstVertex = _vertices.Count;
            _vertices.AddRange(mesh.vertices);
            // Add index data - if the vertex buffer wasn't empty before, the
            // indices need to be offset
            int firstIndex = _indices.Count;
            var indices = mesh.GetIndices(0);
            _indices.AddRange(indices.Select(index => index + firstVertex));
            // Add the object itself
            _meshObjects.Add(new MeshObject()
            {
                localToWorldMatrix = obj.transform.localToWorldMatrix,
                indices_offset = firstIndex,
                indices_count = indices.Length
            });
        }
        CreateComputeBuffer(ref _meshObjectBuffer, _meshObjects, 72);
        CreateComputeBuffer(ref _vertexBuffer, _vertices, 12);
        CreateComputeBuffer(ref _indexBuffer, _indices, 4);
    }
    private static void CreateComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data, int stride)
    where T : struct
    {
        Debug.Log("5");
        // Do we already have a compute buffer?
        if (buffer != null)
        {
            // If no data or buffer doesn't match the given criteria, release it
            if (data.Count == 0 || buffer.count != data.Count || buffer.stride != stride)
            {
                buffer.Release();
                buffer = null;
            }
        }
        if (data.Count != 0)
        {
            // If the buffer has been released or wasn't there to
            // begin with, create it
            if (buffer == null)
            {
                buffer = new ComputeBuffer(data.Count, stride);
            }
            // Set data on the buffer
            buffer.SetData(data);
        }
    }
    private void SetComputeBuffer(string name, ComputeBuffer buffer)
    {
        if (buffer != null)
        {
            m_ComputeShader.SetBuffer(0, name, buffer);
        }
    }

    private void SetMaterialParameters()
    {
        for (int i = 0; i < _rayTracingObjects.Count; i++)
        {
            RaytracedObject obj = _rayTracingObjects[i];

            MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
            Material material = renderer.sharedMaterial;

            SetComputeBuffer("meshObjects", _meshObjectBuffer);
            SetComputeBuffer("vertices", _vertexBuffer);
            SetComputeBuffer("indices", _indexBuffer);
            //material.SetBuffer("_MeshObjects", _meshObjectBuffer);
            //material.SetBuffer("_Vertices", _vertexBuffer);
            //material.SetBuffer("_Indices", _indexBuffer);

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetInt("_MeshIndex", i);
            renderer.SetPropertyBlock(block);
        }
    }


    //public Texture SkyboxTexture;
    //m_ComputeShader.SetTexture(0, "_SkyboxTexture", SkyboxTexture);

    //Texture2D<float4> _SkyboxTexture;
    /*
    SampleState sampler_SkyboxTexture;
    static const float PI = 3.14195265f;
    float theata = acos(ray)*/

    // Start is called before the first frame update
    void Start()
    {
        //m_RTTarget = new RenderTexture(m_RTSize.x, m_RTSize.y, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB);
        //GraphicsFormat 渲染紋理的顏色格式
        //m_RTTarget = new RenderTexture(m_RTSize.x, m_RTSize.y, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm);
        m_RTTarget = new RenderTexture(m_RTSize.x, m_RTSize.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        m_RTTarget.enableRandomWrite = true;
        m_RTTarget.Create();
        m_QuadMaterial.SetTexture("_MainTex", m_RTTarget); //"_MainTex"是主要的漫反射紋理
        //m_QuadMaterial.SetTexture(0,"_SkyboxTexture", SkyboxTexture);


        m_SimpleAccelerationStructureDataBuffer = new ComputeBuffer(512, System.Runtime.InteropServices.Marshal.SizeOf(typeof(SphereData))); //(Number of elements in the buffer,Size of one element in the buffer)

        SphereData Data = new SphereData();

        Data.Center = new Vector3(0, -1000.0f, 0.0f);
        Data.Radius = 1000.0f;
        Data.MaterialType = (int)MaterialType.MAT_LAMBERTIAN;
        //Data.MaterialType = (int)MaterialType.MAT_METAL;
        //Data.MaterialType = (int)MaterialType.MAT_DIELECTRIC;
        Data.MaterialAlbedo = new Vector3(0.5f, 0.5f, 0.5f);
        m_SphereArray[m_NumSpheres] = Data;
        m_SphereTimeOffset[m_NumSpheres] = UnityEngine.Random.Range(0, 100.0f);
        m_NumSpheres++;

        Data.Center = new Vector3(0, 1.0f, 0.0f);  //中間球
        Data.Radius = 1.0f; 
        Data.MaterialType = (int)MaterialType.MAT_DIELECTRIC;
        Data.MaterialAlbedo = new Vector3(0.1f, 0.2f, 0.5f);
        Data.MaterialData = new Vector4(1.5f, 0.0f, 0.0f, 0.0f);
        m_SphereArray[m_NumSpheres] = Data;
        m_SphereTimeOffset[m_NumSpheres] = UnityEngine.Random.Range(0, 100.0f);
        m_NumSpheres++;

        Data.Center = new Vector3(-4.0f, 1.0f, 0.0f);      //最後面球(左)
        Data.Radius = 1.0f;
        Data.MaterialType = (int)MaterialType.MAT_LAMBERTIAN;
        Data.MaterialAlbedo = new Vector3(0.4f, 0.2f, 0.1f);
        //Data.MaterialAlbedo = new Vector3(0.8f, 0.4f, 0.6f);
        Data.MaterialData = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
        m_SphereArray[m_NumSpheres] = Data;
        m_SphereTimeOffset[m_NumSpheres] = UnityEngine.Random.Range(0, 100.0f);
        m_NumSpheres++;

        Data.Center = new Vector3(4.0f, 1.0f, 0.0f);  
        Data.Radius = 1.0f;
        Data.MaterialType = (int)MaterialType.MAT_METAL;
        //Data.MaterialType = (int)MaterialType.MAT_DIELECTRIC;
        Data.MaterialAlbedo = new Vector3(0.7f, 0.6f, 0.5f);
        //Data.MaterialAlbedo = new Vector3(0.6f, 0.8f, 0.4f);
        Data.MaterialData = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        m_SphereArray[m_NumSpheres] = Data;
        m_SphereTimeOffset[m_NumSpheres] = UnityEngine.Random.Range(0, 100.0f);
        m_NumSpheres++;

        for (int a = -4; a < 5; a++)
        {
            for (int b = -4; b < 4; b++)
            {
                float Choose_Mat = UnityEngine.Random.Range(0, 1.0f);
                Vector3 Center = new Vector3(a * 1.5f + 1.5f * UnityEngine.Random.Range(0, 1.0f), 0.2f, b * 1.0f + 1.0f * UnityEngine.Random.Range(0, 1.0f));
                Vector3 Dist = Center - new Vector3(4, 0.2f, 0);
                if (Dist.magnitude > 0.9f)
                {
                    if (Choose_Mat < 0.5f)
                    {
                        // diffuse
                        Vector3 Albedo = new Vector3(UnityEngine.Random.Range(0, 1.0f), UnityEngine.Random.Range(0, 1.0f), UnityEngine.Random.Range(0, 1.0f));
                        Data.Center = Center;
                        Data.Radius = 0.2f;
                        Data.MaterialType = (int)MaterialType.MAT_LAMBERTIAN;
                        Data.MaterialAlbedo = Albedo;
                        Data.MaterialData = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
                    }
                    else if (Choose_Mat < 0.8f)
                    {
                        // metal
                        Vector3 Albedo = new Vector3(UnityEngine.Random.Range(0, 1.0f), UnityEngine.Random.Range(0, 1.0f), UnityEngine.Random.Range(0, 1.0f));
                        float Fuzz = UnityEngine.Mathf.Min(UnityEngine.Random.Range(0, 1.0f), 0.5f);
                        Data.Center = Center;
                        Data.Radius = 0.2f;
                        Data.MaterialType = (int)MaterialType.MAT_METAL;
                        Data.MaterialAlbedo = Albedo;
                        Data.MaterialData = new Vector4(Fuzz, 0.0f, 0.0f, 0.0f);
                    }
                    else
                    {
                        Data.Center = Center;
                        Data.Radius = 0.2f;
                        Data.MaterialType = (int)MaterialType.MAT_DIELECTRIC;
                        Data.MaterialData = new Vector4(1.5f, 0.0f, 0.0f, 0.0f);
                    }
                    m_SphereArray[m_NumSpheres] = Data;
                    m_SphereTimeOffset[m_NumSpheres] = UnityEngine.Random.Range(0, 100.0f);
                    m_NumSpheres++;
                }
            }
        }
        m_SimpleAccelerationStructureDataBuffer.SetData(m_SphereArray);
    }
    
    // Update is called once per frame
    void Update()
    {
        //Debug.Log(m_NumSpheres);
        for (int i = 4; i < m_NumSpheres; i++)
            m_SphereArray[i].Center.y = 0.2f + (UnityEngine.Mathf.Sin(m_SphereTimeOffset[i] + (Time.time * 2.0f))) + 1.0f;

        int KernelHandle = m_ComputeShader.FindKernel("CSMain");
        m_ComputeShader.SetVector("TargetSize", new Vector4(m_RTSize.x, m_RTSize.y, UnityEngine.Mathf.Sin(Time.time * 10.0f), m_NumSpheres));
        m_ComputeShader.SetTexture(KernelHandle, "_SkyboxTexture", SkyboxTexture);
        m_ComputeShader.SetFloat("_SkyBoxFactor", SkyboxFactor);
        m_ComputeShader.SetTexture(KernelHandle, "Result", m_RTTarget);   //public void SetTexture(int kernelIndex, string name, Texture texture);
        //m_ComputeShader.SetTexture(KernelHandle, "_SkyboxTexture", SkyboxTexture);
        
        m_SimpleAccelerationStructureDataBuffer.SetData(m_SphereArray);  //Set the buffer with values from an array，public void SetData(Array data);
        m_ComputeShader.SetBuffer(KernelHandle, "SimpleAccelerationStructureData", m_SimpleAccelerationStructureDataBuffer); //public void SetBuffer(int kernelIndex, string name, ComputeBuffer buffer);
        m_ComputeShader.Dispatch(KernelHandle, m_RTSize.x / 8, m_RTSize.y / 8, 1);  //public void Dispatch(int kernelIndex, int threadGroupsX, int threadGroupsY, int threadGroupsZ);
        //SetComputeBuffer("spheres", _sphereBuffer);
        Debug.Log("7");
        //if (_meshObjectsNeedRebuilding)
        //{
            Debug.Log("6");
            RebuildMeshObjectBuffers();
            SetMaterialParameters();
            _meshObjectsNeedRebuilding = false;
        //}
    }




    [Header("Skybox")]
    [SerializeField] private Texture m_skyboxTexture = null;
    [SerializeField] private float m_skyboxFactor = 1.8f;
}
