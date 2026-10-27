#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/**
 * The one piece of a timeline camera bake that depends on the Cinemachine version: updating the
 * CinemachineBrain after each timeline evaluation and reporting which virtual camera is live.
 * Cinemachine 2.x and 3.x each get a small adapter assembly that compiles only when that version is
 * installed and registers itself in TimelineCameraBrains.Factory.
 * Disposing an adapter restores whatever brain or Cinemachine settings it changed for the bake.
 */
public interface ITimelineCameraBrain : IDisposable
{
    /**
     * Positions the camera for the timeline's current evaluation.
     * @param deltaTime Seconds since the previous sample, so damped cameras move as in play mode; below zero snaps with no damping.
     */
    void Update(float deltaTime);

    /** The dominant live virtual camera's GameObject (the incoming camera during a blend), or null when none is live. */
    GameObject LiveCamera { get; }

    /**
     * Adds every virtual camera contributing to the current frame: the live camera and both ends of the
     * active blend, nested blends included. A frame sharing none of these with the previous frame follows a hard cut.
     * @param results Receives the cameras' GameObjects, each once.
     */
    void CollectContributingCameras(List<GameObject> results);
}

/**
 * Registry for the Cinemachine adapter of the installed Cinemachine version.
 */
public static class TimelineCameraBrains
{
    /** Shown when no Cinemachine adapter compiled into the project. */
    public const string REQUIREMENT_MESSAGE = "Baking a Timeline Cinemachine track needs the Cinemachine package (2.x or 3.x).";

    /** Creates an adapter for a camera, or returns null when the camera has no CinemachineBrain. Set by the adapter assembly. */
    public static Func<Camera, ITimelineCameraBrain> Factory = null;

    /**
     * Creates the adapter that drives a camera's CinemachineBrain.
     * @param camera The camera the timeline drives.
     * @returns An adapter to dispose when the bake is done.
     * @throws InvalidOperationException When no Cinemachine adapter is installed, or the camera has no CinemachineBrain.
     */
    public static ITimelineCameraBrain Create(Camera camera)
    {
        if (Factory == null) throw new InvalidOperationException(REQUIREMENT_MESSAGE);
        var brain = Factory(camera);
        if (brain == null) throw new InvalidOperationException("'" + camera.name + "' has no CinemachineBrain, so no timeline drives it.");
        return brain;
    }
}
#endif
