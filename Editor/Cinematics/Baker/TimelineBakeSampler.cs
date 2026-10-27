#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using CanvasTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;

/// <summary>
/// Steps a director through its Timeline frame by frame and records what Unity evaluated (D14).
/// The frame loop is the old camera baker's: rebuild the graph, then per frame set the time, Evaluate(), update the
/// CinemachineBrain with a fixed delta time, and record. The caller reloads the scenes afterwards, so nothing the
/// evaluation changed survives.
/// </summary>
internal static class TimelineBakeSampler
{
    /// <summary>
    /// D14 fallback switch: sample through AnimationMode when edit-mode Evaluate does not pose Animation tracks.
    /// A static readonly field rather than a const, so the branch it guards compiles without an unreachable-code warning.
    /// </summary>
    internal static readonly bool UseAnimationModeSampling = false;

    /// <summary>Type name of Cinemachine's Timeline track, matched by name because this assembly does not reference Cinemachine.</summary>
    private const string CINEMACHINE_TRACK_TYPE = "CinemachineTrack";
    /// <summary>The CinemachineShot field holding its camera (an ExposedReference), read by reflection.</summary>
    private const string SHOT_CAMERA_FIELD = "VirtualCamera";
    /// <summary>The ExposedReference field naming the director reference.</summary>
    private const string EXPOSED_NAME_FIELD = "exposedName";
    /// <summary>The ExposedReference field used when the director has no value for the name.</summary>
    private const string EXPOSED_DEFAULT_FIELD = "defaultValue";
    /// <summary>The CinemachineTrack field holding the track priority (Cinemachine 3 only).</summary>
    private const string TRACK_PRIORITY_FIELD = "TrackPriority";
    /// <summary>Delta time that makes Cinemachine snap with no damping history; used for the first sample.</summary>
    private const float SNAP_DELTA_TIME = -1.0f;
    /// <summary>The progress bar updates once every this many frames.</summary>
    private const int PROGRESS_INTERVAL_FRAMES = 30;
    /// <summary>Title of the progress bar shown in an interactive Editor.</summary>
    private const string PROGRESS_TITLE = "Bake Timelines";

    /// <summary>Samples the director and returns the bag pieces, the glTF animations and the skipped lines.</summary>
    /// <param name="director">The director to step; its scene is reloaded by the caller afterwards.</param>
    /// <param name="timeline">The director's Timeline asset.</param>
    /// <param name="objectsWithActivationEntries">Objects that already got Activation-track or Control-activation entries (D11).</param>
    /// <param name="skippedLines">Receives one line per item the bake cannot carry.</param>
    /// <returns>The camera, animations, curves and recorded activations.</returns>
    /// <exception cref="OperationCanceledException">The user cancelled the progress bar.</exception>
    public static TimelineSampleResult Sample(PlayableDirector director, TimelineAsset timeline, ISet<GameObject> objectsWithActivationEntries, List<string> skippedLines)
    {
        var result = new TimelineSampleResult { FrameRate = (float)timeline.editorSettings.frameRate };
        result.FrameCount = Mathf.FloorToInt((float)director.duration * result.FrameRate) + 1;
        List<TrackAsset> activeTracks = TimelineTrackExporter.GetActiveTracks(timeline);
        TrackAsset cameraTrack = ChooseCameraTrack(director, activeTracks, skippedLines);
        Camera brainCamera = GetBrainCamera(director, cameraTrack);
        List<RecordedObject> recordedObjects = CreateRecorders(director, activeTracks, brainCamera, result);

        using (ITimelineCameraBrain brainAdapter = CreateBrainAdapter(brainCamera, skippedLines))
        {
            TimelineCameraRecording cameraRecording = (brainAdapter != null) ? new TimelineCameraRecording(brainCamera, brainAdapter, cameraTrack, director.transform) : null;
            RunFrameLoop(director, brainAdapter, cameraRecording, recordedObjects, result);
            if (cameraRecording != null) result.Camera = cameraRecording.ToBagEntry();
        }

        foreach (RecordedObject recordedObject in recordedObjects)
        {
            recordedObject.Recorder.Build(recordedObject.ClipKey, recordedObject.JumpFrames, result.FrameRate, objectsWithActivationEntries, result);
        }
        return result;
    }

    /// <summary>
    /// cinemachine-port T12 (DR-12.1): every unmuted Cinemachine track's shots for the live TOOLKIT.CinemachineTimeline. Shots
    /// are read by reflection, so this assembly needs no Cinemachine reference: `clip.asset.VirtualCamera` (an
    /// ExposedReference) resolves through the director like `ExposedReference.Resolve`, `TrackPriority` is read by field
    /// (0 when missing, as on Cinemachine 2 tracks), and the bound brain comes from the track binding. Cameras and brains
    /// are written as `{binding}` references, converted at export.
    /// </summary>
    /// <param name="director">The director being baked.</param>
    /// <param name="activeTracks">The timeline's unmuted output tracks.</param>
    /// <returns>One `{priority, brain, shots}` entry per Cinemachine track (empty without any).</returns>
    public static List<object> CollectCinemachineTracks(PlayableDirector director, List<TrackAsset> activeTracks)
    {
        var tracks = new List<object>();
        foreach (TrackAsset track in activeTracks)
        {
            if (track.GetType().Name != CINEMACHINE_TRACK_TYPE) continue;
            var shots = new List<object>();
            foreach (TimelineClip clip in track.GetClips()) shots.Add(ShotEntry(director, clip));
            tracks.Add(new Dictionary<string, object>
            {
                ["priority"] = ReadTrackPriority(track),
                ["brain"] = TimelineExportTool.BindingReference(director.GetGenericBinding(track)),
                ["shots"] = shots
            });
        }
        return tracks;
    }

    /// <summary>One Cinemachine shot clip as `{displayName, camera, start, duration, ease/blend durations, mix curves}`.</summary>
    private static Dictionary<string, object> ShotEntry(PlayableDirector director, TimelineClip clip)
    {
        return new Dictionary<string, object>
        {
            ["displayName"] = clip.displayName,
            ["camera"] = TimelineExportTool.BindingReference(ResolveShotCamera(director, clip.asset)),
            ["start"] = (float)clip.start,
            ["duration"] = (float)clip.duration,
            ["easeInDuration"] = (float)clip.easeInDuration,
            ["easeOutDuration"] = (float)clip.easeOutDuration,
            ["blendInDuration"] = (float)clip.blendInDuration,
            ["blendOutDuration"] = (float)clip.blendOutDuration,
            ["mixInCurve"] = ParticleExportSchema.WriteCurve(CinemachineExportTool.ToCurveData(clip.mixInCurve)),
            ["mixOutCurve"] = ParticleExportSchema.WriteCurve(CinemachineExportTool.ToCurveData(clip.mixOutCurve))
        };
    }

    /// <summary>
    /// The camera a CinemachineShot shows: its `VirtualCamera` exposed reference resolved through the director, else the
    /// reference's default value (Unity `ExposedReference.Resolve`); null when the shot has none.
    /// </summary>
    private static UnityEngine.Object ResolveShotCamera(PlayableDirector director, UnityEngine.Object shotAsset)
    {
        if (shotAsset == null) return null;
        System.Reflection.FieldInfo cameraField = shotAsset.GetType().GetField(SHOT_CAMERA_FIELD);
        if (cameraField == null) return null;
        object exposedReference = cameraField.GetValue(shotAsset);
        Type referenceType = exposedReference.GetType();
        object exposedName = referenceType.GetField(EXPOSED_NAME_FIELD).GetValue(exposedReference);
        bool isValid;
        UnityEngine.Object resolved = director.GetReferenceValue((PropertyName)exposedName, out isValid);
        if (isValid && resolved != null) return resolved;
        return referenceType.GetField(EXPOSED_DEFAULT_FIELD).GetValue(exposedReference) as UnityEngine.Object;
    }

    /// <summary>A Cinemachine track's `TrackPriority`, or 0 when the track type has none.</summary>
    private static int ReadTrackPriority(TrackAsset track)
    {
        System.Reflection.FieldInfo priorityField = track.GetType().GetField(TRACK_PRIORITY_FIELD);
        object priority = (priorityField != null) ? priorityField.GetValue(track) : null;
        return (priority is int) ? (int)priority : 0;
    }

    /// <summary>
    /// Names a shot after the director's direct child that holds the live virtual camera (shot prefabs
    /// parented to the director), else after the virtual camera's parent (a shot group holding its Start
    /// and End cameras), else after the virtual camera itself.
    /// </summary>
    internal static string ResolveFallbackShotName(GameObject liveCamera, Transform directorRoot)
    {
        if (liveCamera == null) return "None";
        Transform cameraTransform = liveCamera.transform;
        for (Transform ancestor = cameraTransform; ancestor.parent != null; ancestor = ancestor.parent)
        {
            if (ancestor.parent == directorRoot) return ancestor.name;
        }
        return (cameraTransform.parent != null) ? cameraTransform.parent.name : liveCamera.name;
    }

    /// <summary>True when no camera contributing to this frame contributed to the previous one.</summary>
    internal static bool IsHardCut(List<GameObject> previousCameras, List<GameObject> currentCameras)
    {
        if (previousCameras.Count == 0 && currentCameras.Count == 0) return false;
        foreach (var camera in currentCameras)
        {
            if (previousCameras.Contains(camera)) return false;
        }
        return true;
    }

    /// <summary>The first CinemachineTrack in active-track order; later ones bound to a different brain add a skipped line.</summary>
    private static TrackAsset ChooseCameraTrack(PlayableDirector director, List<TrackAsset> activeTracks, List<string> skippedLines)
    {
        TrackAsset chosenTrack = null;
        UnityEngine.Object chosenBrain = null;
        foreach (TrackAsset track in activeTracks)
        {
            if (track.GetType().Name != CINEMACHINE_TRACK_TYPE) continue;
            UnityEngine.Object brain = director.GetGenericBinding(track);
            if (chosenTrack == null)
            {
                chosenTrack = track;
                chosenBrain = brain;
            }
            else if (brain != chosenBrain)
            {
                skippedLines.Add(track.name + " — a second Cinemachine track bound to another brain is not carried");
            }
        }
        return chosenTrack;
    }

    /// <summary>The Camera on the chosen track's bound CinemachineBrain, or null when there is no track or binding.</summary>
    private static Camera GetBrainCamera(PlayableDirector director, TrackAsset cameraTrack)
    {
        if (cameraTrack == null) return null;
        Behaviour brain = director.GetGenericBinding(cameraTrack) as Behaviour;
        return (brain != null) ? brain.GetComponent<Camera>() : null;
    }

    /// <summary>The Cinemachine adapter for the brain camera, or null (with a skipped line when a camera was bound but has no adapter).</summary>
    private static ITimelineCameraBrain CreateBrainAdapter(Camera brainCamera, List<string> skippedLines)
    {
        if (brainCamera == null) return null;
        try
        {
            return TimelineCameraBrains.Create(brainCamera);
        }
        catch (InvalidOperationException exception)
        {
            skippedLines.Add("Cinemachine track — " + exception.Message);
            return null;
        }
    }

    /// <summary>One recorder per distinct GameObject bound to an active Animation track, with its clip key and jump frames.</summary>
    private static List<RecordedObject> CreateRecorders(PlayableDirector director, List<TrackAsset> activeTracks, Camera brainCamera, TimelineSampleResult result)
    {
        var recordedObjects = new List<RecordedObject>();
        var tracksByObject = new Dictionary<GameObject, List<AnimationTrack>>();
        foreach (TrackAsset track in activeTracks)
        {
            AnimationTrack animationTrack = track as AnimationTrack;
            GameObject boundObject = (animationTrack != null) ? ToGameObject(director.GetGenericBinding(animationTrack)) : null;
            if (boundObject == null) continue;
            List<AnimationTrack> objectTracks;
            if (!tracksByObject.TryGetValue(boundObject, out objectTracks))
            {
                objectTracks = new List<AnimationTrack>();
                tracksByObject[boundObject] = objectTracks;
                recordedObjects.Add(new RecordedObject { BoundObject = boundObject });
            }
            objectTracks.Add(animationTrack);
        }

        var usedClipKeys = new HashSet<string>();
        foreach (RecordedObject recordedObject in recordedObjects)
        {
            recordedObject.ClipKey = MakeUniqueClipKey(recordedObject.BoundObject.name, usedClipKeys);
            recordedObject.JumpFrames = TimelineJumpFrames.Collect(tracksByObject[recordedObject.BoundObject], result.FrameRate, result.FrameCount);
            recordedObject.Recorder = new TimelineObjectRecorder(recordedObject.BoundObject, brainCamera, result.FrameCount);
        }
        return recordedObjects;
    }

    /// <summary>The bound object's name, de-duplicated with "_2", "_3" within one director.</summary>
    private static string MakeUniqueClipKey(string objectName, HashSet<string> usedClipKeys)
    {
        string clipKey = objectName;
        for (int suffix = 2; usedClipKeys.Contains(clipKey); suffix++) clipKey = objectName + "_" + suffix;
        usedClipKeys.Add(clipKey);
        return clipKey;
    }

    /// <summary>Steps every frame: set the time, Evaluate, optionally sample AnimationMode, update the brain, record.</summary>
    private static void RunFrameLoop(PlayableDirector director, ITimelineCameraBrain brainAdapter, TimelineCameraRecording cameraRecording, List<RecordedObject> recordedObjects, TimelineSampleResult result)
    {
        foreach (RecordedObject recordedObject in recordedObjects) recordedObject.Recorder.CaptureAuthored();
        bool showProgress = !Application.isBatchMode;
        if (UseAnimationModeSampling) AnimationMode.StartAnimationMode();
        try
        {
            director.RebuildGraph();
            for (int i = 0; i < result.FrameCount; i++)
            {
                if (showProgress && i % PROGRESS_INTERVAL_FRAMES == 0 && EditorUtility.DisplayCancelableProgressBar(PROGRESS_TITLE, director.name + ": frame " + i + " of " + result.FrameCount, (float)i / result.FrameCount))
                    throw new OperationCanceledException("Bake cancelled.");

                director.time = Math.Min(i / (double)result.FrameRate, director.duration);
                director.Evaluate();
                if (UseAnimationModeSampling) SampleAnimationMode(director);
                if (brainAdapter != null) brainAdapter.Update((i == 0) ? SNAP_DELTA_TIME : 1.0f / result.FrameRate);
                if (cameraRecording != null) cameraRecording.RecordFrame(i, director.time);
                foreach (RecordedObject recordedObject in recordedObjects) recordedObject.Recorder.RecordFrame(i);
            }
        }
        finally
        {
            if (UseAnimationModeSampling) AnimationMode.StopAnimationMode();
            if (showProgress) EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>D14 fallback: poses the Animation tracks through AnimationMode at the director's current time.</summary>
    private static void SampleAnimationMode(PlayableDirector director)
    {
        AnimationMode.BeginSampling();
        AnimationMode.SamplePlayableGraph(director.playableGraph, 0, (float)director.time);
        AnimationMode.EndSampling();
    }

    /// <summary>The GameObject a binding names: the object itself, or a component's GameObject.</summary>
    internal static GameObject ToGameObject(UnityEngine.Object binding)
    {
        GameObject gameObject = binding as GameObject;
        if (gameObject != null) return gameObject;
        Component component = binding as Component;
        return (component != null) ? component.gameObject : null;
    }

    /// <summary>One recorded bound object with the clip key and jump frames it is built with.</summary>
    private sealed class RecordedObject
    {
        /// <summary>The Animation track's bound GameObject.</summary>
        public GameObject BoundObject;
        /// <summary>The de-duplicated group key.</summary>
        public string ClipKey;
        /// <summary>The object's jump frames (DR-9.4).</summary>
        public int[] JumpFrames;
        /// <summary>The per-frame recorder of the object's subtree.</summary>
        public TimelineObjectRecorder Recorder;
    }
}

/// <summary>What one sampling pass produced, ready to merge into the DR-4 bag.</summary>
internal sealed class TimelineSampleResult
{
    /// <summary>The camera entry, or null without a usable Cinemachine track.</summary>
    public Dictionary<string, object> Camera;
    /// <summary>ITimelineAnimationData entries.</summary>
    public List<object> Animations = new List<object>();
    /// <summary>ITimelinePropertyData entries.</summary>
    public List<object> Curves = new List<object>();
    /// <summary>ITimelineActivationData entries recorded from Animation tracks.</summary>
    public List<object> Activations = new List<object>();
    /// <summary>The glTF animations for Core.</summary>
    public List<TimelineAnimationBake> AnimationBakes = new List<TimelineAnimationBake>();
    /// <summary>Number of baked frames.</summary>
    public int FrameCount;
    /// <summary>Samples per second.</summary>
    public float FrameRate;
}

/// <summary>
/// Records the CinemachineBrain camera each frame: local pose and vertical field of view (8 values), the shot segment of
/// every frame (a hard cut starts the next one) and each shot's name (DR-9.2).
/// </summary>
internal sealed class TimelineCameraRecording
{
    private readonly Camera camera;
    private readonly ITimelineCameraBrain brainAdapter;
    private readonly TrackAsset cameraTrack;
    private readonly Transform directorRoot;
    private readonly List<float> frameData = new List<float>();
    private readonly List<int> frameSegments = new List<int>();
    private readonly List<string> shotNames = new List<string>();
    private List<GameObject> previousCameras = new List<GameObject>();
    private List<GameObject> currentCameras = new List<GameObject>();

    /// <summary>Starts an empty recording of the brain camera.</summary>
    /// <param name="camera">The camera the brain drives.</param>
    /// <param name="brainAdapter">The Cinemachine adapter, already created.</param>
    /// <param name="cameraTrack">The chosen CinemachineTrack, whose clips name the shots.</param>
    /// <param name="directorRoot">The director's transform, for the fallback shot name.</param>
    public TimelineCameraRecording(Camera camera, ITimelineCameraBrain brainAdapter, TrackAsset cameraTrack, Transform directorRoot)
    {
        this.camera = camera;
        this.brainAdapter = brainAdapter;
        this.cameraTrack = cameraTrack;
        this.directorRoot = directorRoot;
    }

    /// <summary>Records frame frameIndex after the brain update; a hard cut starts a new shot.</summary>
    /// <param name="frameIndex">The frame being recorded.</param>
    /// <param name="frameTime">The evaluated timeline time in seconds, in the director's double precision so clip bounds compare as Timeline compares them.</param>
    public void RecordFrame(int frameIndex, double frameTime)
    {
        this.currentCameras.Clear();
        this.brainAdapter.CollectContributingCameras(this.currentCameras);
        // A new segment starts only at a hard cut: no camera carries over from the previous frame, even
        // when the next shot's own Start-to-End blend begins on the very same frame.
        if (frameIndex == 0 || TimelineBakeSampler.IsHardCut(this.previousCameras, this.currentCameras))
        {
            this.shotNames.Add(ResolveShotName(frameTime));
        }
        List<GameObject> swap = this.previousCameras;
        this.previousCameras = this.currentCameras;
        this.currentCameras = swap;

        Transform cameraTransform = this.camera.transform;
        Vector3 position = cameraTransform.localPosition;
        Quaternion rotation = cameraTransform.localRotation;
        this.frameData.Add(position.x);
        this.frameData.Add(position.y);
        this.frameData.Add(position.z);
        this.frameData.Add(rotation.x);
        this.frameData.Add(rotation.y);
        this.frameData.Add(rotation.z);
        this.frameData.Add(rotation.w);
        this.frameData.Add(this.camera.fieldOfView);
        this.frameSegments.Add(this.shotNames.Count - 1);
    }

    /// <summary>The ITimelineCameraData entry, with the camera as a binding reference.</summary>
    /// <returns>The bag's camera dictionary.</returns>
    public Dictionary<string, object> ToBagEntry()
    {
        return new Dictionary<string, object>
        {
            ["target"] = TimelineExportTool.BindingReference(this.camera.gameObject),
            ["frameData"] = this.frameData,
            ["frameSegments"] = this.frameSegments,
            ["shotNames"] = this.shotNames
        };
    }

    /// <summary>
    /// The display name of the camera-track clip with the greatest start whose [start, end) holds the time, else the
    /// fallback rule. Clips with equal starts (a shot's Start and End cameras) go to the one with the shorter blend-in,
    /// the camera that is fully live at the cut; Timeline's in-memory clip order is not stable for equal starts.
    /// </summary>
    private string ResolveShotName(double frameTime)
    {
        TimelineClip showingClip = null;
        foreach (TimelineClip clip in this.cameraTrack.GetClips())
        {
            bool containsTime = (clip.start <= frameTime && frameTime < clip.end);
            if (containsTime && (showingClip == null || IsLaterShot(clip, showingClip))) showingClip = clip;
        }
        if (showingClip != null) return showingClip.displayName;
        return TimelineBakeSampler.ResolveFallbackShotName(this.brainAdapter.LiveCamera, this.directorRoot);
    }

    /// <summary>True when candidate starts later than current, or at the same time with a shorter blend-in.</summary>
    private static bool IsLaterShot(TimelineClip candidate, TimelineClip current)
    {
        if (candidate.start != current.start) return candidate.start > current.start;
        return candidate.mixInDuration < current.mixInDuration;
    }
}

/// <summary>Records one bound object's subtree per frame and turns the samples into channels, curves and activations.</summary>
internal sealed class TimelineObjectRecorder
{
    /// <summary>Values per frame of a translation or scale channel.</summary>
    private const int VECTOR3_WIDTH = 3;
    /// <summary>Values per frame of a rotation channel.</summary>
    private const int ROTATION_WIDTH = 4;
    /// <summary>Values per frame of a colour curve (rgba).</summary>
    private const int COLOR_WIDTH = 4;
    /// <summary>Values per frame of a scalar curve or of an activity record.</summary>
    private const int SCALAR_WIDTH = 1;
    /// <summary>An activity record stores activeSelf as 1 (active) or 0.</summary>
    private const float ACTIVE_VALUE = 1.0f;

    private readonly GameObject boundObject;
    private readonly int frameCount;
    private readonly List<RecordedChannel> channels = new List<RecordedChannel>();
    private readonly List<RecordedCurve> curves = new List<RecordedCurve>();
    private readonly List<RecordedActivity> activities = new List<RecordedActivity>();

    /// <summary>Prepares one sampled value set for every recorded property of the bound object's subtree.</summary>
    /// <param name="boundObject">The Animation track's bound GameObject.</param>
    /// <param name="excludedBrainCamera">The CinemachineBrain camera, recorded by the camera entry instead; may be null.</param>
    /// <param name="frameCount">Number of frames the bake records.</param>
    public TimelineObjectRecorder(GameObject boundObject, Camera excludedBrainCamera, int frameCount)
    {
        this.boundObject = boundObject;
        this.frameCount = frameCount;
        Transform excludedTransform = (excludedBrainCamera != null) ? excludedBrainCamera.transform : null;
        foreach (Transform transform in boundObject.GetComponentsInChildren<Transform>(true))
        {
            this.activities.Add(new RecordedActivity(transform.gameObject, CreateActivityValues(transform.gameObject)));
            if (transform is RectTransform || transform == excludedTransform) continue;
            AddTransformChannels(transform);
        }
        foreach (SkinnedMeshRenderer skinnedMesh in boundObject.GetComponentsInChildren<SkinnedMeshRenderer>(true)) AddBlendShapeChannel(skinnedMesh);
        foreach (Camera camera in boundObject.GetComponentsInChildren<Camera>(true))
        {
            if (camera != excludedBrainCamera) AddFieldOfViewCurve(camera);
        }
        foreach (Graphic graphic in boundObject.GetComponentsInChildren<Graphic>(true)) AddGraphicColorCurve(graphic);
        foreach (CanvasGroup canvasGroup in boundObject.GetComponentsInChildren<CanvasGroup>(true)) AddCanvasGroupAlphaCurve(canvasGroup);
    }

    /// <summary>Captures the authored values (before the first Evaluate) that constant-channel dropping compares against.</summary>
    public void CaptureAuthored()
    {
        foreach (RecordedChannel channel in this.channels) channel.Values.CaptureAuthored();
        foreach (RecordedCurve curve in this.curves) curve.Values.CaptureAuthored();
        foreach (RecordedActivity activity in this.activities) activity.Values.CaptureAuthored();
    }

    /// <summary>Records frame frameIndex.</summary>
    /// <param name="frameIndex">The frame being recorded, 0..frameCount - 1.</param>
    public void RecordFrame(int frameIndex)
    {
        foreach (RecordedChannel channel in this.channels) channel.Values.Record(frameIndex);
        foreach (RecordedCurve curve in this.curves) curve.Values.Record(frameIndex);
        foreach (RecordedActivity activity in this.activities) activity.Values.Record(frameIndex);
    }

    /// <summary>Builds the outputs, dropping a channel, curve or activation only when it is constant AND equal to its authored value.</summary>
    /// <param name="clipKey">The group key of this object's glTF animation.</param>
    /// <param name="jumpFrames">The object's jump frames (DR-9.4), ascending.</param>
    /// <param name="frameRate">Baked frames per second.</param>
    /// <param name="objectsWithActivationEntries">Objects whose activation an Activation track or Control clip already owns (D11).</param>
    /// <param name="result">Receives the animation, curves and activations.</param>
    public void Build(string clipKey, int[] jumpFrames, float frameRate, ISet<GameObject> objectsWithActivationEntries, TimelineSampleResult result)
    {
        BuildAnimation(clipKey, jumpFrames, frameRate, result);
        foreach (RecordedCurve curve in this.curves)
        {
            if (curve.Values.IsUnchanged(this.frameCount)) continue;
            result.Curves.Add(BuildCurveEntry(curve, jumpFrames));
        }
        foreach (RecordedActivity activity in this.activities)
        {
            if (objectsWithActivationEntries.Contains(activity.Target) || activity.Values.IsUnchanged(this.frameCount)) continue;
            result.Activations.Add(BuildActivationEntry(activity, frameRate));
        }
    }

    /// <summary>
    /// Adds the glTF animation and its ITimelineAnimationData entry when at least one channel moves. The bake is trimmed to
    /// the union of its kept channels' changing ranges; players hold the first and last keys outside it, so nothing is lost.
    /// </summary>
    private void BuildAnimation(string clipKey, int[] jumpFrames, float frameRate, TimelineSampleResult result)
    {
        List<RecordedChannel> keptChannels = this.channels.FindAll(channel => !channel.Values.IsUnchanged(this.frameCount));
        if (keptChannels.Count == 0) return;
        int startFrame = this.frameCount - 1;
        int lastFrame = 0;
        foreach (RecordedChannel channel in keptChannels)
        {
            int channelStartFrame;
            int channelLastFrame;
            if (!channel.Values.TryGetChangingRange(this.frameCount, out channelStartFrame, out channelLastFrame)) continue;
            startFrame = Math.Min(startFrame, channelStartFrame);
            lastFrame = Math.Max(lastFrame, channelLastFrame);
        }
        // Kept channels that never change (constant at a non-authored value) need one frame only.
        if (lastFrame < startFrame) startFrame = lastFrame;

        var animationBake = new TimelineAnimationBake
        {
            Clip = clipKey,
            TargetId = GlobalObjectId.GetGlobalObjectIdSlow(this.boundObject).ToString(),
            FrameRate = frameRate,
            StartFrame = startFrame,
            FrameCount = lastFrame - startFrame + 1,
            TimelineFrameCount = this.frameCount,
            JumpFrames = jumpFrames
        };
        foreach (RecordedChannel channel in keptChannels)
        {
            float[] trimmedValues = channel.Values.CopyFrames(startFrame, animationBake.FrameCount);
            animationBake.Channels.Add(new TimelineChannelBake { NodeId = channel.NodeId, Path = channel.Path, Width = channel.Values.Width, Values = trimmedValues });
        }
        result.AnimationBakes.Add(animationBake);
        result.Animations.Add(new Dictionary<string, object>
        {
            ["target"] = TimelineExportTool.BindingReference(this.boundObject),
            ["clip"] = clipKey
        });
    }

    /// <summary>The ITimelinePropertyData entry for a changing curve, trimmed to its changing range (D6).</summary>
    private Dictionary<string, object> BuildCurveEntry(RecordedCurve curve, int[] jumpFrames)
    {
        SampledValues values = curve.Values;
        int startFrame;
        int lastChangingFrame;
        if (!values.TryGetChangingRange(this.frameCount, out startFrame, out lastChangingFrame))
        {
            startFrame = 0;
            lastChangingFrame = 0;
        }
        float[] keptValues = values.CopyFrames(startFrame, lastChangingFrame - startFrame + 1);
        var keptJumpFrames = new List<int>();
        foreach (int jumpFrame in jumpFrames)
        {
            if (jumpFrame > startFrame && jumpFrame <= lastChangingFrame) keptJumpFrames.Add(jumpFrame);
        }
        return new Dictionary<string, object>
        {
            ["target"] = TimelineExportTool.BindingReference(curve.Target),
            ["kind"] = (int)curve.Kind,
            ["element"] = curve.Element,
            ["startFrame"] = startFrame,
            ["stride"] = values.Width,
            ["values"] = keptValues,
            ["jumpFrames"] = keptJumpFrames
        };
    }

    /// <summary>The ITimelineActivationData entry: each run of active frames becomes [runStart, runEnd) in seconds, Revert on release.</summary>
    private Dictionary<string, object> BuildActivationEntry(RecordedActivity activity, float frameRate)
    {
        var intervals = new List<float>();
        int runStartFrame = -1;
        for (int frame = 0; frame < this.frameCount; frame++)
        {
            bool isActive = activity.Values.Frames[frame] == ACTIVE_VALUE;
            if (isActive && runStartFrame < 0) runStartFrame = frame;
            if (isActive || runStartFrame < 0) continue;
            intervals.Add(runStartFrame / frameRate);
            intervals.Add(frame / frameRate);
            runStartFrame = -1;
        }
        // A run that lasts to the last frame ends at frameCount / fps, so the object stays active through the end.
        if (runStartFrame >= 0)
        {
            intervals.Add(runStartFrame / frameRate);
            intervals.Add(this.frameCount / frameRate);
        }
        return new Dictionary<string, object>
        {
            ["target"] = TimelineExportTool.BindingReference(activity.Target),
            ["intervals"] = intervals,
            ["postPlayback"] = (int)ActivationTrack.PostPlaybackState.Revert
        };
    }

    /// <summary>Adds the translation, rotation and scale channels of one transform.</summary>
    private void AddTransformChannels(Transform transform)
    {
        string nodeId = GlobalObjectId.GetGlobalObjectIdSlow(transform.gameObject).ToString();
        this.channels.Add(new RecordedChannel(nodeId, "translation", new SampledValues(VECTOR3_WIDTH, this.frameCount, (buffer, offset) =>
        {
            Vector3 position = transform.localPosition;
            buffer[offset] = position.x;
            buffer[offset + 1] = position.y;
            buffer[offset + 2] = position.z;
        })));
        this.channels.Add(new RecordedChannel(nodeId, "rotation", new SampledValues(ROTATION_WIDTH, this.frameCount, (buffer, offset) =>
        {
            Quaternion rotation = transform.localRotation;
            buffer[offset] = rotation.x;
            buffer[offset + 1] = rotation.y;
            buffer[offset + 2] = rotation.z;
            buffer[offset + 3] = rotation.w;
        })));
        this.channels.Add(new RecordedChannel(nodeId, "scale", new SampledValues(VECTOR3_WIDTH, this.frameCount, (buffer, offset) =>
        {
            Vector3 scale = transform.localScale;
            buffer[offset] = scale.x;
            buffer[offset + 1] = scale.y;
            buffer[offset + 2] = scale.z;
        })));
    }

    /// <summary>Adds the weights channel of a skinned mesh with blend shapes (Unity's 0..100 scale).</summary>
    private void AddBlendShapeChannel(SkinnedMeshRenderer skinnedMesh)
    {
        Mesh sharedMesh = skinnedMesh.sharedMesh;
        if (sharedMesh == null || sharedMesh.blendShapeCount <= 0) return;
        int blendShapeCount = sharedMesh.blendShapeCount;
        string nodeId = GlobalObjectId.GetGlobalObjectIdSlow(skinnedMesh.gameObject).ToString();
        this.channels.Add(new RecordedChannel(nodeId, "weights", new SampledValues(blendShapeCount, this.frameCount, (buffer, offset) =>
        {
            for (int i = 0; i < blendShapeCount; i++) buffer[offset + i] = skinnedMesh.GetBlendShapeWeight(i);
        })));
    }

    /// <summary>Adds the field-of-view curve of a camera that no brain drives.</summary>
    private void AddFieldOfViewCurve(Camera camera)
    {
        var values = new SampledValues(SCALAR_WIDTH, this.frameCount, (buffer, offset) => buffer[offset] = camera.fieldOfView);
        this.curves.Add(new RecordedCurve(camera.gameObject, TimelinePropertyKind.FieldOfView, string.Empty, values));
    }

    /// <summary>Adds the colour curve of a UI Graphic.</summary>
    private void AddGraphicColorCurve(Graphic graphic)
    {
        var values = new SampledValues(COLOR_WIDTH, this.frameCount, (buffer, offset) =>
        {
            Color color = graphic.color;
            buffer[offset] = color.r;
            buffer[offset + 1] = color.g;
            buffer[offset + 2] = color.b;
            buffer[offset + 3] = color.a;
        });
        this.curves.Add(new RecordedCurve(graphic.gameObject, TimelinePropertyKind.GraphicColor, GetCanvasElementPath(graphic.transform), values));
    }

    /// <summary>Adds the alpha curve of a CanvasGroup.</summary>
    private void AddCanvasGroupAlphaCurve(CanvasGroup canvasGroup)
    {
        var values = new SampledValues(SCALAR_WIDTH, this.frameCount, (buffer, offset) => buffer[offset] = canvasGroup.alpha);
        this.curves.Add(new RecordedCurve(canvasGroup.gameObject, TimelinePropertyKind.CanvasGroupAlpha, GetCanvasElementPath(canvasGroup.transform), values));
    }

    /// <summary>The activity record of one GameObject: 1 while activeSelf, else 0.</summary>
    private SampledValues CreateActivityValues(GameObject target)
    {
        return new SampledValues(SCALAR_WIDTH, this.frameCount, (buffer, offset) => buffer[offset] = target.activeSelf ? ACTIVE_VALUE : 0.0f);
    }

    /// <summary>The UI path TOOLKIT.UserInterface.FindElement matches: names from the root Canvas GameObject (inclusive) down to the element.</summary>
    private static string GetCanvasElementPath(Transform element)
    {
        Transform rootCanvas = null;
        for (Transform ancestor = element; ancestor != null; ancestor = ancestor.parent)
        {
            if (ancestor.GetComponent<Canvas>() != null) rootCanvas = ancestor;
        }
        if (rootCanvas == null) return TimelineExportTool.GetHierarchyPath(element);
        string path = element.name;
        for (Transform ancestor = element; ancestor != rootCanvas; ancestor = ancestor.parent) path = ancestor.parent.name + "/" + path;
        return path;
    }

    /// <summary>The property kinds a baked curve drives; values match the runtime's TimelinePropertyKind.</summary>
    private enum TimelinePropertyKind { FieldOfView = 0, GraphicColor = 1, CanvasGroupAlpha = 2 }

    /// <summary>One transform or blend shape channel being recorded.</summary>
    private sealed class RecordedChannel
    {
        /// <summary>GlobalObjectId of the node the channel moves.</summary>
        public readonly string NodeId;
        /// <summary>The glTF channel path name.</summary>
        public readonly string Path;
        /// <summary>The recorded values.</summary>
        public readonly SampledValues Values;

        /// <summary>Pairs a node and path with its recorded values.</summary>
        public RecordedChannel(string nodeId, string path, SampledValues values)
        {
            this.NodeId = nodeId;
            this.Path = path;
            this.Values = values;
        }
    }

    /// <summary>One property curve being recorded.</summary>
    private sealed class RecordedCurve
    {
        /// <summary>The node that owns the property.</summary>
        public readonly GameObject Target;
        /// <summary>The property the curve drives.</summary>
        public readonly TimelinePropertyKind Kind;
        /// <summary>The UI path for UI properties; "" otherwise.</summary>
        public readonly string Element;
        /// <summary>The recorded values.</summary>
        public readonly SampledValues Values;

        /// <summary>Pairs a property with its recorded values.</summary>
        public RecordedCurve(GameObject target, TimelinePropertyKind kind, string element, SampledValues values)
        {
            this.Target = target;
            this.Kind = kind;
            this.Element = element;
            this.Values = values;
        }
    }

    /// <summary>One GameObject's activeSelf being recorded.</summary>
    private sealed class RecordedActivity
    {
        /// <summary>The object switched on and off.</summary>
        public readonly GameObject Target;
        /// <summary>The recorded 1 / 0 values.</summary>
        public readonly SampledValues Values;

        /// <summary>Pairs an object with its recorded activity.</summary>
        public RecordedActivity(GameObject target, SampledValues values)
        {
            this.Target = target;
            this.Values = values;
        }
    }

    /// <summary>A fixed number of values per frame, read by a delegate, plus the authored values read before the first Evaluate.</summary>
    private sealed class SampledValues
    {
        /// <summary>Marks a frame search that found nothing.</summary>
        private const int NOT_FOUND = -1;

        /// <summary>Values per frame.</summary>
        public readonly int Width;
        /// <summary>Width values per frame, for every frame.</summary>
        public readonly float[] Frames;
        /// <summary>The Width values captured before sampling.</summary>
        private readonly float[] authored;
        /// <summary>Writes the current Width values into a buffer at an offset.</summary>
        private readonly Action<float[], int> readCurrent;

        /// <summary>Allocates storage for frameCount frames.</summary>
        public SampledValues(int width, int frameCount, Action<float[], int> readCurrent)
        {
            this.Width = width;
            this.Frames = new float[width * frameCount];
            this.authored = new float[width];
            this.readCurrent = readCurrent;
        }

        /// <summary>Stores the current values as the authored reference.</summary>
        public void CaptureAuthored()
        {
            this.readCurrent(this.authored, 0);
        }

        /// <summary>Stores the current values as frame frameIndex.</summary>
        public void Record(int frameIndex)
        {
            this.readCurrent(this.Frames, frameIndex * this.Width);
        }

        /// <summary>True when every frame equals the authored values, so the value can be dropped.</summary>
        public bool IsUnchanged(int frameCount)
        {
            return TimelineKeyframeEncoder.IsConstantAt(this.Frames, this.Width, frameCount, this.authored);
        }

        /// <summary>
        /// The frames that hold the motion (D6): from the frame before the first one that differs from frame 0, to the first
        /// frame from which every frame equals the final frame. Differences are measured against those two anchors, not
        /// the adjacent frame, so a slow drift below the tolerance per frame is still kept over its whole range. Before
        /// the range every frame equals frame 0 and after it every frame equals the final frame, within the tolerance.
        /// The range always holds at least one frame (startFrame &lt;= lastChangingFrame): the tolerance test is not
        /// transitive, so a slow drift can make the last frame that differs from the final one fall before startFrame.
        /// </summary>
        /// <returns>False, with both frames 0, when every frame equals frame 0.</returns>
        public bool TryGetChangingRange(int frameCount, out int startFrame, out int lastChangingFrame)
        {
            startFrame = 0;
            lastChangingFrame = 0;
            int firstDifferentFromStart = NOT_FOUND;
            for (int frame = 1; frame < frameCount; frame++)
            {
                if (FrameEquals(frame, 0)) continue;
                firstDifferentFromStart = frame;
                break;
            }
            if (firstDifferentFromStart == NOT_FOUND) return false;
            int finalFrame = frameCount - 1;
            int lastDifferentFromFinal = NOT_FOUND;
            for (int frame = finalFrame - 1; frame >= 0; frame--)
            {
                if (FrameEquals(frame, finalFrame)) continue;
                lastDifferentFromFinal = frame;
                break;
            }
            startFrame = firstDifferentFromStart - 1;
            int firstFinalFrame = (lastDifferentFromFinal == NOT_FOUND) ? 0 : lastDifferentFromFinal + 1;
            lastChangingFrame = Math.Max(firstFinalFrame, startFrame);
            return true;
        }

        /// <summary>Width values per frame for count frames from startFrame on.</summary>
        public float[] CopyFrames(int startFrame, int count)
        {
            var copied = new float[count * this.Width];
            Array.Copy(this.Frames, startFrame * this.Width, copied, 0, copied.Length);
            return copied;
        }

        /// <summary>True when two frames hold equal values within the encoder's tolerance.</summary>
        private bool FrameEquals(int frameIndex, int otherFrameIndex)
        {
            for (int i = 0; i < this.Width; i++)
            {
                float difference = Math.Abs(this.Frames[frameIndex * this.Width + i] - this.Frames[otherFrameIndex * this.Width + i]);
                if (difference > TimelineKeyframeEncoder.CONSTANT_TOLERANCE) return false;
            }
            return true;
        }
    }
}

/// <summary>The structural jump frames of one bound object (DR-9.4): zero-mix clip boundaries and constant-tangent keys.</summary>
internal static class TimelineJumpFrames
{
    /// <summary>Subtracted before rounding up, so a boundary a float rounding error past a frame still lands on that frame.</summary>
    private const float FRAME_ROUNDING_TOLERANCE = 0.0001f;

    /// <summary>Jump frames from the object's Animation tracks and their override children, kept in 1..frameCount - 1.</summary>
    /// <param name="tracks">The Animation tracks bound to the object.</param>
    /// <param name="frameRate">Baked frames per second.</param>
    /// <param name="frameCount">Number of baked frames.</param>
    /// <returns>Ascending, de-duplicated frames.</returns>
    public static int[] Collect(List<AnimationTrack> tracks, float frameRate, int frameCount)
    {
        var boundaryTimes = new List<double>();
        foreach (AnimationTrack track in tracks) CollectTrackBoundaries(track, boundaryTimes);
        var frames = new List<int>();
        foreach (double boundaryTime in boundaryTimes)
        {
            frames.Add(Mathf.CeilToInt((float)(boundaryTime * frameRate) - FRAME_ROUNDING_TOLERANCE));
        }
        return TimelineKeyframeEncoder.NormalizeJumpFrames(frames.ToArray(), frameCount);
    }

    /// <summary>Adds one track's boundaries, then its override children's.</summary>
    private static void CollectTrackBoundaries(AnimationTrack track, List<double> boundaryTimes)
    {
        foreach (TimelineClip clip in track.GetClips())
        {
            if (clip.mixInDuration == 0) boundaryTimes.Add(clip.start);
            if (clip.mixOutDuration == 0) boundaryTimes.Add(clip.end);
            if (clip.animationClip != null) CollectConstantKeyTimes(clip.animationClip, clip, boundaryTimes);
        }
        if (track.infiniteClip != null) CollectConstantKeyTimes(track.infiniteClip, null, boundaryTimes);
        foreach (TrackAsset child in track.GetChildTracks())
        {
            AnimationTrack overrideTrack = child as AnimationTrack;
            if (overrideTrack != null && !overrideTrack.mutedInHierarchy) CollectTrackBoundaries(overrideTrack, boundaryTimes);
        }
    }

    /// <summary>
    /// Adds the timeline time of key k + 1 for every key k whose right tangent is Constant. Inside a clip the key time is
    /// mapped through clipIn and timeScale and kept only within [start, end]; an infinite clip (clip null) uses key times as-is.
    /// </summary>
    private static void CollectConstantKeyTimes(AnimationClip animationClip, TimelineClip clip, List<double> boundaryTimes)
    {
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(animationClip))
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(animationClip, binding);
            if (curve == null) continue;
            Keyframe[] keys = curve.keys;
            for (int k = 0; k < keys.Length - 1; k++)
            {
                if (AnimationUtility.GetKeyRightTangentMode(curve, k) != AnimationUtility.TangentMode.Constant) continue;
                double nextKeyTime = keys[k + 1].time;
                if (clip == null)
                {
                    boundaryTimes.Add(nextKeyTime);
                    continue;
                }
                double timelineTime = clip.start + (nextKeyTime - clip.clipIn) / clip.timeScale;
                if (timelineTime >= clip.start && timelineTime <= clip.end) boundaryTimes.Add(timelineTime);
            }
        }
    }
}
#endif
