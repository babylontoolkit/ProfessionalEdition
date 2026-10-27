#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Cinemachine;
using UnityEditor;
using UnityEngine;

/**
 * Timeline camera bake adapter for Cinemachine 2.x. Compiled only when Cinemachine 2.x is installed
 * (BabylonToolkit.Editor.Cinematics.Cinemachine2), and registered as the bake's brain factory on load.
 * Samples are stepped with CinemachineCore.UniformDeltaTimeOverride, which the adapter restores on dispose.
 */
[InitializeOnLoad]
internal sealed class Cinemachine2CameraBrain : ITimelineCameraBrain
{
    private readonly CinemachineBrain brain;
    private readonly float previousDeltaTimeOverride;

    /** Registers this adapter as the bake's brain factory. */
    static Cinemachine2CameraBrain()
    {
        TimelineCameraBrains.Factory = Create;
    }

    /** Creates an adapter for the camera's CinemachineBrain, or returns null when it has none. */
    private static ITimelineCameraBrain Create(Camera camera)
    {
        var brain = camera.GetComponent<CinemachineBrain>();
        return (brain != null) ? new Cinemachine2CameraBrain(brain) : null;
    }

    private Cinemachine2CameraBrain(CinemachineBrain brain)
    {
        this.brain = brain;
        this.previousDeltaTimeOverride = CinemachineCore.UniformDeltaTimeOverride;
    }

    /** Updates the virtual cameras and positions the camera for the current timeline evaluation. */
    public void Update(float deltaTime)
    {
        CinemachineCore.UniformDeltaTimeOverride = deltaTime;
        this.brain.ManualUpdate();
    }

    /** The live virtual camera's GameObject, or null. */
    public GameObject LiveCamera
    {
        get
        {
            var liveCamera = this.brain.ActiveVirtualCamera;
            return (liveCamera != null) ? liveCamera.VirtualCameraGameObject : null;
        }
    }

    /** Adds the live camera and both ends of the active blend, nested blends included. */
    public void CollectContributingCameras(List<GameObject> results)
    {
        AddCamera(this.brain.ActiveVirtualCamera, results);
        AddBlend(this.brain.ActiveBlend, results);
    }

    /** Adds a virtual camera, or the cameras of the blend it stands for. */
    private static void AddCamera(ICinemachineCamera camera, List<GameObject> results)
    {
        if (camera == null) return;
        // A blend interrupted by another blend is wrapped in Cinemachine 2's internal BlendSourceVirtualCamera,
        // which has no GameObject; its public Blend property holds the cameras it mixes.
        if (camera.VirtualCameraGameObject == null)
        {
            var blendProperty = camera.GetType().GetProperty("Blend", BindingFlags.Public | BindingFlags.Instance);
            AddBlend((blendProperty != null) ? blendProperty.GetValue(camera) as CinemachineBlend : null, results);
            return;
        }
        if (!results.Contains(camera.VirtualCameraGameObject)) results.Add(camera.VirtualCameraGameObject);
    }

    /** Adds both ends of a blend. */
    private static void AddBlend(CinemachineBlend blend, List<GameObject> results)
    {
        if (blend == null) return;
        AddCamera(blend.CamA, results);
        AddCamera(blend.CamB, results);
    }

    /** Restores Cinemachine's global delta time override. */
    public void Dispose()
    {
        CinemachineCore.UniformDeltaTimeOverride = this.previousDeltaTimeOverride;
    }
}
#endif
