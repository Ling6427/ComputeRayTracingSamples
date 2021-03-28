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
        Debug.Log(m_NumSpheres);
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
    }




    [Header("Skybox")]
    [SerializeField] private Texture m_skyboxTexture = null;
    [SerializeField] private float m_skyboxFactor = 1.8f;
}
