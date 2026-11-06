// Minimal engine boundary for executing the production renderer's state transitions offline.
// Unity meshes have an independent lifetime: destroying a GameObject deliberately leaves its sharedMesh alive here too.
using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace UnityEngine
{
    public static class Faults
    {
        public static Action<string, Object> Before;
        public static readonly List<string> Calls = new List<string>();
        public static void Hit(string operation, Object value = null)
        {
            Calls.Add(operation);
            Before?.Invoke(operation, value);
        }
    }
    public enum HideFlags { DontSave }
    public class Object
    {
        private string m_Name;
        private HideFlags m_HideFlags;
        public string name { get => m_Name; set { m_Name = value; Faults.Hit(GetType().Name + ".name", this); } }
        public HideFlags hideFlags { get => m_HideFlags; set { m_HideFlags = value; Faults.Hit(GetType().Name + ".hideFlags", this); } }
        public bool Destroyed;
        public static void Destroy(Object value)
        {
            if (value == null) return;
            Faults.Hit("Destroy." + value.GetType().Name, value);
            value.Destroyed = true;
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector4 { public float x, y, z, w; public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; } }
    public struct Bounds
    {
        public Vector3 Min, Max;
        public void SetMinMax(Vector3 min, Vector3 max) { Min = min; Max = max; }
    }
    public static class Mathf
    {
        public static float Abs(float x) => Math.Abs(x);
        public static float Sqrt(float x) => (float)Math.Sqrt(x);
        public static float Clamp01(float x) => Math.Max(0f, Math.Min(1f, x));
        public static float Round(float x) => (float)Math.Round(x);
        public static float Clamp(float x, float lo, float hi) => Math.Max(lo, Math.Min(hi, x));
    }
    // The frame clock: a test that wants the load report or the load factor moves it.
    public static class Time
    {
        public static float realtimeSinceStartup;
        public static float unscaledDeltaTime = 1f / 60f;
    }
    public class Transform
    {
        private Vector3 m_Position;
        public Transform Parent;
        public Vector3 position { get => m_Position; set { m_Position = value; Faults.Hit("Transform.position"); } }
        public void SetParent(Transform parent, bool worldPositionStays) { Parent = parent; Faults.Hit("Transform.parent"); }
    }
    public class GameObject : Object
    {
        private readonly Dictionary<Type, object> m_Components = new Dictionary<Type, object>();
        public readonly Transform transform = new Transform();
        public static readonly List<GameObject> Created = new List<GameObject>();
        private int m_Layer;
        public int layer { get => m_Layer; set { m_Layer = value; Faults.Hit("GameObject.layer", this); } }
        public bool activeSelf = true;
        public GameObject(string name) { Faults.Hit("GameObject.construct"); this.name = name; Created.Add(this); }
        public void SetActive(bool active) { activeSelf = active; Faults.Hit("GameObject.active", this); }
        public T AddComponent<T>() where T : new()
        {
            var component = new T(); m_Components[typeof(T)] = component;
            if (component is IComponentOwned owned) owned.Owner = this;
            Faults.Hit("GameObject.Add." + typeof(T).Name, this);
            return component;
        }
        public T GetComponent<T>() where T : class => m_Components.TryGetValue(typeof(T), out object value) ? (T)value : null;
    }
    public class Camera { public readonly Transform transform = new Transform(); }
    public class Material : Object { }
    public enum MeshTopology { Triangles }
    public class Mesh : Object
    {
        public static readonly List<Mesh> Created = new List<Mesh>();
        public Mesh() { Faults.Hit("Mesh.construct"); Created.Add(this); }
        private UnityEngine.Rendering.IndexFormat m_IndexFormat;
        private Vector3[] m_Vertices, m_Normals;
        private Vector2[] m_Uv;
        private Bounds m_Bounds;
        public UnityEngine.Rendering.IndexFormat indexFormat { get => m_IndexFormat; set { m_IndexFormat = value; Faults.Hit("Mesh.indexFormat", this); } }
        public Vector3[] vertices { get => m_Vertices; set { m_Vertices = value; Faults.Hit("Mesh.vertices", this); } }
        public Vector3[] normals { get => m_Normals; set { m_Normals = value; Faults.Hit("Mesh.normals", this); } }
        public Vector2[] uv { get => m_Uv; set { m_Uv = value; Faults.Hit("Mesh.uv", this); } }
        public Vector4[] tangents { get; set; }
        public Bounds bounds { get => m_Bounds; set { m_Bounds = value; Faults.Hit("Mesh.bounds", this); } }
        public int[] Indices;
        public MeshTopology Topology;
        public int Submesh;
        public bool RecalculateBounds;
        public bool? Uploaded;
        public void SetIndices(int[] indices, MeshTopology topology, int submesh, bool recalculateBounds)
        {
            Indices = indices; Topology = topology; Submesh = submesh; RecalculateBounds = recalculateBounds;
            Faults.Hit("Mesh.indices", this);
        }
        public void UploadMeshData(bool noLongerReadable) { Uploaded = noLongerReadable; Faults.Hit("Mesh.upload", this); }
    }
    public class MeshFilter
    {
        private Mesh m_Mesh;
        public Mesh sharedMesh { get => m_Mesh; set { m_Mesh = value; Faults.Hit("MeshFilter.mesh"); } }
    }
    public interface IComponentOwned { GameObject Owner { set; } }
    // Unity's null: a component of a destroyed GameObject compares equal to null.
    public class MeshRenderer : Object, IComponentOwned
    {
        public GameObject Owner { get; set; }
        private static bool Gone(MeshRenderer r) => ReferenceEquals(r, null) || r.Destroyed || r.Owner != null && r.Owner.Destroyed;
        public static bool operator ==(MeshRenderer a, MeshRenderer b) => ReferenceEquals(a, b) || Gone(a) && Gone(b);
        public static bool operator !=(MeshRenderer a, MeshRenderer b) => !(a == b);
        public override bool Equals(object other) => other is MeshRenderer r && this == r;
        public override int GetHashCode() => base.GetHashCode();
        private Material m_Material;
        private uint m_Layer;
        private UnityEngine.Rendering.LightProbeUsage m_LightProbe;
        private UnityEngine.Rendering.ReflectionProbeUsage m_ReflectionProbe;
        private UnityEngine.Rendering.ShadowCastingMode m_Shadow;
        private UnityEngine.Rendering.MotionVectorGenerationMode m_Motion;
        private bool m_Receive, m_Occlusion;
        public Material sharedMaterial { get => m_Material; set { m_Material = value; Faults.Hit("Renderer.material"); } }
        public uint renderingLayerMask { get => m_Layer; set { m_Layer = value; Faults.Hit("Renderer.layer"); } }
        public UnityEngine.Rendering.LightProbeUsage lightProbeUsage { get => m_LightProbe; set { m_LightProbe = value; Faults.Hit("Renderer.lightProbe"); } }
        public UnityEngine.Rendering.ReflectionProbeUsage reflectionProbeUsage { get => m_ReflectionProbe; set { m_ReflectionProbe = value; Faults.Hit("Renderer.reflectionProbe"); } }
        public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode { get => m_Shadow; set { m_Shadow = value; Faults.Hit("Renderer.shadow"); } }
        public UnityEngine.Rendering.MotionVectorGenerationMode motionVectorGenerationMode { get => m_Motion; set { m_Motion = value; Faults.Hit("Renderer.motion"); } }
        public bool receiveShadows { get => m_Receive; set { m_Receive = value; Faults.Hit("Renderer.receive"); } }
        public bool allowOcclusionWhenDynamic { get => m_Occlusion; set { m_Occlusion = value; Faults.Hit("Renderer.occlusion"); } }
    }
}
namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }
    public enum LightProbeUsage { BlendProbes }
    public enum ReflectionProbeUsage { BlendProbes }
    public enum ShadowCastingMode { On, Off }
    public enum MotionVectorGenerationMode { Camera }
}
namespace UnityEngine.Scripting { public class PreserveAttribute : Attribute { } }
namespace Unity.Collections
{
    public enum Allocator { Temp }
    public struct NativeArray<T> : IDisposable
    {
        private readonly T[] m_Items;
        public NativeArray(T[] items) { m_Items = items; }
        public int Length => m_Items.Length;
        public T this[int i] => m_Items[i];
        public void Dispose() { }
    }
}
namespace Unity.Entities
{
    public struct Entity : IEquatable<Entity>
    {
        public int Index, Version;
        public bool Equals(Entity other) => Index == other.Index && Version == other.Version;
        public override bool Equals(object other) => other is Entity entity && Equals(entity);
        public override int GetHashCode() => Index;
    }
    public struct ComponentType { public static ComponentType ReadOnly<T>() => default; }
    public class EntityQueryDesc { public ComponentType[] All, None; }
    public class EntityManager
    {
        internal readonly Dictionary<Entity, object> Items = new Dictionary<Entity, object>();
        public bool Exists(Entity entity) => Items.ContainsKey(entity);
        public bool HasComponent<T>(Entity entity) => Items.ContainsKey(entity) && Items[entity] is T;
        public T GetComponentData<T>(Entity entity) => (T)Items[entity];
        public void SetComponentData<T>(Entity entity, T item) => Items[entity] = item;
        public void DestroyEntity(Entity entity) => Items.Remove(entity);
        public void DestroyEntity(EntityQuery query) => Items.Clear();
    }
    public class EntityQuery
    {
        private readonly EntityManager m_Manager;
        private bool m_Changed;
        public EntityQuery(EntityManager manager) { m_Manager = manager; }
        public bool IsEmpty => m_Changed || m_Manager.Items.Count == 0;
        public void SetChangedVersionFilter(ComponentType component) { m_Changed = true; }
        public int CalculateEntityCount() => m_Changed ? 0 : m_Manager.Items.Count;
        public NativeArray<Entity> ToEntityArray(Allocator allocator) => new NativeArray<Entity>(m_Changed ? Array.Empty<Entity>() : m_Manager.Items.Keys.ToArray());
        public NativeArray<T> ToComponentDataArray<T>(Allocator allocator) => new NativeArray<T>(m_Changed ? Array.Empty<T>() : m_Manager.Items.Values.Cast<T>().ToArray());
    }
    public class World { public T GetOrCreateSystemManaged<T>() where T : new() => new T(); }
}
namespace Colossal.Serialization.Entities { public enum Purpose { LoadGame } }
namespace Game
{
    public enum GameMode { Game, MainMenu }
    public class GameSystemBase
    {
        public readonly EntityManager EntityManager = new EntityManager();
        protected readonly World World = new World();
        protected EntityQuery GetEntityQuery(ComponentType type) => new EntityQuery(EntityManager);
        protected EntityQuery GetEntityQuery(EntityQueryDesc description) => new EntityQuery(EntityManager);
        protected virtual void OnCreate() { }
        protected virtual void OnDestroy() { }
        protected virtual void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode) { }
        protected virtual void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode) { }
        protected virtual void OnUpdate() { }
        public void Initialize() => OnCreate();
    }
}
namespace Game.Common { public struct Deleted { } }
namespace Game.Rendering
{
    public class CameraController { public Vector3 pivot; }
    public class CameraUpdateSystem { public Camera activeCamera; public CameraController orbitCameraController, activeCameraController; }
    public class LightingSystem { public float dayLightBrightness; }
}
namespace Game.SceneFlow
{
    public class GameManager { public static GameManager instance = new GameManager(); public Game.GameMode gameMode = Game.GameMode.Game; }
}
namespace CDLC.TS
{
    // The renderer reads only these value fields; serialization and FixedString are outside this test's scope.
    public struct TsItem
    {
        public uint TypeHash, SupportHash, Group;
        public Entity Edge;
        public Vector3 Position;
        public float Yaw, Reach, OffsetX, Turn;
        public byte Slot, Lane;
        public bool Manual, Auto, Back;
        public int TiltSteps;
        public byte Style;
        public uint BatchId;
        public float Rise, Clearance;
        public string Spec;
    }
    public static class Mod
    {
        public static string Directory;
        public static readonly Log log = new Log();
        public class Log { public readonly List<string> Lines = new List<string>(); public void Info(string text) => Lines.Add(text); public void Error(string text) { } }
    }
    public static class PlacementTool
    {
        public static IEnumerable<string> PreviewFaceKeys;
        public static int Forgotten;
        public static void ForgetHistory() => Forgotten++;
    }
    public static class TsBatchSystem
    {
        public static void Note(Entity entity, uint batchId) { }
        public static void Gone(Entity entity) { }
    }
    public class AreaFitSystem
    {
        public static AreaFitSystem Instance;
        public bool TakeReady(List<Entity> into) => false;
        public AreaFit FitFor(Entity entity, ItemRecord record) => null;
        public GroundProfile GroundFor(Entity entity, ItemRecord record) => null;
        public MarkingGround.Laid LaidFor(Entity entity, ItemRecord record) => null;
        public int NearWaitingValue;
        public int NearWaiting => NearWaitingValue;
        public int GroundsMade => 0;
        public double LoadMs => 0.0;
        public string Describe() => "";
        public string ReadSummary => "";
        public string DescribeWorker() => "";
    }
}
namespace CDLC.TS.Render
{
    public static class LedScreens { public static LedGrid Grid(SignInstance instance) => null; }
    public static class FacePainter { public static int Waiting => 0; public static int Pending => 0; public static int Painted => 0; public static int Encoded => 0; }
    public static class SlowLog
    {
        public static void Add(string source, double elapsed, string details) { }
        public static bool IsSlow(double elapsed) => false;
        public static string Memory() => "";
        public static void Tick() { }
    }
    public static class TsMaterials
    {
        private static readonly Material s_Material = new Material();
        public static long FaceTextureBytes => 0;
        public static int FaceTextureCount => 0;
        public static int MadeCount { get; private set; }

        /// <summary>What a part's material needs, by its name (the tests set it); default: it is there. A material that is asked for with OfBody is made, and is there from then on.</summary>
        public static System.Func<string, MaterialNeed> NeedRule;
        public static readonly HashSet<string> Made = new HashSet<string>();
        public static readonly List<string> Asked = new List<string>();
        /// <summary>Parts whose material takes this long (ms) to be given: a slow part, so that the half of a budget is spent by it.</summary>
        public static readonly Dictionary<string, int> SleepMs = new Dictionary<string, int>();
        public static int WarmCalls;
        public static string WarmSummary => "";
        public static MaterialNeed Need(string material)
        {
            Asked.Add(material);
            return Made.Contains(material) || NeedRule == null ? MaterialNeed.Have : NeedRule(material);
        }

        public static double WarmStep(double budgetMs)
        {
            WarmCalls++;
            return 0.0;
        }

        public static void PumpMarking()
        {
        }

        public static Material Metal() => s_Material;
        public static Material Concrete() => s_Material;
        public static Material LedMaterial(int colour) => s_Material;
        public static Material Marking(MarkingColour colour) => s_Material;
        public static Material Face(string key) => s_Material;
        public static Material OfBody(string material)
        {
            if (material.StartsWith(ChunkBuilder.FacePrefix, System.StringComparison.Ordinal))
                return null;
            if (SleepMs.TryGetValue(material, out int sleep))
                System.Threading.Thread.Sleep(sleep);
            if (NeedRule != null && !Made.Contains(material) && NeedRule(material) != MaterialNeed.Have && Made.Add(material))
                MadeCount++;
            return s_Material;
        }
        public static int PumpFaces(double budget) => 0;
        public static void SetNight(float night) { }
        public static void Prune(HashSet<string> keep) { }
        public static void Clear() { }
    }
}
