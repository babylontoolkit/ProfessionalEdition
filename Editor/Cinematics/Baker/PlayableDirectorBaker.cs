#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CanvasTools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Debug = UnityEngine.Debug;

/// <summary>
/// Bakes every PlayableDirector's Timeline for export, menu and CLI, through Core's TimelineExportTool hook.
/// Cached bakes whose key still matches are reused; the rest are sampled one director at a time, each followed by a
/// reload of the open scenes from disk, so the evaluation leaves nothing behind. Never shows a dialog.
/// </summary>
[InitializeOnLoad]
public static class PlayableDirectorBaker
{
    private const string MENU_PATH = "Tools/Babylon Toolkit/Cinematics/Bake Timelines";
    /// <summary>Bytes per kilobyte, for the report's cache file size.</summary>
    private const double BYTES_PER_KILOBYTE = 1024.0;
    /// <summary>Indent and label of a skipped line under its director's report line.</summary>
    private const string SKIPPED_PREFIX = "  skipped: ";

    /// <summary>Registers the bake as Core's Timeline hook.</summary>
    static PlayableDirectorBaker()
    {
        TimelineExportTool.BakeHandler = BakeDirectors;
    }

    /// <summary>Menu item: bakes (or loads) every director in the open scenes and logs the report.</summary>
    [MenuItem(MENU_PATH, false, 80)]
    private static void BakeFromMenu()
    {
        Debug.Log(TimelineExportTool.BakeOpenScenes(false));
    }

    /// <summary>The hook: cache lookups first, then fresh bakes (each followed by a scene reload), then cache writes and report lines.</summary>
    /// <param name="request">The directors to bake, and whether to ignore the cache.</param>
    /// <returns>One bake per director that could be baked or loaded, and the report.</returns>
    public static TimelineBakeResult BakeDirectors(TimelineBakeRequest request)
    {
        var jobs = new List<DirectorBakeJob>();
        foreach (PlayableDirector director in request.Directors) jobs.Add(CreateJob(director, request.Force));

        List<DirectorBakeJob> freshJobs = jobs.FindAll(job => job.Bake == null);
        string reason;
        if (freshJobs.Count > 0 && !CanBakeFresh(out reason))
        {
            foreach (DirectorBakeJob job in freshJobs) job.FailureReason = reason;
        }
        else
        {
            foreach (DirectorBakeJob job in freshJobs) BakeFresh(job);
        }

        var result = new TimelineBakeResult();
        foreach (DirectorBakeJob job in jobs)
        {
            if (job.Bake != null) result.Bakes.Add(job.Bake);
            result.ReportLines.Add(FormatReportLine(job));
            List<string> skippedLines = (job.Bake != null) ? job.Bake.SkippedLines : job.SkippedLines;
            foreach (string skippedLine in skippedLines) result.ReportLines.Add(SKIPPED_PREFIX + skippedLine);
        }
        return result;
    }

    /// <summary>
    /// Builds one director's bag in the DR-9.5 order: settings, then the event tracks (whose activation-owned objects the
    /// sampler must not record), then the sampled camera, animations and curves, with the recorded activations appended
    /// after the event activations.
    /// </summary>
    /// <param name="director">The director, in a freshly loaded scene.</param>
    /// <param name="cacheKey">The key the bake is stored under.</param>
    /// <param name="skippedLines">Receives one line per item the bake cannot carry.</param>
    /// <returns>The bake, not yet saved.</returns>
    /// <exception cref="InvalidOperationException">The director's playable asset is not a Timeline.</exception>
    private static TimelineDirectorBake BakeDirector(PlayableDirector director, string cacheKey, List<string> skippedLines)
    {
        TimelineAsset timeline = director.playableAsset as TimelineAsset;
        if (timeline == null) throw new InvalidOperationException("its playable asset is not a Timeline");
        var properties = new Dictionary<string, object>();
        TimelineTrackExporter.BuildSettings(director, timeline, properties);
        ISet<GameObject> activationOwnedObjects = TimelineTrackExporter.BuildEventTracks(director, timeline, properties, skippedLines);
        TimelineSampleResult sample = TimelineBakeSampler.Sample(director, timeline, activationOwnedObjects, skippedLines);
        properties["camera"] = sample.Camera;
        properties[TimelineExportTool.CINEMACHINE_TRACKS_KEY] = TimelineBakeSampler.CollectCinemachineTracks(director, TimelineTrackExporter.GetActiveTracks(timeline));
        properties["animations"] = sample.Animations;
        properties["curves"] = sample.Curves;
        ((List<object>)properties["activations"]).AddRange(sample.Activations);
        return new TimelineDirectorBake
        {
            DirectorId = GlobalObjectId.GetGlobalObjectIdSlow(director).ToString(),
            CacheKey = cacheKey,
            PropertiesJson = TimelineExportTool.ToJson(properties),
            Animations = sample.AnimationBakes
        };
    }

    /// <summary>Reopens the loaded scenes from disk, discarding in-memory changes, and keeps the multi-scene setup.</summary>
    private static void ReloadScenesFromDisk()
    {
        var sceneSetup = EditorSceneManager.GetSceneManagerSetup();
        if (sceneSetup.Length == 1)
        {
            EditorSceneManager.OpenScene(sceneSetup[0].path, OpenSceneMode.Single);
            return;
        }
        EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
    }

    /// <summary>D19: fresh bakes need edit mode and every open scene saved and clean, because each bake reloads them.</summary>
    /// <param name="reason">Why a fresh bake cannot run; null when it can.</param>
    /// <returns>True when fresh bakes may run.</returns>
    private static bool CanBakeFresh(out string reason)
    {
        reason = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            reason = "not baked — exit play mode to bake";
            return false;
        }
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var scene = EditorSceneManager.GetSceneAt(i);
            if (string.IsNullOrEmpty(scene.path) || scene.isDirty)
            {
                reason = "not baked — save " + (string.IsNullOrEmpty(scene.path) ? scene.name : scene.path) + " first (the bake reloads it from disk)";
                return false;
            }
        }
        return true;
    }

    /// <summary>Records what the report needs before any reload, and takes the cached bake when its key still matches.</summary>
    private static DirectorBakeJob CreateJob(PlayableDirector director, bool force)
    {
        var job = new DirectorBakeJob
        {
            DirectorId = GlobalObjectId.GetGlobalObjectIdSlow(director).ToString(),
            Label = director.gameObject.scene.name + "/" + TimelineExportTool.GetHierarchyPath(director.transform),
            TimelineName = director.playableAsset.name,
            CacheKey = TimelineBakeCache.ComputeKey(director),
            CachePath = TimelineBakeCache.GetCachePath(director)
        };
        if (force) return job;
        TimelineDirectorBake cached = TimelineExportTool.LoadBake(job.CachePath);
        if (cached != null && cached.CacheKey == job.CacheKey)
        {
            cached.FromCache = true;
            job.Bake = cached;
        }
        return job;
    }

    /// <summary>Samples one director in the current scenes, saves the cache file, and reloads the scenes from disk.</summary>
    private static void BakeFresh(DirectorBakeJob job)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            PlayableDirector director = ResolveDirector(job.DirectorId);
            if (director == null)
            {
                job.FailureReason = "not baked — the director no longer resolves after the scene reload";
                return;
            }
            job.Bake = BakeDirector(director, job.CacheKey, job.SkippedLines);
            job.Bake.SkippedLines.AddRange(job.SkippedLines);
            TimelineExportTool.SaveBake(job.CachePath, job.Bake);
        }
        catch (OperationCanceledException)
        {
            job.Bake = null;
            job.FailureReason = "not baked — cancelled";
        }
        catch (Exception exception)
        {
            job.Bake = null;
            job.FailureReason = "not baked — " + exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            ReloadScenesFromDisk();
            job.BakeSeconds = stopwatch.Elapsed.TotalSeconds;
        }
    }

    /// <summary>The PlayableDirector a GlobalObjectId string names in the loaded scenes, or null.</summary>
    private static PlayableDirector ResolveDirector(string directorId)
    {
        GlobalObjectId parsedId;
        if (!GlobalObjectId.TryParse(directorId, out parsedId)) return null;
        return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsedId) as PlayableDirector;
    }

    /// <summary>
    /// The director's report line:
    /// &lt;scene&gt;/&lt;path&gt; "&lt;timeline&gt;": &lt;cache hit | baked N frames @ F fps in S s&gt;; camera &lt;K shots | none&gt;; animation xA; curves xC; activation xV; audio xU; signals xG; control xR; &lt;size&gt; KB
    /// </summary>
    private static string FormatReportLine(DirectorBakeJob job)
    {
        string header = job.Label + " \"" + job.TimelineName + "\": ";
        if (job.Bake == null) return header + job.FailureReason;
        JObject bag = JObject.Parse(job.Bake.PropertiesJson);
        string outcome = job.Bake.FromCache ? "cache hit" : FormatBakeOutcome(bag, job.BakeSeconds);
        JArray shotNames = (bag["camera"] as JObject)?["shotNames"] as JArray;
        string camera = (shotNames != null) ? shotNames.Count + " shots" : "none";
        long cacheBytes = File.Exists(job.CachePath) ? new FileInfo(job.CachePath).Length : 0;
        return header + outcome
            + "; camera " + camera
            + "; animation x" + CountEntries(bag, "animations")
            + "; curves x" + CountEntries(bag, "curves")
            + "; activation x" + CountEntries(bag, "activations")
            + "; audio x" + CountEntries(bag, "audio")
            + "; signals x" + CountEntries(bag, "signals")
            + "; control x" + CountEntries(bag, "controls")
            + "; " + (cacheBytes / BYTES_PER_KILOBYTE).ToString("0.0") + " KB";
    }

    /// <summary>"baked N frames @ F fps in S s", with N computed exactly as the sampler counts frames.</summary>
    private static string FormatBakeOutcome(JObject bag, double bakeSeconds)
    {
        float frameRate = bag.Value<float>("frameRate");
        float duration = bag.Value<float>("duration");
        int frameCount = Mathf.FloorToInt(duration * frameRate) + 1;
        return "baked " + frameCount + " frames @ " + frameRate.ToString("0.##") + " fps in " + bakeSeconds.ToString("0.00") + " s";
    }

    /// <summary>The number of entries in one of the bag's lists.</summary>
    private static int CountEntries(JObject bag, string key)
    {
        JArray entries = bag[key] as JArray;
        return (entries != null) ? entries.Count : 0;
    }

    /// <summary>One director's progress through BakeDirectors, captured before any scene reload.</summary>
    private sealed class DirectorBakeJob
    {
        /// <summary>GlobalObjectId of the director component; survives the reloads.</summary>
        public string DirectorId;
        /// <summary>"&lt;scene&gt;/&lt;hierarchy path&gt;" for the report.</summary>
        public string Label;
        /// <summary>The Timeline asset's name.</summary>
        public string TimelineName;
        /// <summary>The current cache key.</summary>
        public string CacheKey;
        /// <summary>The cache file path.</summary>
        public string CachePath;
        /// <summary>The cached or fresh bake; null when the director could not be baked.</summary>
        public TimelineDirectorBake Bake;
        /// <summary>Why there is no bake, for the report line.</summary>
        public string FailureReason;
        /// <summary>Seconds the fresh bake took.</summary>
        public double BakeSeconds;
        /// <summary>Items the bake could not carry.</summary>
        public readonly List<string> SkippedLines = new List<string>();
    }
}
#endif
