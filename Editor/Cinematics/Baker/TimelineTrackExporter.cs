#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using CanvasTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;

/// <summary>Turns a director's settings and its discrete tracks into DR-4 property-bag entries and skipped-report lines.</summary>
internal static class TimelineTrackExporter
{
    /// <summary>Type name of Cinemachine's Timeline track (sampled by TimelineBakeSampler, not here).</summary>
    private const string CINEMACHINE_TRACK_TYPE = "CinemachineTrack";
    /// <summary>Recorder tracks only render offline and are ignored without a report line.</summary>
    private const string RECORDER_TYPE_FRAGMENT = "Recorder";
    /// <summary>The runtime's TimelineControlKind values.</summary>
    private const int CONTROL_KIND_DIRECTOR = 0;
    private const int CONTROL_KIND_PARTICLES = 1;
    private const int CONTROL_KIND_ACTIVATION = 2;
    /// <summary>The runtime's TimelineArgumentType values.</summary>
    private const int ARGUMENT_NONE = 0;
    private const int ARGUMENT_BOOL = 1;
    private const int ARGUMENT_INT = 2;
    private const int ARGUMENT_FLOAT = 3;
    private const int ARGUMENT_STRING = 4;

    /// <summary>Writes version, timeline, frameRate, duration, playOnAwake, wrapMode, initialTime and updateMode.</summary>
    /// <param name="director">The director being baked.</param>
    /// <param name="timeline">Its Timeline asset.</param>
    /// <param name="properties">The bag being built.</param>
    public static void BuildSettings(PlayableDirector director, TimelineAsset timeline, Dictionary<string, object> properties)
    {
        properties["version"] = TimelineExportTool.BAKE_FORMAT_VERSION;
        properties["timeline"] = timeline.name;
        properties["frameRate"] = (float)timeline.editorSettings.frameRate;
        properties["duration"] = (float)director.duration;
        properties["playOnAwake"] = director.playOnAwake;
        properties["wrapMode"] = (int)director.extrapolationMode;
        properties["initialTime"] = (float)director.initialTime;
        properties["updateMode"] = (int)director.timeUpdateMode;
    }

    /// <summary>Writes activations (merged), audio, signals and controls; adds skipped lines; returns the GameObjects that got Activation or Control-activation entries.</summary>
    /// <param name="director">The director being baked.</param>
    /// <param name="timeline">Its Timeline asset.</param>
    /// <param name="properties">The bag being built; receives "activations", "audio", "signals" and "controls".</param>
    /// <param name="skippedLines">Receives one line per item the bake cannot carry.</param>
    /// <returns>The objects whose activation these entries own (D11), so the sampler records no activation for them.</returns>
    public static ISet<GameObject> BuildEventTracks(PlayableDirector director, TimelineAsset timeline, Dictionary<string, object> properties, List<string> skippedLines)
    {
        var tracks = new EventTrackEntries();
        foreach (TrackAsset track in GetActiveTracks(timeline))
        {
            GameObject markerTarget = (track == timeline.markerTrack) ? director.gameObject : TimelineBakeSampler.ToGameObject(director.GetGenericBinding(track));
            AddSignals(track, markerTarget, tracks, skippedLines);
            AddTrackEntries(director, track, tracks, skippedLines);
        }
        properties["activations"] = tracks.Activations;
        properties["audio"] = tracks.Audio;
        properties["signals"] = tracks.Signals.OrderBy(signal => (float)signal["time"]).Cast<object>().ToList();
        properties["controls"] = tracks.Controls;
        return tracks.ActivationOwnedObjects;
    }

    /// <summary>timeline.GetOutputTracks() without tracks where mutedInHierarchy is true.</summary>
    /// <param name="timeline">The Timeline asset.</param>
    /// <returns>The tracks that play, in Timeline order.</returns>
    public static List<TrackAsset> GetActiveTracks(TimelineAsset timeline)
    {
        var activeTracks = new List<TrackAsset>();
        foreach (TrackAsset track in timeline.GetOutputTracks())
        {
            if (!track.mutedInHierarchy) activeTracks.Add(track);
        }
        return activeTracks;
    }

    /// <summary>Dispatches one track by type; unsupported types add a skipped line (Recorder tracks silently).</summary>
    private static void AddTrackEntries(PlayableDirector director, TrackAsset track, EventTrackEntries tracks, List<string> skippedLines)
    {
        if (track is ActivationTrack) AddActivationEntry(director, (ActivationTrack)track, tracks, skippedLines);
        else if (track is AudioTrack) AddAudioEntries(director, (AudioTrack)track, tracks, skippedLines);
        else if (track is ControlTrack) AddControlEntries(director, (ControlTrack)track, tracks, skippedLines);
        else if (track is AnimationTrack) ReportUncarriedCurves(director, (AnimationTrack)track, tracks, skippedLines);
        else if (IsCarriedElsewhere(track)) return;
        else if (!track.GetType().Name.Contains(RECORDER_TYPE_FRAGMENT)) skippedLines.Add(track.name + " — " + track.GetType().Name + " tracks are not carried");
    }

    /// <summary>Signal, marker, group and Cinemachine tracks: their content is exported by the signal pass or the sampler.</summary>
    private static bool IsCarriedElsewhere(TrackAsset track)
    {
        return track is SignalTrack || track is MarkerTrack || track is GroupTrack || track.GetType().Name == CINEMACHINE_TRACK_TYPE;
    }

    /// <summary>One ITimelineActivationData entry: every clip is an active [start, end) interval.</summary>
    private static void AddActivationEntry(PlayableDirector director, ActivationTrack track, EventTrackEntries tracks, List<string> skippedLines)
    {
        GameObject target = TimelineBakeSampler.ToGameObject(director.GetGenericBinding(track));
        if (target == null)
        {
            skippedLines.Add(track.name + " — Activation track has no bound object");
            return;
        }
        var intervals = new List<float>();
        foreach (TimelineClip clip in track.GetClips())
        {
            intervals.Add((float)clip.start);
            intervals.Add((float)clip.end);
        }
        tracks.Activations.Add(new Dictionary<string, object>
        {
            ["target"] = TimelineExportTool.BindingReference(target),
            ["intervals"] = intervals,
            ["postPlayback"] = (int)track.postPlaybackState
        });
        tracks.ActivationOwnedObjects.Add(target);
    }

    /// <summary>One ITimelineAudioData entry per AudioPlayableAsset clip, with clipAsset in place of file until export.</summary>
    private static void AddAudioEntries(PlayableDirector director, AudioTrack track, EventTrackEntries tracks, List<string> skippedLines)
    {
        AudioSource boundSource = director.GetGenericBinding(track) as AudioSource;
        float trackVolume = ReadVolume(track, "m_TrackProperties.volume", track.name + " — track volume is not readable; exported at full volume", skippedLines);
        foreach (TimelineClip clip in track.GetClips())
        {
            AudioPlayableAsset asset = clip.asset as AudioPlayableAsset;
            if (asset == null || asset.clip == null) continue;
            float clipVolume = ReadVolume(asset, "m_ClipProperties.volume", "audio clip " + clip.displayName + " — clip volume is not readable; exported at full volume", skippedLines);
            tracks.Audio.Add(new Dictionary<string, object>
            {
                ["target"] = (boundSource != null) ? TimelineExportTool.BindingReference(boundSource.gameObject) : null,
                ["clipAsset"] = AssetDatabase.GetAssetPath(asset.clip),
                ["start"] = (float)clip.start,
                ["duration"] = (float)clip.duration,
                ["clipIn"] = (float)clip.clipIn,
                ["clipLength"] = asset.clip.length,
                ["loop"] = asset.loop,
                ["volume"] = clipVolume * trackVolume,
                ["easeIn"] = (float)clip.mixInDuration,
                ["easeOut"] = (float)clip.mixOutDuration
            });
        }
    }

    /// <summary>A serialized volume factor, or 1 with a skipped line when the property is missing.</summary>
    private static float ReadVolume(Object owner, string propertyPath, string missingLine, List<string> skippedLines)
    {
        SerializedProperty volume = new SerializedObject(owner).FindProperty(propertyPath);
        if (volume != null) return volume.floatValue;
        skippedLines.Add(missingLine);
        return 1.0f;
    }

    /// <summary>The ITimelineControlData entries of every Control clip; prefab-spawning and ITimeControl clips are skipped whole.</summary>
    private static void AddControlEntries(PlayableDirector director, ControlTrack track, EventTrackEntries tracks, List<string> skippedLines)
    {
        foreach (TimelineClip clip in track.GetClips())
        {
            ControlPlayableAsset asset = clip.asset as ControlPlayableAsset;
            if (asset == null) continue;
            GameObject source = asset.sourceGameObject.Resolve(director);
            string uncarriedReason = GetUncarriedControlReason(asset, source);
            if (uncarriedReason != null)
            {
                skippedLines.Add("control clip " + clip.displayName + " — " + uncarriedReason);
                continue;
            }
            bool controlsItself = (source == director.gameObject);
            if (asset.updateDirector) AddControlledDirectors(director, asset, source, clip, tracks);
            if (asset.updateParticle) AddControlledParticles(asset, source, clip, tracks);
            if (asset.active && !controlsItself)
            {
                tracks.Controls.Add(CreateControlEntry(source, CONTROL_KIND_ACTIVATION, clip, (int)asset.postPlayback));
                tracks.ActivationOwnedObjects.Add(source);
            }
        }
    }

    /// <summary>Why a Control clip cannot be carried, or null when it can.</summary>
    private static string GetUncarriedControlReason(ControlPlayableAsset asset, GameObject source)
    {
        if (asset.prefabGameObject != null) return "prefab spawning not carried";
        if (source == null) return "its source object does not resolve";
        if (asset.updateITimeControl && HasTimeControl(source)) return "custom ITimeControl not carried";
        return null;
    }

    /// <summary>True when the source or a descendant has an ITimeControl script, as Timeline searches for them.</summary>
    private static bool HasTimeControl(GameObject source)
    {
        foreach (MonoBehaviour script in source.GetComponentsInChildren<MonoBehaviour>())
        {
            if (script is ITimeControl) return true;
        }
        return false;
    }

    /// <summary>One Director entry per PlayableDirector on the source (children too with searchHierarchy), skipping the playing Timeline itself.</summary>
    private static void AddControlledDirectors(PlayableDirector director, ControlPlayableAsset asset, GameObject source, TimelineClip clip, EventTrackEntries tracks)
    {
        PlayableDirector[] controlledDirectors = asset.searchHierarchy ? source.GetComponentsInChildren<PlayableDirector>(true) : source.GetComponents<PlayableDirector>();
        foreach (PlayableDirector controlled in controlledDirectors)
        {
            // Timeline never drives a director that plays the controlling Timeline (it would recurse).
            if (controlled.playableAsset == director.playableAsset) continue;
            tracks.Controls.Add(CreateControlEntry(controlled.gameObject, CONTROL_KIND_DIRECTOR, clip, (int)asset.postPlayback));
        }
    }

    /// <summary>
    /// One Particles entry per top-most ParticleSystem: the source's own, plus, with searchHierarchy, every descendant system
    /// that has no particle-system ancestor below the source. The runtime plays each entry with its children, so a nested
    /// system is never simulated twice.
    /// </summary>
    private static void AddControlledParticles(ControlPlayableAsset asset, GameObject source, TimelineClip clip, EventTrackEntries tracks)
    {
        if (source.GetComponent<ParticleSystem>() != null)
        {
            tracks.Controls.Add(CreateControlEntry(source, CONTROL_KIND_PARTICLES, clip, (int)asset.postPlayback));
            return;
        }
        if (!asset.searchHierarchy) return;
        foreach (ParticleSystem particles in source.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (HasParticleAncestorBelow(particles.transform, source.transform)) continue;
            tracks.Controls.Add(CreateControlEntry(particles.gameObject, CONTROL_KIND_PARTICLES, clip, (int)asset.postPlayback));
        }
    }

    /// <summary>True when a ParticleSystem sits between the transform and the source (exclusive of both).</summary>
    private static bool HasParticleAncestorBelow(Transform transform, Transform source)
    {
        for (Transform ancestor = transform.parent; ancestor != null && ancestor != source; ancestor = ancestor.parent)
        {
            if (ancestor.GetComponent<ParticleSystem>() != null) return true;
        }
        return false;
    }

    /// <summary>One ITimelineControlData entry.</summary>
    private static Dictionary<string, object> CreateControlEntry(GameObject target, int kind, TimelineClip clip, int postPlayback)
    {
        return new Dictionary<string, object>
        {
            ["target"] = TimelineExportTool.BindingReference(target),
            ["kind"] = kind,
            ["start"] = (float)clip.start,
            ["duration"] = (float)clip.duration,
            ["clipIn"] = (float)clip.clipIn,
            ["timeScale"] = (float)clip.timeScale,
            ["postPlayback"] = postPlayback
        };
    }

    /// <summary>One ITimelineSignalData entry per SignalEmitter with an asset on the track.</summary>
    private static void AddSignals(TrackAsset track, GameObject target, EventTrackEntries tracks, List<string> skippedLines)
    {
        foreach (IMarker marker in track.GetMarkers())
        {
            SignalEmitter emitter = marker as SignalEmitter;
            if (emitter == null || emitter.asset == null) continue;
            tracks.Signals.Add(new Dictionary<string, object>
            {
                ["time"] = (float)emitter.time,
                ["name"] = emitter.asset.name,
                ["retroactive"] = emitter.retroactive,
                ["emitOnce"] = emitter.emitOnce,
                ["target"] = TimelineExportTool.BindingReference(target),
                ["reactions"] = BuildReactions(target, emitter.asset, skippedLines)
            });
        }
    }

    /// <summary>The ITimelineSignalReactionData entries of every SignalReceiver on the target that reacts to the signal.</summary>
    private static List<object> BuildReactions(GameObject target, SignalAsset signal, List<string> skippedLines)
    {
        var reactions = new List<object>();
        if (target == null) return reactions;
        foreach (SignalReceiver receiver in target.GetComponents<SignalReceiver>())
        {
            UnityEvent reaction = receiver.GetReaction(signal);
            if (reaction == null) continue;
            SerializedProperty persistentCalls = FindPersistentCalls(receiver, signal);
            for (int i = 0; i < reaction.GetPersistentEventCount(); i++)
            {
                Dictionary<string, object> reactionEntry = BuildReaction(receiver, reaction, i, persistentCalls, skippedLines);
                if (reactionEntry != null) reactions.Add(reactionEntry);
            }
        }
        return reactions;
    }

    /// <summary>One persistent call as a reaction, or null with a skipped line when it targets something the web cannot call.</summary>
    private static Dictionary<string, object> BuildReaction(SignalReceiver receiver, UnityEvent reaction, int callIndex, SerializedProperty persistentCalls, List<string> skippedLines)
    {
        string method = reaction.GetPersistentMethodName(callIndex);
        string receiverPath = TimelineExportTool.GetHierarchyPath(receiver.transform);
        EditorScriptComponent scriptTarget = reaction.GetPersistentTarget(callIndex) as EditorScriptComponent;
        if (scriptTarget == null)
        {
            skippedLines.Add("signal reaction " + method + " on " + receiverPath + " — target is not a toolkit script component");
            return null;
        }
        SerializedProperty call = (persistentCalls != null && callIndex < persistentCalls.arraySize) ? persistentCalls.GetArrayElementAtIndex(callIndex) : null;
        if (call == null)
        {
            skippedLines.Add("signal reaction " + method + " on " + receiverPath + " — its call data is not readable");
            return null;
        }
        PersistentListenerMode mode = (PersistentListenerMode)call.FindPropertyRelative("m_Mode").intValue;
        SerializedProperty arguments = call.FindPropertyRelative("m_Arguments");
        int argumentType;
        object argument;
        if (!TryReadArgument(mode, arguments, out argumentType, out argument))
        {
            skippedLines.Add("signal reaction " + method + " on " + receiverPath + " — " + mode + " arguments are not carried");
            return null;
        }
        return new Dictionary<string, object>
        {
            ["target"] = TimelineExportTool.BindingReference(scriptTarget.gameObject),
            ["klass"] = System.UnityTools.GetScriptComponentClass(scriptTarget),
            ["method"] = method,
            ["argumentType"] = argumentType,
            ["argument"] = argument
        };
    }

    /// <summary>m_Events.m_Events[signal index].m_PersistentCalls.m_Calls of the receiver, or null when the signal is not registered.</summary>
    private static SerializedProperty FindPersistentCalls(SignalReceiver receiver, SignalAsset signal)
    {
        var serializedReceiver = new SerializedObject(receiver);
        SerializedProperty signals = serializedReceiver.FindProperty("m_Events.m_Signals");
        if (signals == null) return null;
        for (int signalIndex = 0; signalIndex < signals.arraySize; signalIndex++)
        {
            if (signals.GetArrayElementAtIndex(signalIndex).objectReferenceValue != signal) continue;
            return serializedReceiver.FindProperty("m_Events.m_Events.Array.data[" + signalIndex + "].m_PersistentCalls.m_Calls");
        }
        return null;
    }

    /// <summary>Maps a persistent call's mode to the runtime argument type and reads the matching argument; false for Object and EventDefined.</summary>
    private static bool TryReadArgument(PersistentListenerMode mode, SerializedProperty arguments, out int argumentType, out object argument)
    {
        argumentType = ARGUMENT_NONE;
        argument = null;
        switch (mode)
        {
            case PersistentListenerMode.Void:
                return true;
            case PersistentListenerMode.Int:
                argumentType = ARGUMENT_INT;
                argument = arguments.FindPropertyRelative("m_IntArgument").intValue;
                return true;
            case PersistentListenerMode.Float:
                argumentType = ARGUMENT_FLOAT;
                argument = arguments.FindPropertyRelative("m_FloatArgument").floatValue;
                return true;
            case PersistentListenerMode.String:
                argumentType = ARGUMENT_STRING;
                argument = arguments.FindPropertyRelative("m_StringArgument").stringValue;
                return true;
            case PersistentListenerMode.Bool:
                argumentType = ARGUMENT_BOOL;
                argument = arguments.FindPropertyRelative("m_BoolArgument").boolValue;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Reports the animated properties of an Animation track (and its override tracks) that the bake does not carry:
    /// one line per distinct type and property, and one per animated RectTransform path.
    /// </summary>
    private static void ReportUncarriedCurves(PlayableDirector director, AnimationTrack track, EventTrackEntries tracks, List<string> skippedLines)
    {
        GameObject boundObject = TimelineBakeSampler.ToGameObject(director.GetGenericBinding(track));
        string boundName = (boundObject != null) ? boundObject.name : track.name;
        foreach (AnimationClip animationClip in CollectAnimationClips(track))
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(animationClip))
            {
                string bindingPath = string.IsNullOrEmpty(binding.path) ? boundName : boundName + "/" + binding.path;
                if (typeof(RectTransform).IsAssignableFrom(binding.type))
                {
                    if (tracks.ReportedCurves.Add("RectTransform|" + bindingPath)) skippedLines.Add(bindingPath + " — UI RectTransform animation not carried");
                    continue;
                }
                if (IsCarriedCurve(binding)) continue;
                if (tracks.ReportedCurves.Add(binding.type.FullName + "|" + binding.propertyName)) skippedLines.Add(bindingPath + "." + binding.propertyName + " — animated property not carried");
            }
        }
    }

    /// <summary>Every clip and infinite clip of the track and its override tracks.</summary>
    private static List<AnimationClip> CollectAnimationClips(AnimationTrack track)
    {
        var animationClips = new List<AnimationClip>();
        if (track.infiniteClip != null) animationClips.Add(track.infiniteClip);
        foreach (TimelineClip clip in track.GetClips())
        {
            if (clip.animationClip != null) animationClips.Add(clip.animationClip);
        }
        foreach (TrackAsset child in track.GetChildTracks())
        {
            AnimationTrack overrideTrack = child as AnimationTrack;
            if (overrideTrack != null && !overrideTrack.mutedInHierarchy) animationClips.AddRange(CollectAnimationClips(overrideTrack));
        }
        return animationClips;
    }

    /// <summary>True for the curve bindings the sampler records (FR-A5).</summary>
    private static bool IsCarriedCurve(EditorCurveBinding binding)
    {
        System.Type type = binding.type;
        string property = binding.propertyName;
        if (type == typeof(Transform) || type == typeof(Animator)) return true;
        if (type == typeof(SkinnedMeshRenderer)) return property.StartsWith("blendShape.");
        if (type == typeof(GameObject)) return property == "m_IsActive";
        if (type == typeof(Camera)) return property == "field of view";
        if (typeof(Graphic).IsAssignableFrom(type)) return property.StartsWith("m_Color.");
        if (type == typeof(CanvasGroup)) return property == "m_Alpha";
        return false;
    }

    /// <summary>The entries one director's event tracks produce, collected in track order.</summary>
    private sealed class EventTrackEntries
    {
        /// <summary>ITimelineActivationData entries from Activation tracks.</summary>
        public readonly List<object> Activations = new List<object>();
        /// <summary>ITimelineAudioData entries.</summary>
        public readonly List<object> Audio = new List<object>();
        /// <summary>ITimelineSignalData entries, unsorted.</summary>
        public readonly List<Dictionary<string, object>> Signals = new List<Dictionary<string, object>>();
        /// <summary>ITimelineControlData entries.</summary>
        public readonly List<object> Controls = new List<object>();
        /// <summary>Objects with Activation-track or Control-activation entries.</summary>
        public readonly HashSet<GameObject> ActivationOwnedObjects = new HashSet<GameObject>();
        /// <summary>Uncarried-curve keys already reported, so each appears once.</summary>
        public readonly HashSet<string> ReportedCurves = new HashSet<string>();
    }
}
#endif
