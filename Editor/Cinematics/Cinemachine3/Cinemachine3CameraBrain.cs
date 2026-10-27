#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

/**
 * Timeline camera bake adapter for Cinemachine 3.x. Compiled only when Cinemachine 3.x is installed
 * (BabylonToolkit.Editor.Cinematics.Cinemachine3), and registered as the bake's brain factory on load.
 *
 * Cinemachine 3 only accepts ManualUpdate in ManualUpdate mode, and ignores a second update with the same
 * frame number, so the adapter switches the brain to ManualUpdate for the bake, passes a fresh frame
 * number with every sample, and restores the brain's update method on dispose.
 */
[InitializeOnLoad]
internal sealed class Cinemachine3CameraBrain : ITimelineCameraBrain
{
    private readonly CinemachineBrain brain;
    private readonly CinemachineBrain.UpdateMethods previousUpdateMethod;
    private int updateFrame;

    /** Registers this adapter as the bake's brain factory. */
    static Cinemachine3CameraBrain()
    {
        TimelineCameraBrains.Factory = Create;
    }

    /** Creates an adapter for the camera's CinemachineBrain, or returns null when it has none. */
    private static ITimelineCameraBrain Create(Camera camera)
    {
        var brain = camera.GetComponent<CinemachineBrain>();
        return (brain != null) ? new Cinemachine3CameraBrain(brain) : null;
    }

    private Cinemachine3CameraBrain(CinemachineBrain brain)
    {
        this.brain = brain;
        this.previousUpdateMethod = brain.UpdateMethod;
        this.updateFrame = Time.frameCount;
        brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
    }

    /** Updates the virtual cameras and positions the camera for the current timeline evaluation. */
    public void Update(float deltaTime)
    {
        this.updateFrame++;
        this.brain.ManualUpdate(this.updateFrame, deltaTime);
    }

    /** The live virtual camera's GameObject, or null (also during a blend whose source is not a camera component). */
    public GameObject LiveCamera
    {
        get
        {
            var liveCamera = this.brain.ActiveVirtualCamera as Component;
            return (liveCamera != null) ? liveCamera.gameObject : null;
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
        var nestedBlend = camera as NestedBlendSource;
        if (nestedBlend != null)
        {
            AddBlend(nestedBlend.Blend, results);
            return;
        }
        var component = camera as Component;
        if (component != null && !results.Contains(component.gameObject)) results.Add(component.gameObject);
    }

    /** Adds both ends of a blend. */
    private static void AddBlend(CinemachineBlend blend, List<GameObject> results)
    {
        if (blend == null) return;
        AddCamera(blend.CamA, results);
        AddCamera(blend.CamB, results);
    }

    /** Restores the brain's own update method. */
    public void Dispose()
    {
        this.brain.UpdateMethod = this.previousUpdateMethod;
    }
}
#endif
