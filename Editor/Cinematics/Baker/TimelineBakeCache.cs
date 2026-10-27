#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CanvasTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Cache keys and paths for baked Timelines (D18). A key hashes everything that changes a bake: the baker build, the Timeline and every
/// asset it depends on, the director's settings and bindings, the bound Animators' controllers and avatars, and the
/// serialized state of every bound subtree. Equal keys mean a cached bake can be exported without sampling again.
/// </summary>
internal static class TimelineBakeCache
{
    /// <summary>The cache folder, relative to the project folder.</summary>
    private const string CACHE_FOLDER = "Library/BabylonToolkit/TimelineBakes";
    /// <summary>Extension of a cache file.</summary>
    private const string CACHE_EXTENSION = ".json";
    /// <summary>Field of Cinemachine's shot asset that names its virtual camera, read by reflection (no Cinemachine reference).</summary>
    private const string SHOT_CAMERA_FIELD = "VirtualCamera";
    /// <summary>Field of ExposedReference holding the name resolved through the director.</summary>
    private const string EXPOSED_NAME_FIELD = "exposedName";
    /// <summary>Written for a missing binding or asset so positions in the hashed text stay aligned.</summary>
    private const string NONE_MARKER = "none";

    /// <summary>SHA-1 hex over everything that changes the bake; a fresh GUID when anything cannot be hashed.</summary>
    /// <param name="director">A director in a saved scene, with a Timeline asset.</param>
    /// <returns>The key the bake is stored and looked up under.</returns>
    public static string ComputeKey(PlayableDirector director)
    {
        try
        {
            var keyText = new StringBuilder();
            keyText.Append("v").Append(TimelineExportTool.BAKE_FORMAT_VERSION).Append('\n');
            // The baker assembly's build id: any change to the baker code re-bakes (the exporter DLL is left out, it changes every build).
            keyText.Append(typeof(PlayableDirectorBaker).Assembly.ManifestModule.ModuleVersionId).Append('\n');
            AppendTimelineDependencies(director, keyText);
            AppendDirectorSettings(director, keyText);
            List<GameObject> signatureRoots = AppendBindings(director, keyText);
            // Timeline re-sorts a track's clips in memory when it builds a graph, and clips with equal starts may swap,
            // so the roots are hashed in GlobalObjectId order rather than in clip order.
            signatureRoots.Sort((first, second) => string.CompareOrdinal(GetGlobalId(first), GetGlobalId(second)));
            foreach (GameObject root in signatureRoots) AppendSubtreeSignature(root, keyText);
            return ToSha1Hex(keyText.ToString());
        }
        catch (Exception)
        {
            // D18: anything that cannot be hashed makes a key no cache file matches, so the director is baked again.
            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>&lt;ProjectPath&gt;/Library/BabylonToolkit/TimelineBakes/&lt;sha1(director GlobalObjectId)&gt;.json.</summary>
    /// <param name="director">The director the cache file belongs to.</param>
    /// <returns>The absolute cache file path.</returns>
    public static string GetCachePath(PlayableDirector director)
    {
        string projectFolder = Directory.GetParent(Application.dataPath).FullName;
        string directorId = GlobalObjectId.GetGlobalObjectIdSlow(director).ToString();
        return Path.Combine(projectFolder, CACHE_FOLDER, ToSha1Hex(directorId) + CACHE_EXTENSION);
    }

    /// <summary>The dependency hash of the Timeline and of every asset it depends on (clips, takes, signals, audio, nested timelines).</summary>
    private static void AppendTimelineDependencies(PlayableDirector director, StringBuilder keyText)
    {
        string timelinePath = AssetDatabase.GetAssetPath(director.playableAsset);
        string[] dependencyPaths = AssetDatabase.GetDependencies(timelinePath, true);
        Array.Sort(dependencyPaths, StringComparer.Ordinal);
        foreach (string dependencyPath in dependencyPaths) AppendAssetHash(dependencyPath, keyText);
    }

    /// <summary>The director settings the bag carries.</summary>
    private static void AppendDirectorSettings(PlayableDirector director, StringBuilder keyText)
    {
        keyText.Append((int)director.extrapolationMode).Append('|')
            .Append((int)director.timeUpdateMode).Append('|')
            .Append(director.initialTime.ToString("R")).Append('|')
            .Append(director.playOnAwake).Append('\n');
    }

    /// <summary>
    /// The binding of every output track and the bound Animators' controller and avatar hashes. Returns the GameObjects
    /// whose subtree signatures are hashed next: the director's own GameObject (its exposed-reference table and the
    /// marker-track Signal Receivers), every bound object, the Cinemachine shots' virtual cameras and the sources of the
    /// active Control clips.
    /// </summary>
    private static List<GameObject> AppendBindings(PlayableDirector director, StringBuilder keyText)
    {
        var signatureRoots = new List<GameObject>();
        AddSignatureRoot(director.gameObject, signatureRoots);
        TimelineAsset timeline = director.playableAsset as TimelineAsset;
        if (timeline == null) return signatureRoots;
        foreach (TrackAsset track in timeline.GetOutputTracks())
        {
            UnityEngine.Object binding = director.GetGenericBinding(track);
            keyText.Append((binding != null) ? GlobalObjectId.GetGlobalObjectIdSlow(binding).ToString() : NONE_MARKER).Append('\n');
            AddSignatureRoot(TimelineBakeSampler.ToGameObject(binding), signatureRoots);
            AppendAnimatorAssets(binding as Animator, keyText);
            foreach (TimelineClip clip in track.GetClips())
            {
                AddSignatureRoot(ResolveShotCamera(director, clip.asset), signatureRoots);
                if (!track.mutedInHierarchy) AddSignatureRoot(ResolveControlSource(director, clip.asset), signatureRoots);
            }
        }
        return signatureRoots;
    }

    /// <summary>The GameObject a Control clip drives, resolved through the director, or null for any other clip.</summary>
    private static GameObject ResolveControlSource(PlayableDirector director, UnityEngine.Object clipAsset)
    {
        ControlPlayableAsset controlAsset = clipAsset as ControlPlayableAsset;
        return (controlAsset != null) ? controlAsset.sourceGameObject.Resolve(director) : null;
    }

    /// <summary>The dependency hashes of a bound Animator's controller and avatar.</summary>
    private static void AppendAnimatorAssets(Animator animator, StringBuilder keyText)
    {
        if (animator == null) return;
        AppendAssetHash(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController), keyText);
        AppendAssetHash(AssetDatabase.GetAssetPath(animator.avatar), keyText);
    }

    /// <summary>The virtual camera a Cinemachine shot clip resolves to through the director, or null for any other clip.</summary>
    private static GameObject ResolveShotCamera(PlayableDirector director, UnityEngine.Object clipAsset)
    {
        if (clipAsset == null) return null;
        FieldInfo cameraField = clipAsset.GetType().GetField(SHOT_CAMERA_FIELD, BindingFlags.Public | BindingFlags.Instance);
        if (cameraField == null) return null;
        object exposedReference = cameraField.GetValue(clipAsset);
        FieldInfo nameField = exposedReference.GetType().GetField(EXPOSED_NAME_FIELD, BindingFlags.Public | BindingFlags.Instance);
        if (nameField == null) return null;
        bool isValid;
        UnityEngine.Object resolved = director.GetReferenceValue((PropertyName)nameField.GetValue(exposedReference), out isValid);
        return isValid ? TimelineBakeSampler.ToGameObject(resolved) : null;
    }

    /// <summary>The GlobalObjectId string of a GameObject.</summary>
    private static string GetGlobalId(GameObject gameObject)
    {
        return GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString();
    }

    /// <summary>Adds a GameObject once.</summary>
    private static void AddSignatureRoot(GameObject root, List<GameObject> signatureRoots)
    {
        if (root != null && !signatureRoots.Contains(root)) signatureRoots.Add(root);
    }

    /// <summary>
    /// Every GameObject in the subtree: hierarchy path, activeSelf and local TRS, then each non-Transform component's type
    /// and serialized JSON. A missing script is a null component and is skipped.
    /// </summary>
    private static void AppendSubtreeSignature(GameObject root, StringBuilder keyText)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            keyText.Append(TimelineExportTool.GetHierarchyPath(transform)).Append('|')
                .Append(transform.gameObject.activeSelf).Append('|')
                .Append(transform.localPosition.ToString("R")).Append(transform.localRotation.ToString("R")).Append(transform.localScale.ToString("R")).Append('\n');
            foreach (Component component in transform.GetComponents<Component>())
            {
                if (component == null || component is Transform) continue;
                keyText.Append(component.GetType().FullName).Append(EditorJsonUtility.ToJson(component)).Append('\n');
            }
        }
    }

    /// <summary>One asset's path and dependency hash.</summary>
    private static void AppendAssetHash(string assetPath, StringBuilder keyText)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            keyText.Append(NONE_MARKER).Append('\n');
            return;
        }
        keyText.Append(assetPath).Append(':').Append(AssetDatabase.GetAssetDependencyHash(assetPath).ToString()).Append('\n');
    }

    /// <summary>Lower-case SHA-1 hex of the UTF-8 text.</summary>
    private static string ToSha1Hex(string text)
    {
        using (SHA1 sha1 = SHA1.Create())
        {
            byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(text));
            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash) hex.Append(value.ToString("x2"));
            return hex.ToString();
        }
    }
}
#endif
