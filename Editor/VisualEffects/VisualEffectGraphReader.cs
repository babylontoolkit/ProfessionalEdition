#if UNITY_EDITOR && BT_VFX_GRAPH
using System;
using System.Collections.Generic;
using System.Reflection;
using CanvasTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Reads a VFX Graph asset's internal model into the neutral description Core exports (vfx-graph-port plan D5, D6, DR-6.4).
/// Registered as Core's <see cref="VisualEffectExportTool.GraphReader"/> when this optional assembly compiles, which it only
/// does for VFX Graph 17.x. Strictly read-only: it never calls GetOrCreateGraph, SanitizeGraph or any model setter.
/// </summary>
[InitializeOnLoad]
public static class VisualEffectGraphReader
{
    /// <summary>Registers the reader as Core's VFX Graph hook.</summary>
    static VisualEffectGraphReader()
    {
        VisualEffectExportTool.GraphReader = Read;
    }

    /// <summary>
    /// Reads one graph (DR-6.4): systems, spawners, blocks, outputs, exposed properties and events, with every slot value as a
    /// DR-4 expression and every sampled asset as a resource request. A missing reflection member or an unloaded graph gives
    /// an Error instead of a partial description.
    /// </summary>
    /// <param name="request">The asset and its path.</param>
    /// <returns>The description and resources, or an Error.</returns>
    public static VfxReadResult Read(VfxReadRequest request)
    {
        VisualEffectReflection.ResetMissingMembers();
        VfxReadResult result = new VisualEffectGraphWalk(request).Run();
        if (result.Error == null && VisualEffectReflection.MissingMembers.Count > 0)
        {
            return new VfxReadResult { Error = "missing " + string.Join(", ", VisualEffectReflection.MissingMembers) };
        }
        return result;
    }
}

/// <summary>One read of one graph: groups contexts into particle systems and describes each (DR-6.4 steps 1-10).</summary>
internal sealed class VisualEffectGraphWalk
{
    /// <summary>Every block's activation slot, never exported as a slot (DR-6.4 step 4).</summary>
    private const string ActivationSlotName = "_vfx_enabled";
    /// <summary>The data class of particle systems.</summary>
    private const string ParticleDataType = "VFXDataParticle";

    /// <summary>Settings read per block / operator type (DR-19.4).</summary>
    internal static readonly Dictionary<string, string[]> SettingsByType = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        { "SetAttribute", new[] { "attribute", "Composition", "Source", "Random", "channels" } },
        { "PositionShape", new[] { "shape", "heightMode", "applyOrientation", "killOutliers", "compositionPosition", "compositionAxes", "compositionDirection", "positionMode", "spawnMode" } },
        { "Turbulence", new[] { "Mode", "NoiseType" } },
        { "Force", new[] { "Mode" } },
        { "Drag", new[] { "UseParticleSize" } },
        { "VectorFieldForce", new[] { "DataEncoding", "Mode", "ClosedField", "ConserveMagnitude" } },
        { "CollisionShape", new[] { "shape", "behavior", "mode", "radiusMode", "roughSurface" } },
        { "Orient", new[] { "mode", "axes", "faceRay" } },
        { "FlipbookPlay", new[] { "mode", "frameRateMode", "cyclesMode", "animationRange", "useCustomRange", "reverse" } },
        { "VFXSpawnerBurst", new[] { "repeat", "spawnMode", "delayMode" } },
        { "AttributeFromMap", new[] { "attribute", "Composition", "SampleMode", "mode", "channels", "usePointCount" } },
        { "AttributeFromCurve", new[] { "attribute", "Composition", "AlphaComposition", "SampleMode", "Mode", "ColorMode", "channels" } },
        { "ColorOverLife", new[] { "mode", "ColorComposition", "AlphaComposition" } },
        { "CameraFade", new[] { "cullWhenFaded", "fadeMode", "affectShadows" } },
        { "ScreenSpaceSize", new[] { "sizeMode", "sizeZMode" } },
        { "VelocityRandomize", new[] { "composition", "speedMode" } },
        { "Random", new[] { "seed", "constant", "independentSeed" } },
        { "Remap", new[] { "m_Clamp" } },
        { "Condition", new[] { "condition" } },
        { "Swizzle", new[] { "mask" } },
    };

    /// <summary>VFXBasicSpawner settings (DR-19.4).</summary>
    private static readonly string[] SpawnerSettings = { "loopDuration", "loopCount", "delayBeforeLoop", "delayAfterLoop" };

    /// <summary>Output settings (DR-3 `IVisualEffectOutput.settings`).</summary>
    private static readonly string[] OutputSettings =
    {
        "blendMode", "cullMode", "zWriteMode", "zTestMode", "useAlphaClipping", "uvMode", "flipbookBlendFrames", "useSoftParticle",
        "castShadows", "primitiveType", "sort", "colorMapping", "useBaseColorMap", "materialType", "doubleSided", "useNormalMap",
        "useMaskMap", "colorMode",
    };

    /// <summary>Output slots carried (DR-3 `IVisualEffectOutput.slots`).</summary>
    private static readonly HashSet<string> OutputSlotNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "mainTexture", "baseColorMap", "normalMap", "maskMap", "flipBookSize", "softParticleFadeDistance", "alphaThreshold",
        "smoothness", "thickness", "normalScale",
    };

    /// <summary>Block types whose `attribute` setting names the attribute they write.</summary>
    private static readonly HashSet<string> AttributeWritingBlocks = new HashSet<string>(StringComparer.Ordinal)
    {
        "SetAttribute", "AttributeFromCurve", "AttributeFromMap",
    };

    /// <summary>The request being read.</summary>
    private readonly VfxReadRequest request;
    /// <summary>The slot-to-expression converter, shared by every system of the graph.</summary>
    private readonly VisualEffectSlotConverter converter = new VisualEffectSlotConverter();

    /// <summary>Creates a walk for one request.</summary>
    /// <param name="request">The asset and its path.</param>
    public VisualEffectGraphWalk(VfxReadRequest request)
    {
        this.request = request;
    }

    /// <summary>Runs the whole read.</summary>
    /// <returns>The read result.</returns>
    public VfxReadResult Run()
    {
        string assetName = request.Asset != null ? request.Asset.name : request.AssetPath;
        object resource = VisualEffectReflection.ResourceAt(request.AssetPath);
        object graph = resource == null ? null : VisualEffectReflection.GraphOf(resource);
        if (graph == null) return new VfxReadResult { Error = "the graph of " + assetName + " is not loaded in the editor" };

        VfxReadResult result = new VfxReadResult
        {
            UpdateMode = VisualEffectReflection.ResourceUpdateMode(resource),
            CullingFlags = VisualEffectReflection.ResourceCullingFlags(resource),
            PrewarmStepCount = VisualEffectReflection.ResourcePrewarmStepCount(resource),
            PrewarmDeltaTime = VisualEffectReflection.ResourcePrewarmDeltaTime(resource),
            InitialEventName = VisualEffectReflection.ResourceInitialEventName(resource),
            Graph = new VfxGraphDescription(),
        };
        List<object> children = VisualEffectReflection.Children(graph);
        DescribeSystems(children, result.Graph);
        DescribeExposed(children, result.Graph);
        DescribeEvents(result.Graph);
        result.Graph.Deviations.AddRange(converter.GraphDeviations);
        result.Resources = converter.Resources;
        return result;
    }

    // ----- systems (DR-6.4 step 2) -----

    /// <summary>Groups the graph's contexts by particle data and describes one system per group, in graph order.</summary>
    /// <param name="children">The graph's children.</param>
    /// <param name="graph">The description being filled.</param>
    private void DescribeSystems(List<object> children, VfxGraphDescription graph)
    {
        List<object> dataOrder = new List<object>();
        Dictionary<object, List<object>> contextsByData = new Dictionary<object, List<object>>();
        foreach (object child in children)
        {
            if (!IsA(child, "VFXContext")) continue;
            object data = VisualEffectReflection.Data(child);
            if (data == null || data.GetType().Name != ParticleDataType) continue;
            if (!contextsByData.TryGetValue(data, out List<object> contexts))
            {
                contexts = new List<object>();
                contextsByData[data] = contexts;
                dataOrder.Add(data);
            }
            contexts.Add(child);
        }
        for (int i = 0; i < dataOrder.Count; i++) graph.Systems.Add(DescribeSystem(dataOrder[i], contextsByData[dataOrder[i]], i));
    }

    /// <summary>Describes one particle system from its data and contexts.</summary>
    /// <param name="data">The VFXDataParticle.</param>
    /// <param name="contexts">Its contexts in graph order.</param>
    /// <param name="index">The system index, for the fallback name.</param>
    /// <returns>The system description.</returns>
    private VfxSystemDescription DescribeSystem(object data, List<object> contexts, int index)
    {
        object initialize = FirstContextOfType(contexts, "Init");
        object update = FirstContextOfType(contexts, "Update");
        string label = initialize == null ? null : VisualEffectReflection.Label(initialize);
        VfxSystemDescription system = new VfxSystemDescription
        {
            Name = string.IsNullOrEmpty(label) ? "System " + index : label,
            Capacity = ToInt(VisualEffectReflection.Setting(data, "capacity")),
            Space = VisualEffectReflection.DataSpace(data),
        };
        converter.BeginSystem(system);
        if (initialize != null)
        {
            system.Spawners = DescribeSpawners(initialize);
            system.Initialize = DescribeBlocks(initialize, system);
        }
        if (update != null) system.Update = DescribeUpdate(update, system);
        foreach (object context in contexts)
        {
            if (VisualEffectReflection.ContextType(context) == "Output") system.Outputs.Add(DescribeOutput(context, system));
        }
        system.WritesLifetime = WritesAttribute(system.Initialize, "lifetime") || (system.Update != null && WritesAttribute(system.Update.Blocks, "lifetime"));
        return system;
    }

    /// <summary>The Spawn contexts linked into the Initialize context (DR-6.4 step 3).</summary>
    /// <param name="initialize">The Initialize context.</param>
    /// <returns>The spawner descriptions.</returns>
    private List<VfxSpawnerDescription> DescribeSpawners(object initialize)
    {
        List<VfxSpawnerDescription> spawners = new List<VfxSpawnerDescription>();
        HashSet<object> seen = new HashSet<object>();
        foreach (List<object> flowSlot in VisualEffectReflection.InputFlowSlots(initialize))
        {
            foreach (object linked in flowSlot)
            {
                if (VisualEffectReflection.ContextType(linked) != "Spawner" || !seen.Add(linked)) continue;
                spawners.Add(DescribeSpawner(linked, spawners.Count));
            }
        }
        return spawners;
    }

    /// <summary>Describes one spawner: its loop settings and slots, start / stop events and blocks (D17).</summary>
    /// <param name="spawner">The Spawn context.</param>
    /// <param name="index">The spawner index, for the fallback name.</param>
    /// <returns>The spawner description.</returns>
    private VfxSpawnerDescription DescribeSpawner(object spawner, int index)
    {
        string label = VisualEffectReflection.Label(spawner);
        VfxSpawnerDescription description = new VfxSpawnerDescription { Name = string.IsNullOrEmpty(label) ? "Spawner " + index : label };
        Dictionary<string, object> settings = ReadSettings(spawner, SpawnerSettings);
        description.LoopDuration = SettingText(settings, "loopDuration");
        description.LoopCount = SettingText(settings, "loopCount");
        description.DelayBeforeLoop = SettingText(settings, "delayBeforeLoop");
        description.DelayAfterLoop = SettingText(settings, "delayAfterLoop");
        description.Slots = ConvertInputSlots(spawner, null);

        List<List<object>> flowSlots = VisualEffectReflection.InputFlowSlots(spawner);
        description.StartEvents = EventNames(flowSlots, 0, VisualEffectExportTool.DefaultInitialEvent);
        description.StopEvents = EventNames(flowSlots, 1, "OnStop");
        foreach (object block in VisualEffectReflection.ActiveBlocks(spawner))
        {
            VfxBlockDescription blockDescription = DescribeBlock(block, null);
            if (blockDescription != null) description.Blocks.Add(blockDescription);
        }
        return description;
    }

    /// <summary>The names of the Event contexts linked to one flow slot, or the default when none is linked.</summary>
    /// <param name="flowSlots">The spawner's input flow slots.</param>
    /// <param name="slotIndex">0 = Start, 1 = Stop.</param>
    /// <param name="fallback">The default event name.</param>
    /// <returns>The event names.</returns>
    private static List<string> EventNames(List<List<object>> flowSlots, int slotIndex, string fallback)
    {
        List<string> names = new List<string>();
        if (slotIndex < flowSlots.Count)
        {
            foreach (object linked in flowSlots[slotIndex])
            {
                if (VisualEffectReflection.ContextType(linked) != "Event") continue;
                if (VisualEffectReflection.Setting(linked, "eventName") is string eventName && !names.Contains(eventName)) names.Add(eventName);
            }
        }
        if (names.Count == 0) names.Add(fallback);
        return names;
    }

    /// <summary>VFXBasicUpdate's settings and blocks (DR-6.4 step 5).</summary>
    /// <param name="update">The Update context.</param>
    /// <param name="system">The system (receives custom attributes and deviations).</param>
    /// <returns>The update description.</returns>
    private VfxUpdateDescription DescribeUpdate(object update, VfxSystemDescription system)
    {
        return new VfxUpdateDescription
        {
            Integration = SettingText(VisualEffectReflection.Setting(update, "integration")),
            AngularIntegration = SettingText(VisualEffectReflection.Setting(update, "angularIntegration")),
            AgeParticles = SettingFlag(VisualEffectReflection.Setting(update, "ageParticles"), true),
            ReapParticles = SettingFlag(VisualEffectReflection.Setting(update, "reapParticles"), true),
            SkipZeroDeltaUpdate = SettingFlag(VisualEffectReflection.Setting(update, "skipZeroDeltaUpdate"), false),
            Blocks = DescribeBlocks(update, system),
        };
    }

    /// <summary>One Output context: its class, DR-3 settings and slots, and blocks (DR-6.4 step 6).</summary>
    /// <param name="output">The Output context.</param>
    /// <param name="system">The system (receives custom attributes and deviations).</param>
    /// <returns>The output description.</returns>
    private VfxOutputDescription DescribeOutput(object output, VfxSystemDescription system)
    {
        VfxOutputDescription description = new VfxOutputDescription
        {
            Type = output.GetType().Name,
            Settings = ReadSettings(output, OutputSettings),
            Slots = ConvertInputSlots(output, OutputSlotNames),
        };
        description.Blocks = DescribeBlocks(output, system);
        return description;
    }

    // ----- blocks (DR-6.4 step 4) -----

    /// <summary>Describes a context's enabled blocks in order.</summary>
    /// <param name="context">The context.</param>
    /// <param name="system">The system (receives custom attributes and deviations).</param>
    /// <returns>The block descriptions.</returns>
    private List<VfxBlockDescription> DescribeBlocks(object context, VfxSystemDescription system)
    {
        List<VfxBlockDescription> blocks = new List<VfxBlockDescription>();
        foreach (object block in VisualEffectReflection.ActiveBlocks(context))
        {
            VfxBlockDescription description = DescribeBlock(block, system);
            if (description != null) blocks.Add(description);
        }
        return blocks;
    }

    /// <summary>One block: class name, DR-19.4 settings and every input slot but the activation slot. Subgraph blocks are neutral.</summary>
    /// <param name="block">The block.</param>
    /// <param name="system">The system, or null for spawner blocks.</param>
    /// <returns>The block description, or null for a subgraph block.</returns>
    private VfxBlockDescription DescribeBlock(object block, VfxSystemDescription system)
    {
        string type = block.GetType().Name;
        if (type == "VFXSubgraphBlock")
        {
            converter.AddSystemDeviation(VisualEffectClassifier.NeutralStep, "block:" + type, "subgraph blocks not carried");
            return null;
        }
        VfxBlockDescription description = new VfxBlockDescription { Type = type };
        if (SettingsByType.TryGetValue(type, out string[] settingNames)) description.Settings = ReadSettings(block, settingNames);
        description.Slots = ConvertInputSlots(block, null);
        if (system != null) RecordWrittenCustomAttribute(description, system);
        return description;
    }

    /// <summary>Records the attribute an attribute-writing block names when it is not a VFX built-in (DR-6.4 step 7).</summary>
    /// <param name="block">The block description.</param>
    /// <param name="system">The system.</param>
    private void RecordWrittenCustomAttribute(VfxBlockDescription block, VfxSystemDescription system)
    {
        if (!AttributeWritingBlocks.Contains(block.Type)) return;
        if (!(block.Settings.TryGetValue("attribute", out object attribute) && attribute is string attributeName)) return;
        converter.RecordCustomAttribute(attributeName, WrittenAttributeType(block));
    }

    /// <summary>
    /// The type of the attribute a block writes: a SetAttribute's first typed value slot, else "float" (curve and map blocks
    /// do not carry the attribute type in a slot).
    /// </summary>
    /// <param name="block">The attribute-writing block.</param>
    /// <returns>The DR-3 custom attribute type.</returns>
    private static string WrittenAttributeType(VfxBlockDescription block)
    {
        const string fallbackType = "float";
        if (block.Type != "SetAttribute") return fallbackType;
        foreach (VfxExpression slot in block.Slots.Values)
        {
            if (slot != null && slot.Type != null) return slot.Type;
        }
        return fallbackType;
    }

    /// <summary>Converts a container's input slots (except the activation slot), optionally limited to a set of names.</summary>
    /// <param name="container">The context or block.</param>
    /// <param name="allowedNames">The names to keep, or null for all.</param>
    /// <returns>The slot expressions by slot name.</returns>
    private Dictionary<string, VfxExpression> ConvertInputSlots(object container, HashSet<string> allowedNames)
    {
        Dictionary<string, VfxExpression> slots = new Dictionary<string, VfxExpression>(StringComparer.Ordinal);
        foreach (object slot in VisualEffectReflection.InputSlots(container))
        {
            string name = VisualEffectReflection.SlotName(slot);
            if (name == null || name == ActivationSlotName) continue;
            if (allowedNames != null && !allowedNames.Contains(name)) continue;
            slots[name] = converter.ConvertSlot(slot);
        }
        return slots;
    }

    // ----- exposed properties and events (DR-6.4 steps 9-10) -----

    /// <summary>The exposed properties in order, each default read from the VFXParameter with that exposed name.</summary>
    /// <param name="children">The graph's children.</param>
    /// <param name="graph">The description being filled.</param>
    private void DescribeExposed(List<object> children, VfxGraphDescription graph)
    {
        Dictionary<string, object> parametersByName = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (object child in children)
        {
            if (!IsA(child, "VFXParameter") || !VisualEffectReflection.IsExposed(child)) continue;
            string exposedName = VisualEffectReflection.ExposedName(child);
            if (exposedName != null && !parametersByName.ContainsKey(exposedName)) parametersByName[exposedName] = child;
        }
        foreach (VFXExposedProperty exposed in VisualEffectReflection.ExposedProperties(request.Asset as VisualEffectAsset))
        {
            parametersByName.TryGetValue(exposed.name, out object parameter);
            object defaultValue = parameter == null ? null : converter.EncodeValue(VisualEffectReflection.ParameterValue(parameter), exposed.type, null);
            graph.Exposed.Add(new VfxExposedProperty { Name = exposed.name, Type = VisualEffectSlotConverter.TypeName(exposed.type), DefaultValue = defaultValue });
        }
    }

    /// <summary>Every event the graph listens to, always including OnPlay and OnStop.</summary>
    /// <param name="graph">The description being filled.</param>
    private void DescribeEvents(VfxGraphDescription graph)
    {
        foreach (string eventName in VisualEffectReflection.Events(request.Asset as VisualEffectAsset))
        {
            if (!graph.Events.Contains(eventName)) graph.Events.Add(eventName);
        }
        foreach (string required in new[] { VisualEffectExportTool.DefaultInitialEvent, "OnStop" })
        {
            if (!graph.Events.Contains(required)) graph.Events.Add(required);
        }
    }

    // ----- helpers -----

    /// <summary>Reads the named settings that exist on a model, as DR-3 values (enums as names).</summary>
    /// <param name="model">The model.</param>
    /// <param name="names">The setting names.</param>
    /// <returns>The settings by name; missing settings are omitted.</returns>
    private static Dictionary<string, object> ReadSettings(object model, string[] names)
    {
        Dictionary<string, object> settings = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            object value = VisualEffectSlotConverter.SettingValue(VisualEffectReflection.Setting(model, name));
            if (value != null) settings[name] = value;
        }
        return settings;
    }

    /// <summary>True when any block in the list writes the attribute.</summary>
    /// <param name="blocks">The blocks.</param>
    /// <param name="attribute">The attribute name.</param>
    /// <returns>True when written.</returns>
    private static bool WritesAttribute(List<VfxBlockDescription> blocks, string attribute)
    {
        foreach (VfxBlockDescription block in blocks)
        {
            if (AttributeWritingBlocks.Contains(block.Type) && block.Settings.TryGetValue("attribute", out object written) && (written as string) == attribute) return true;
        }
        return false;
    }

    /// <summary>The first context of a contextType.</summary>
    /// <param name="contexts">The contexts.</param>
    /// <param name="contextType">The contextType name.</param>
    /// <returns>The context, or null.</returns>
    private static object FirstContextOfType(List<object> contexts, string contextType)
    {
        foreach (object context in contexts)
        {
            if (VisualEffectReflection.ContextType(context) == contextType) return context;
        }
        return null;
    }

    /// <summary>True when the object's type or a base type has the given class name.</summary>
    /// <param name="model">The object.</param>
    /// <param name="typeName">The class name.</param>
    /// <returns>True when it derives from that class.</returns>
    internal static bool IsA(object model, string typeName)
    {
        for (Type type = model == null ? null : model.GetType(); type != null; type = type.BaseType)
        {
            if (type.Name == typeName) return true;
        }
        return false;
    }

    /// <summary>A setting value as text, or null.</summary>
    /// <param name="value">The raw setting.</param>
    /// <returns>The text.</returns>
    private static string SettingText(object value)
    {
        return value == null ? null : value.ToString();
    }

    /// <summary>A setting from a settings dictionary as text, or null.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="name">The setting name.</param>
    /// <returns>The text.</returns>
    private static string SettingText(Dictionary<string, object> settings, string name)
    {
        return settings.TryGetValue(name, out object value) ? SettingText(value) : null;
    }

    /// <summary>A boolean setting, or the fallback when absent.</summary>
    /// <param name="value">The raw setting.</param>
    /// <param name="fallback">The value when absent.</param>
    /// <returns>The flag.</returns>
    private static bool SettingFlag(object value, bool fallback)
    {
        return value is bool flag ? flag : fallback;
    }

    /// <summary>A numeric setting as an integer (0 when absent).</summary>
    /// <param name="value">The raw setting.</param>
    /// <returns>The integer.</returns>
    private static int ToInt(object value)
    {
        return value == null ? 0 : Convert.ToInt32(value);
    }
}

/// <summary>
/// Converts VFX slots into DR-4 expressions (DR-6.4.1): links become operator / parameter / attribute / builtin trees, unlinked
/// slots become constants, and sampled assets become resource requests. One converter per graph read.
/// </summary>
internal sealed class VisualEffectSlotConverter
{
    /// <summary>The prefix of a point cache operator's attribute-map outputs.</summary>
    private const string AttributeMapPrefix = "AttributeMap : ";
    /// <summary>A point cache operator's point-count output.</summary>
    private const string PointCountSlot = "Point Count";
    /// <summary>The bits a flags enum can use as a positive 64-bit value (bit 63 is the sign).</summary>
    private const int MaxFlagBits = 63;

    /// <summary>Unity operator class → DR-4.2 op.</summary>
    private static readonly Dictionary<string, string> OperatorOps = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "Add", "add" }, { "Subtract", "subtract" }, { "Multiply", "multiply" }, { "Divide", "divide" }, { "Minimum", "min" },
        { "Maximum", "max" }, { "Modulo", "modulo" }, { "Power", "power" }, { "Negate", "negate" }, { "Absolute", "absolute" },
        { "Sine", "sine" }, { "Cosine", "cosine" }, { "Tangent", "tangent" }, { "SquareRoot", "sqrt" }, { "Floor", "floor" },
        { "Ceiling", "ceil" }, { "Fractional", "fract" }, { "Sign", "sign" }, { "OneMinus", "oneMinus" }, { "Reciprocal", "reciprocal" },
        { "Saturate", "saturate" }, { "Clamp", "clamp" }, { "Lerp", "lerp" }, { "Step", "step" }, { "Smoothstep", "smoothstep" },
        { "Remap", "remap" }, { "Random", "random" }, { "SampleCurve", "sampleCurve" }, { "SampleGradient", "sampleGradient" },
        { "AgeOverLifetime", "ageOverLifetime" }, { "AppendVector", "appendVector" }, { "Swizzle", "swizzle" }, { "Length", "length" },
        { "Normalize", "normalize" }, { "Distance", "distance" }, { "DotProduct", "dot" }, { "CrossProduct", "cross" },
        { "TransformPosition", "transformPosition" }, { "TransformDirection", "transformDirection" }, { "TransformVector", "transformVector" },
        { "Condition", "condition" }, { "Branch", "branch" }, { "LogicalAnd", "and" }, { "LogicalOr", "or" }, { "LogicalNot", "not" },
    };

    /// <summary>The builtin flags carried (DR-4.2 `builtin`); any other flag is neutral.</summary>
    private static readonly HashSet<string> CarriedBuiltins = new HashSet<string>(StringComparer.Ordinal)
    {
        "VfxDeltaTime", "VfxUnscaledDeltaTime", "VfxTotalTime", "VfxFrameIndex", "GameDeltaTime", "GameUnscaledDeltaTime", "GameTotalTime",
        "GameUnscaledTotalTime",
    };

    /// <summary>The built-in attribute names of DR-9.1 (anything else is a custom attribute).</summary>
    private static readonly HashSet<string> BuiltInAttributes = new HashSet<string>(StringComparer.Ordinal)
    {
        "position", "velocity", "color", "alpha", "size", "scale", "scaleX", "scaleY", "scaleZ", "angle", "angleX", "angleY", "angleZ",
        "angularVelocity", "angularVelocityX", "angularVelocityY", "angularVelocityZ", "pivot", "pivotX", "pivotY", "pivotZ", "lifetime",
        "age", "alive", "mass", "texIndex", "direction", "oldPosition", "targetPosition", "seed", "particleId", "spawnIndex", "axisX",
        "axisY", "axisZ",
    };

    /// <summary>DR-4.1 type names by Unity type name.</summary>
    private static readonly Dictionary<string, string> TypeNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "Single", "float" }, { "Int32", "int" }, { "UInt32", "uint" }, { "Boolean", "bool" }, { "Vector2", "vector2" },
        { "Vector3", "vector3" }, { "Vector4", "vector4" }, { "Color", "color" }, { "AnimationCurve", "curve" }, { "Gradient", "gradient" },
        { "Texture", "texture2d" }, { "Texture2D", "texture2d" }, { "Texture2DArray", "texture2d" }, { "Texture3D", "texture3d" },
        { "Mesh", "mesh" }, { "PointCacheAsset", "pointCache" }, { "Transform", "transform" }, { "AABox", "aabox" },
        { "OrientedBox", "orientedBox" }, { "Sphere", "sphere" }, { "TSphere", "sphere" }, { "ArcSphere", "arcSphere" },
        { "TArcSphere", "arcSphere" }, { "Cone", "cone" }, { "TCone", "cone" }, { "ArcCone", "arcCone" }, { "TArcCone", "arcCone" },
        { "Position", "position" }, { "DirectionType", "direction" }, { "Vector", "vector" },
    };

    /// <summary>The DR-4.1 types written as vectors (a linked component makes a `compose`).</summary>
    private static readonly HashSet<string> VectorTypes = new HashSet<string>(StringComparer.Ordinal) { "vector2", "vector3", "vector4", "color" };
    /// <summary>The DR-4.1 types that wrap a single vector3 field and carry a space.</summary>
    private static readonly HashSet<string> WrappedVectorTypes = new HashSet<string>(StringComparer.Ordinal) { "position", "direction", "vector" };
    /// <summary>Scalar DR-4.1 types; a scalar sub-slot of a spaceable struct carries no space of its own.</summary>
    private static readonly HashSet<string> ScalarTypes = new HashSet<string>(StringComparer.Ordinal) { "float", "int", "uint", "bool" };
    /// <summary>The DR-4.1 resource kinds.</summary>
    private static readonly HashSet<string> ResourceKinds = new HashSet<string>(StringComparer.Ordinal) { "texture2d", "texture3d", "mesh", "pointCache" };

    /// <summary>The resource requests of the graph, in id order.</summary>
    public readonly List<VfxResourceRequest> Resources = new List<VfxResourceRequest>();
    /// <summary>Deviations that belong to no system.</summary>
    public readonly List<VfxDeviation> GraphDeviations = new List<VfxDeviation>();

    /// <summary>Resource ids by asset (object identity, portable across Unity versions' instance-id APIs).</summary>
    private readonly Dictionary<UnityEngine.Object, string> resourceIdsByAsset = new Dictionary<UnityEngine.Object, string>();
    /// <summary>Resource ids by the GUID of an asset that does not resolve.</summary>
    private readonly Dictionary<string, string> resourceIdsByMissingGuid = new Dictionary<string, string>(StringComparer.Ordinal);
    /// <summary>Operators being converted on the current path (cycle guard).</summary>
    private readonly HashSet<object> operatorsInProgress = new HashSet<object>();
    /// <summary>The system whose slots are being converted, or null.</summary>
    private VfxSystemDescription currentSystem;

    /// <summary>Starts converting the slots of a system (deviations and custom attributes go to it).</summary>
    /// <param name="system">The system.</param>
    public void BeginSystem(VfxSystemDescription system)
    {
        currentSystem = system;
    }

    /// <summary>Adds a deviation to the current system once (or to the graph when no system is current).</summary>
    /// <param name="step">The ladder step.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="effect">The effect text.</param>
    public void AddSystemDeviation(string step, string subject, string effect)
    {
        List<VfxDeviation> deviations = currentSystem != null ? currentSystem.Deviations : GraphDeviations;
        foreach (VfxDeviation existing in deviations)
        {
            if (existing.Step == step && existing.Subject == subject && existing.Effect == effect) return;
        }
        deviations.Add(new VfxDeviation(step, subject, effect));
    }

    /// <summary>Records a custom attribute on the current system when it is not a built-in.</summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="type">The DR-3 custom attribute type.</param>
    public void RecordCustomAttribute(string name, string type)
    {
        if (currentSystem == null || string.IsNullOrEmpty(name) || BuiltInAttributes.Contains(name)) return;
        foreach (VfxCustomAttribute existing in currentSystem.CustomAttributes)
        {
            if (existing.Name == name) return;
        }
        currentSystem.CustomAttributes.Add(new VfxCustomAttribute { Name = name, Type = type });
    }

    // ----- slots (DR-6.4.1) -----

    /// <summary>Converts one input slot: its link, else a compose / struct of linked sub-slots, else a constant.</summary>
    /// <param name="slot">The input slot.</param>
    /// <returns>The expression.</returns>
    public VfxExpression ConvertSlot(object slot)
    {
        List<object> links = VisualEffectReflection.SlotLinks(slot);
        if (links.Count > 0) return ConvertSource(links[0], slot);
        if (HasLinkedDescendant(slot)) return ComposeFromChildren(slot);
        VfxExpression constant = EncodeExpression(VisualEffectReflection.SlotValue(slot), VisualEffectReflection.SlotType(slot), slot);
        ApplySpace(constant, slot);
        return constant;
    }

    /// <summary>True when any sub-slot (at any depth) has a link.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>True when a descendant is linked.</returns>
    private static bool HasLinkedDescendant(object slot)
    {
        foreach (object child in VisualEffectReflection.SlotChildren(slot))
        {
            if (VisualEffectReflection.SlotLinks(child).Count > 0 || HasLinkedDescendant(child)) return true;
        }
        return false;
    }

    /// <summary>Builds `compose` (vectors), the wrapped vector (position / direction / vector) or `struct` from sub-slots.</summary>
    /// <param name="slot">The compound slot.</param>
    /// <returns>The expression.</returns>
    private VfxExpression ComposeFromChildren(object slot)
    {
        string type = TypeName(VisualEffectReflection.SlotType(slot));
        List<object> children = VisualEffectReflection.SlotChildren(slot);
        VfxExpression expression;
        if (VectorTypes.Contains(type))
        {
            expression = new VfxExpression { Op = "compose", Type = type, Args = new List<VfxExpression>() };
            foreach (object child in children) expression.Args.Add(ConvertSlot(child));
        }
        else if (WrappedVectorTypes.Contains(type) && children.Count == 1)
        {
            expression = ConvertSlot(children[0]);
            expression.Type = type;
        }
        else
        {
            expression = new VfxExpression { Op = "struct", Type = type, Fields = new Dictionary<string, VfxExpression>(StringComparer.Ordinal) };
            foreach (object child in children) expression.Fields[VisualEffectReflection.SlotName(child)] = ConvertSlot(child);
        }
        ApplySpace(expression, slot);
        return expression;
    }

    /// <summary>Sets `space` on a spaceable slot's (non-scalar) expression.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="slot">The slot.</param>
    private static void ApplySpace(VfxExpression expression, object slot)
    {
        if (expression == null || ScalarTypes.Contains(expression.Type) || !VisualEffectReflection.SlotSpaceable(slot)) return;
        string space = VisualEffectReflection.SlotSpace(slot);
        if (space != null) expression.Space = space;
    }

    // ----- sources (DR-6.4.1 ConvertSource) -----

    /// <summary>Converts the output slot an input slot links to.</summary>
    /// <param name="outputSlot">The linked output slot.</param>
    /// <param name="consumingSlot">The input slot reading it (its stored value backs neutral nodes).</param>
    /// <returns>The expression.</returns>
    private VfxExpression ConvertSource(object outputSlot, object consumingSlot)
    {
        object parent = VisualEffectReflection.SlotParent(outputSlot);
        if (VisualEffectGraphWalk.IsA(parent, "VFXSlot")) return ConvertSubSlotSource(outputSlot, parent, consumingSlot);
        object owner = VisualEffectReflection.SlotOwner(outputSlot);
        if (owner == null) return Neutral(consumingSlot, "VFXSlot");
        if (!operatorsInProgress.Add(owner)) return Neutral(consumingSlot, owner.GetType().Name);
        try
        {
            return ConvertOwner(owner, outputSlot, consumingSlot);
        }
        finally
        {
            operatorsInProgress.Remove(owner);
        }
    }

    /// <summary>A link from a sub-slot: a vector component (`property` index or `component`), or the whole wrapped vector.</summary>
    /// <param name="outputSlot">The linked sub-slot.</param>
    /// <param name="parent">Its parent slot.</param>
    /// <param name="consumingSlot">The input slot reading it.</param>
    /// <returns>The expression.</returns>
    private VfxExpression ConvertSubSlotSource(object outputSlot, object parent, object consumingSlot)
    {
        string parentType = TypeName(VisualEffectReflection.SlotType(parent));
        VfxExpression parentExpression = ConvertSource(parent, consumingSlot);
        if (WrappedVectorTypes.Contains(parentType))
        {
            parentExpression.Type = TypeName(VisualEffectReflection.SlotType(outputSlot));
            return parentExpression;
        }
        if (!VectorTypes.Contains(parentType)) return Neutral(consumingSlot, "VFXSlot");
        int index = VisualEffectReflection.SlotChildren(parent).IndexOf(outputSlot);
        string componentType = TypeName(VisualEffectReflection.SlotType(outputSlot));
        if (parentExpression.Op == "property" && parentExpression.Index == null)
        {
            parentExpression.Index = index;
            parentExpression.Type = componentType;
            return parentExpression;
        }
        return new VfxExpression { Op = "component", Type = componentType, Index = index, Args = new List<VfxExpression> { parentExpression } };
    }

    /// <summary>Maps the owner of an output slot by its class (DR-4.2).</summary>
    /// <param name="owner">The owning operator or parameter.</param>
    /// <param name="outputSlot">The output slot.</param>
    /// <param name="consumingSlot">The input slot reading it.</param>
    /// <returns>The expression.</returns>
    private VfxExpression ConvertOwner(object owner, object outputSlot, object consumingSlot)
    {
        string ownerType = owner.GetType().Name;
        string outputType = TypeName(VisualEffectReflection.SlotType(outputSlot));
        switch (ownerType)
        {
            case "VFXParameter":
                if (VisualEffectReflection.IsExposed(owner)) return VfxExpression.Property(outputType, VisualEffectReflection.ExposedName(owner));
                return EncodeExpression(VisualEffectReflection.ParameterValue(owner), VisualEffectReflection.SlotType(outputSlot), outputSlot);
            case "VFXInlineOperator":
                List<object> inputs = VisualEffectReflection.InputSlots(owner);
                return inputs.Count > 0 ? ConvertSlot(inputs[0]) : Neutral(consumingSlot, ownerType);
            case "VFXAttributeParameter":
                return ConvertAttribute(owner, outputType);
            case "VFXDynamicBuiltInParameter":
                return ConvertBuiltin(owner, outputSlot, outputType, consumingSlot);
            case "VFXOperatorPointCache":
                return ConvertPointCache(owner, outputSlot, consumingSlot);
            default:
                return OperatorOps.TryGetValue(ownerType, out string operationName) ? ConvertOperator(owner, operationName, outputType) : Neutral(consumingSlot, ownerType);
        }
    }

    /// <summary>An operator node: its input slots converted in order (Random's seed inputs skipped), plus DR-19.4 settings.</summary>
    /// <param name="owner">The operator.</param>
    /// <param name="operationName">The DR-4.2 op.</param>
    /// <param name="outputType">The result type.</param>
    /// <returns>The expression.</returns>
    private VfxExpression ConvertOperator(object owner, string operationName, string outputType)
    {
        string ownerType = owner.GetType().Name;
        VfxExpression expression = new VfxExpression { Op = operationName, Type = outputType, Args = new List<VfxExpression>() };
        foreach (object input in VisualEffectReflection.InputSlots(owner))
        {
            string inputName = VisualEffectReflection.SlotName(input) ?? string.Empty;
            if (ownerType == "Random" && inputName.StartsWith("seed", StringComparison.Ordinal)) continue;
            expression.Args.Add(ConvertSlot(input));
        }
        if (VisualEffectGraphWalk.SettingsByType.TryGetValue(ownerType, out string[] settingNames))
        {
            expression.Settings = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string name in settingNames)
            {
                object value = SettingValue(VisualEffectReflection.Setting(owner, name));
                if (value != null) expression.Settings[name] = value;
            }
        }
        return expression;
    }

    /// <summary>An attribute read (`VFXAttributeParameter`), wrapped in a swizzle when its mask is not the full xyz.</summary>
    /// <param name="owner">The attribute parameter.</param>
    /// <param name="outputType">The output slot type.</param>
    /// <returns>The expression.</returns>
    private VfxExpression ConvertAttribute(object owner, string outputType)
    {
        string attribute = VisualEffectReflection.Setting(owner, "attribute") as string;
        object location = VisualEffectReflection.Setting(owner, "location");
        string mask = VisualEffectReflection.Setting(owner, "mask") as string;
        bool masked = !string.IsNullOrEmpty(mask) && mask != "xyz";
        VfxExpression read = VfxExpression.Attribute(masked ? "vector3" : outputType, attribute, location == null ? "Current" : location.ToString());
        RecordCustomAttribute(attribute, outputType);
        if (!masked) return read;
        VfxExpression swizzle = VfxExpression.Operator("swizzle", outputType, read);
        swizzle.Settings = new Dictionary<string, object> { { "mask", mask } };
        return swizzle;
    }

    /// <summary>A builtin read: output slot i is the i-th set bit of m_BuiltInParameters (DR-4.2 `builtin`).</summary>
    /// <param name="owner">The builtin parameter.</param>
    /// <param name="outputSlot">The output slot.</param>
    /// <param name="outputType">The output type.</param>
    /// <param name="consumingSlot">The input slot reading it.</param>
    /// <returns>The expression.</returns>
    private VfxExpression ConvertBuiltin(object owner, object outputSlot, string outputType, object consumingSlot)
    {
        object flags = VisualEffectReflection.Setting(owner, "m_BuiltInParameters");
        int slotIndex = VisualEffectReflection.OutputSlots(owner).IndexOf(outputSlot);
        string flagName = flags == null ? null : SetBitName(flags, slotIndex);
        if (flagName == null || !CarriedBuiltins.Contains(flagName)) return Neutral(consumingSlot, "VFXDynamicBuiltInParameter " + (flagName ?? "?"));
        return new VfxExpression { Op = "builtin", Type = outputType, Name = flagName };
    }

    /// <summary>The enum name of the n-th set bit of a flags value, counting from the lowest bit.</summary>
    /// <param name="flags">The flags enum value.</param>
    /// <param name="bitOrdinal">Which set bit (0 = the lowest).</param>
    /// <returns>The flag name, or null.</returns>
    private static string SetBitName(object flags, int bitOrdinal)
    {
        long bits = Convert.ToInt64(flags);
        int seen = 0;
        for (int bit = 0; bit < MaxFlagBits && bitOrdinal >= 0; bit++)
        {
            long mask = 1L << bit;
            if ((bits & mask) == 0) continue;
            if (seen == bitOrdinal) return Enum.GetName(flags.GetType(), Enum.ToObject(flags.GetType(), mask));
            seen++;
        }
        return null;
    }

    /// <summary>A point cache operator output: `pointCacheMap` for an attribute map, `const` uint for the point count.</summary>
    /// <param name="owner">The point cache operator.</param>
    /// <param name="outputSlot">The output slot.</param>
    /// <param name="consumingSlot">The input slot reading it.</param>
    /// <returns>The expression.</returns>
    private VfxExpression ConvertPointCache(object owner, object outputSlot, object consumingSlot)
    {
        UnityEngine.Object asset = VisualEffectReflection.Setting(owner, "Asset") as UnityEngine.Object;
        string slotName = VisualEffectReflection.SlotName(outputSlot) ?? string.Empty;
        if (slotName == PointCountSlot)
        {
            object pointCount = asset == null ? null : VisualEffectReflection.ReadField(asset, "PointCount");
            return VfxExpression.Constant("uint", pointCount == null ? 0u : Convert.ToUInt32(pointCount));
        }
        if (!slotName.StartsWith(AttributeMapPrefix, StringComparison.Ordinal)) return Neutral(consumingSlot, "VFXOperatorPointCache");
        string resourceId = ResourceId(asset, "pointCache", null);
        return new VfxExpression { Op = "pointCacheMap", Type = "texture2d", Value = resourceId, Name = slotName.Substring(AttributeMapPrefix.Length) };
    }

    /// <summary>A `neutral` node: the consuming slot's stored value plus one `operator:&lt;Type&gt;` deviation (DR-4.2).</summary>
    /// <param name="consumingSlot">The input slot.</param>
    /// <param name="ownerType">The unported class.</param>
    /// <returns>The expression.</returns>
    private VfxExpression Neutral(object consumingSlot, string ownerType)
    {
        VfxExpression stored = EncodeExpression(VisualEffectReflection.SlotValue(consumingSlot), VisualEffectReflection.SlotType(consumingSlot), consumingSlot);
        AddSystemDeviation(VisualEffectClassifier.NeutralStep, "operator:" + ownerType, ownerType + " has no port; the input's stored value is used");
        return new VfxExpression { Op = "neutral", Type = stored.Type, Value = stored.Value };
    }

    // ----- values (DR-4.1 Encode) -----

    /// <summary>A `const` of a value, or a `resource` for a texture / mesh / point cache.</summary>
    /// <param name="value">The value.</param>
    /// <param name="type">The value's slot type.</param>
    /// <param name="slot">The slot (for a missing asset's GUID), or null.</param>
    /// <returns>The expression.</returns>
    private VfxExpression EncodeExpression(object value, Type type, object slot)
    {
        string typeName = TypeName(type);
        if (ResourceKinds.Contains(typeName)) return new VfxExpression { Op = "resource", Type = typeName, Value = EncodeValue(value, type, slot) };
        return VfxExpression.Constant(typeName, EncodeValue(value, type, slot));
    }

    /// <summary>
    /// Encodes a value by DR-4.1: scalars as is, vectors and colours as arrays, curves / gradients as the Shuriken JSON, assets as
    /// resource ids, VFX structs as objects of their public fields (position / direction / vector unwrapped to [x, y, z]).
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="type">The declared type (the value's own type when null).</param>
    /// <param name="slot">The slot (for a missing asset's GUID), or null.</param>
    /// <returns>The encoded value.</returns>
    public object EncodeValue(object value, Type type, object slot)
    {
        Type valueType = type ?? (value == null ? null : value.GetType());
        string typeName = TypeName(valueType);
        if (ResourceKinds.Contains(typeName)) return ResourceId(value as UnityEngine.Object, typeName, slot);
        if (value == null) return null;
        switch (value)
        {
            case float single: return single;
            case int integer: return integer;
            case uint unsigned: return unsigned;
            case bool flag: return flag;
            case Vector2 vector2: return new[] { vector2.x, vector2.y };
            case Vector3 vector3: return new[] { vector3.x, vector3.y, vector3.z };
            case Vector4 vector4: return new[] { vector4.x, vector4.y, vector4.z, vector4.w };
            case Color color: return new[] { color.r, color.g, color.b, color.a };
            case AnimationCurve curve: return VisualEffectExportTool.EncodeCurve(curve);
            case Gradient gradient: return VisualEffectExportTool.EncodeGradient(gradient);
            case Enum enumValue: return enumValue.ToString();
        }
        return EncodeStruct(value, typeName);
    }

    /// <summary>A VFX struct as an object of its public fields; position / direction / vector unwrap to their vector.</summary>
    /// <param name="value">The struct value.</param>
    /// <param name="typeName">Its DR-4.1 type.</param>
    /// <returns>The encoded object, or null for an unknown non-struct type.</returns>
    private object EncodeStruct(object value, string typeName)
    {
        Type type = value.GetType();
        if (!type.IsValueType) return null;
        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        if (WrappedVectorTypes.Contains(typeName) && fields.Length == 1) return EncodeValue(fields[0].GetValue(value), fields[0].FieldType, null);
        Dictionary<string, object> encoded = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (FieldInfo field in fields) encoded[field.Name] = EncodeValue(field.GetValue(value), field.FieldType, null);
        return encoded;
    }

    /// <summary>The resource id of an asset (a new request on first use); a null asset uses the slot's serialized GUID.</summary>
    /// <param name="asset">The asset, or null.</param>
    /// <param name="kind">The resource kind.</param>
    /// <param name="slot">The slot (for a missing asset's GUID), or null.</param>
    /// <returns>The id, or null when there is neither an asset nor a GUID.</returns>
    private string ResourceId(UnityEngine.Object asset, string kind, object slot)
    {
        string missingGuid = asset == null && slot != null ? VisualEffectReflection.SlotSerializedGuid(slot) : null;
        if (asset == null && missingGuid == null) return null;
        bool known = asset != null ? resourceIdsByAsset.TryGetValue(asset, out string existingId) : resourceIdsByMissingGuid.TryGetValue(missingGuid, out existingId);
        if (known) return existingId;
        string id = "res" + Resources.Count;
        if (asset != null) resourceIdsByAsset[asset] = id;
        else resourceIdsByMissingGuid[missingGuid] = id;
        Resources.Add(new VfxResourceRequest { Id = id, Kind = kind, Asset = asset, MissingGuid = missingGuid });
        return id;
    }

    /// <summary>The DR-4.1 type name of a Unity type (the raw class name for types DR-4.1 does not list).</summary>
    /// <param name="type">The type, or null.</param>
    /// <returns>The type name.</returns>
    public static string TypeName(Type type)
    {
        if (type == null) return "unknown";
        return TypeNames.TryGetValue(type.Name, out string typeName) ? typeName : type.Name;
    }

    /// <summary>A setting value as DR-3 data: enums as names, scalars and strings as is, vectors as arrays, objects dropped.</summary>
    /// <param name="value">The raw setting.</param>
    /// <returns>The encoded setting, or null.</returns>
    public static object SettingValue(object value)
    {
        switch (value)
        {
            case null: return null;
            case Enum enumValue: return enumValue.ToString();
            case string text: return text;
            case bool flag: return flag;
            case float single: return single;
            case int integer: return integer;
            case uint unsigned: return unsigned;
            case Vector2 vector2: return new[] { vector2.x, vector2.y };
            case Vector3 vector3: return new[] { vector3.x, vector3.y, vector3.z };
            case UnityEngine.Object _: return null;
            default: return value.ToString();
        }
    }
}
#endif
