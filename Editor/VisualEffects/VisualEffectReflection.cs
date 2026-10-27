#if UNITY_EDITOR && BT_VFX_GRAPH
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine.VFX;

/// <summary>
/// Cached, read-only reflection accessors over VFX Graph's internal editor model (vfx-graph-port plan D5, DR-6.3). The model
/// types are internal to Unity.VisualEffectGraph.Editor and UnityEditor.VFXModule, so every member is reached by name. An
/// accessor whose member is missing returns null or a default and records the member in <see cref="MissingMembers"/>; the
/// reader then reports the graph unsupported instead of writing half a description. Nothing here calls a setter.
/// </summary>
internal static class VisualEffectReflection
{
    /// <summary>Instance members of any visibility.</summary>
    private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    /// <summary>Static members of any visibility.</summary>
    private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    /// <summary>The internal resource type that owns a .vfx asset's graph.</summary>
    private const string ResourceTypeName = "UnityEditor.VFX.VisualEffectResource, UnityEditor.VFXModule";
    /// <summary>The serialized path of an object slot's stored reference (a JSON string carrying the asset GUID).</summary>
    private const string SerializedObjectValuePath = "m_MasterData.m_Value.m_SerializableObject";

    /// <summary>A GUID inside a VFX serialized object reference.</summary>
    private static readonly Regex SerializedGuid = new Regex("\"guid\"\\s*:\\s*\"([0-9a-fA-F]{32})\"");
    /// <summary>Members that were not found since the last <see cref="ResetMissingMembers"/>, as "Type.member".</summary>
    public static readonly SortedSet<string> MissingMembers = new SortedSet<string>(StringComparer.Ordinal);

    /// <summary>Resolved properties by declaring type and name (null when missing).</summary>
    private static readonly Dictionary<string, PropertyInfo> propertyCache = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
    /// <summary>Resolved fields by declaring type and name (null when missing).</summary>
    private static readonly Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
    /// <summary>Resolved parameterless or single-argument methods by declaring type and name (null when missing).</summary>
    private static readonly Dictionary<string, MethodInfo> methodCache = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
    /// <summary>The resolved VisualEffectResource type (null when the module is missing).</summary>
    private static Type resourceType;

    /// <summary>Clears <see cref="MissingMembers"/> before a read.</summary>
    public static void ResetMissingMembers()
    {
        MissingMembers.Clear();
    }

    // ----- resource -----

    /// <summary>VisualEffectResource.GetResourceAtPath(path), or null.</summary>
    /// <param name="path">The .vfx asset path.</param>
    /// <returns>The resource, or null.</returns>
    public static object ResourceAt(string path)
    {
        if (resourceType == null) resourceType = Type.GetType(ResourceTypeName);
        if (resourceType == null)
        {
            MissingMembers.Add("VisualEffectResource");
            return null;
        }
        MethodInfo getResource = resourceType.GetMethod("GetResourceAtPath", StaticFlags, null, new[] { typeof(string) }, null);
        if (getResource == null)
        {
            MissingMembers.Add("VisualEffectResource.GetResourceAtPath");
            return null;
        }
        return getResource.Invoke(null, new object[] { path });
    }

    /// <summary>The resource's graph (a VFXGraph ScriptableObject), or null.</summary>
    /// <param name="resource">The VisualEffectResource.</param>
    /// <returns>The graph, or null.</returns>
    public static object GraphOf(object resource)
    {
        return ReadProperty(resource, "graph");
    }

    /// <summary>VisualEffectResource.updateMode as an integer.</summary>
    /// <param name="resource">The resource.</param>
    /// <returns>The VFXUpdateMode value.</returns>
    public static int ResourceUpdateMode(object resource)
    {
        return ToInt(ReadProperty(resource, "updateMode"), 0);
    }

    /// <summary>VisualEffectResource.cullingFlags as an integer.</summary>
    /// <param name="resource">The resource.</param>
    /// <returns>The VFXCullingFlags value.</returns>
    public static int ResourceCullingFlags(object resource)
    {
        return ToInt(ReadProperty(resource, "cullingFlags"), 0);
    }

    /// <summary>VisualEffectResource.preWarmDeltaTime, seconds.</summary>
    /// <param name="resource">The resource.</param>
    /// <returns>The prewarm step length.</returns>
    public static float ResourcePrewarmDeltaTime(object resource)
    {
        object value = ReadProperty(resource, "preWarmDeltaTime");
        return value == null ? 0f : Convert.ToSingle(value);
    }

    /// <summary>VisualEffectResource.preWarmStepCount.</summary>
    /// <param name="resource">The resource.</param>
    /// <returns>The prewarm step count.</returns>
    public static int ResourcePrewarmStepCount(object resource)
    {
        return ToInt(ReadProperty(resource, "preWarmStepCount"), 0);
    }

    /// <summary>VisualEffectResource.initialEventName.</summary>
    /// <param name="resource">The resource.</param>
    /// <returns>The event name, or null.</returns>
    public static string ResourceInitialEventName(object resource)
    {
        return ReadProperty(resource, "initialEventName") as string;
    }

    // ----- models and contexts -----

    /// <summary>VFXModel.children as a list (most-derived declaration wins).</summary>
    /// <param name="model">The model.</param>
    /// <returns>The children (empty when missing).</returns>
    public static List<object> Children(object model)
    {
        return ToList(ReadProperty(model, "children"));
    }

    /// <summary>VFXModel.GetSettingValue(name); null when the model has no such setting.</summary>
    /// <param name="model">The model.</param>
    /// <param name="name">The setting field name.</param>
    /// <returns>The setting value, or null.</returns>
    public static object Setting(object model, string name)
    {
        MethodInfo getSetting = FindMethod(model, "GetSettingValue", 1);
        if (getSetting == null) return null;
        try
        {
            return getSetting.Invoke(model, new object[] { name });
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is ArgumentException)
        {
            return null;
        }
    }

    /// <summary>VFXContext.contextType as its enum name ("Spawner", "Init", "Update", "Output", "Event", …).</summary>
    /// <param name="context">The context.</param>
    /// <returns>The type name, or null.</returns>
    public static string ContextType(object context)
    {
        object value = ReadProperty(context, "contextType");
        return value == null ? null : value.ToString();
    }

    /// <summary>VFXContext.label.</summary>
    /// <param name="context">The context.</param>
    /// <returns>The label, or null.</returns>
    public static string Label(object context)
    {
        return ReadProperty(context, "label") as string;
    }

    /// <summary>VFXContext.inputFlowSlot: per flow slot, the contexts linked to it (VFXContextSlot.link[*].context).</summary>
    /// <param name="context">The context.</param>
    /// <returns>One list of linked contexts per input flow slot.</returns>
    public static List<List<object>> InputFlowSlots(object context)
    {
        List<List<object>> flowSlots = new List<List<object>>();
        foreach (object flowSlot in ToList(ReadProperty(context, "inputFlowSlot")))
        {
            List<object> linked = new List<object>();
            foreach (object link in ToList(ReadField(flowSlot, "link")))
            {
                object linkedContext = ReadField(link, "context");
                if (linkedContext != null) linked.Add(linkedContext);
            }
            flowSlots.Add(linked);
        }
        return flowSlots;
    }

    /// <summary>VFXContext.GetData().</summary>
    /// <param name="context">The context.</param>
    /// <returns>The data, or null.</returns>
    public static object Data(object context)
    {
        MethodInfo getData = FindMethod(context, "GetData", 0);
        return getData == null ? null : getData.Invoke(context, null);
    }

    /// <summary>VFXDataParticle.space as "local" / "world".</summary>
    /// <param name="data">The particle data.</param>
    /// <returns>The space name, lowercase.</returns>
    public static string DataSpace(object data)
    {
        object value = ReadProperty(data, "space");
        return value == null ? "local" : value.ToString().ToLowerInvariant();
    }

    /// <summary>The context's authored blocks whose VFXBlock.enabled is true (implicit blocks are not children).</summary>
    /// <param name="context">The context.</param>
    /// <returns>The enabled blocks in order.</returns>
    public static List<object> ActiveBlocks(object context)
    {
        List<object> blocks = new List<object>();
        foreach (object block in Children(context))
        {
            if (ReadProperty(block, "enabled") is bool enabled && enabled) blocks.Add(block);
        }
        return blocks;
    }

    // ----- slots -----

    /// <summary>IVFXSlotContainer.inputSlots.</summary>
    /// <param name="container">A context, block or operator.</param>
    /// <returns>The input slots.</returns>
    public static List<object> InputSlots(object container)
    {
        return ToList(ReadProperty(container, "inputSlots"));
    }

    /// <summary>IVFXSlotContainer.outputSlots.</summary>
    /// <param name="container">An operator or parameter.</param>
    /// <returns>The output slots.</returns>
    public static List<object> OutputSlots(object container)
    {
        return ToList(ReadProperty(container, "outputSlots"));
    }

    /// <summary>VFXSlot.property.name.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The slot name.</returns>
    public static string SlotName(object slot)
    {
        object property = ReadProperty(slot, "property");
        return property == null ? null : ReadField(property, "name") as string;
    }

    /// <summary>VFXSlot.property.type.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The slot's value type.</returns>
    public static Type SlotType(object slot)
    {
        object property = ReadProperty(slot, "property");
        return property == null ? null : ReadProperty(property, "type") as Type;
    }

    /// <summary>VFXSlot.value (a sub-slot reads its field from the parent's value).</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The stored value.</returns>
    public static object SlotValue(object slot)
    {
        return ReadProperty(slot, "value");
    }

    /// <summary>VFXSlot.children (the sub-slots).</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The sub-slots.</returns>
    public static List<object> SlotChildren(object slot)
    {
        return Children(slot);
    }

    /// <summary>VFXSlot.LinkedSlots.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The slots linked to it.</returns>
    public static List<object> SlotLinks(object slot)
    {
        return ToList(ReadProperty(slot, "LinkedSlots"));
    }

    /// <summary>VFXSlot.owner (the master slot's container).</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The owning context, block, operator or parameter.</returns>
    public static object SlotOwner(object slot)
    {
        return ReadProperty(slot, "owner");
    }

    /// <summary>VFXModel.GetParent(): the parent slot of a sub-slot, or the container of a master slot.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The parent model, or null.</returns>
    public static object SlotParent(object slot)
    {
        MethodInfo getParent = FindMethod(slot, "GetParent", 0);
        return getParent == null ? null : getParent.Invoke(slot, null);
    }

    /// <summary>VFXSlot.space as "local" / "world", or null when the slot has no space.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The space name, or null.</returns>
    public static string SlotSpace(object slot)
    {
        object value = ReadProperty(slot, "space");
        if (value == null) return null;
        string space = value.ToString().ToLowerInvariant();
        return space == "local" || space == "world" ? space : null;
    }

    /// <summary>VFXSlot.spaceable.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>True when the slot carries a space.</returns>
    public static bool SlotSpaceable(object slot)
    {
        return ReadProperty(slot, "spaceable") is bool spaceable && spaceable;
    }

    /// <summary>
    /// The asset GUID an object slot stores, read from its serialized reference (`m_MasterData.m_Value.m_SerializableObject`)
    /// through a read-only SerializedObject; used to name a texture that no longer resolves.
    /// </summary>
    /// <param name="slot">A master slot of an object type.</param>
    /// <returns>The GUID, or null.</returns>
    public static string SlotSerializedGuid(object slot)
    {
        if (!(slot is UnityEngine.Object slotObject) || slotObject == null) return null;
        using (SerializedObject serializedSlot = new SerializedObject(slotObject))
        {
            SerializedProperty stored = serializedSlot.FindProperty(SerializedObjectValuePath);
            if (stored == null || stored.propertyType != SerializedPropertyType.String) return null;
            Match match = SerializedGuid.Match(stored.stringValue ?? string.Empty);
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
        }
    }

    // ----- parameters -----

    /// <summary>VFXParameter.exposed.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns>True when the parameter is exposed on the component.</returns>
    public static bool IsExposed(object parameter)
    {
        return ReadProperty(parameter, "exposed") is bool exposed && exposed;
    }

    /// <summary>VFXParameter.exposedName.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns>The exposed name.</returns>
    public static string ExposedName(object parameter)
    {
        return ReadProperty(parameter, "exposedName") as string;
    }

    /// <summary>VFXParameter.value.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns>The parameter's value.</returns>
    public static object ParameterValue(object parameter)
    {
        return ReadProperty(parameter, "value");
    }

    /// <summary>VisualEffectAsset.GetExposedProperties (public API).</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The exposed properties in graph order.</returns>
    public static List<VFXExposedProperty> ExposedProperties(VisualEffectAsset asset)
    {
        List<VFXExposedProperty> exposed = new List<VFXExposedProperty>();
        if (asset != null) asset.GetExposedProperties(exposed);
        return exposed;
    }

    /// <summary>VisualEffectAsset.GetEvents (public API).</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The event names.</returns>
    public static List<string> Events(VisualEffectAsset asset)
    {
        List<string> events = new List<string>();
        if (asset != null) asset.GetEvents(events);
        return events;
    }

    /// <summary>A public or private instance field of any object (for plain data such as VFXContextLink.context).</summary>
    /// <param name="target">The object.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or null when missing.</returns>
    public static object ReadField(object target, string name)
    {
        if (target == null) return null;
        Type type = target.GetType();
        string key = type.FullName + "." + name;
        if (!fieldCache.TryGetValue(key, out FieldInfo field))
        {
            field = FindDeclared(type, (candidate) => candidate.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly));
            fieldCache[key] = field;
        }
        if (field == null)
        {
            MissingMembers.Add(type.Name + "." + name);
            return null;
        }
        return field.GetValue(target);
    }

    // ----- helpers -----

    /// <summary>Reads an instance property, the most-derived declaration first (VFXModel&lt;,&gt; redeclares `children`).</summary>
    /// <param name="target">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or null when the target or the property is missing.</returns>
    private static object ReadProperty(object target, string name)
    {
        if (target == null) return null;
        Type type = target.GetType();
        string key = type.FullName + "." + name;
        if (!propertyCache.TryGetValue(key, out PropertyInfo property))
        {
            property = FindDeclared(type, (candidate) => candidate.GetProperty(name, InstanceFlags | BindingFlags.DeclaredOnly));
            propertyCache[key] = property;
        }
        if (property == null)
        {
            MissingMembers.Add(type.Name + "." + name);
            return null;
        }
        return property.GetValue(target);
    }

    /// <summary>Finds an instance method by name and parameter count, most-derived declaration first.</summary>
    /// <param name="target">The object.</param>
    /// <param name="name">The method name.</param>
    /// <param name="parameterCount">The number of parameters.</param>
    /// <returns>The method, or null when missing (recorded).</returns>
    private static MethodInfo FindMethod(object target, string name, int parameterCount)
    {
        if (target == null) return null;
        Type type = target.GetType();
        string key = type.FullName + "." + name + "/" + parameterCount;
        if (!methodCache.TryGetValue(key, out MethodInfo method))
        {
            method = FindDeclared(type, (candidate) =>
            {
                foreach (MethodInfo declared in candidate.GetMethods(InstanceFlags | BindingFlags.DeclaredOnly))
                {
                    if (declared.Name == name && declared.GetParameters().Length == parameterCount) return declared;
                }
                return null;
            });
            methodCache[key] = method;
        }
        if (method == null) MissingMembers.Add(type.Name + "." + name);
        return method;
    }

    /// <summary>Walks a type and its bases and returns the first member the lookup finds.</summary>
    /// <typeparam name="TMember">The member kind.</typeparam>
    /// <param name="type">The most-derived type.</param>
    /// <param name="lookup">The per-type declared-only lookup.</param>
    /// <returns>The member, or null.</returns>
    private static TMember FindDeclared<TMember>(Type type, Func<Type, TMember> lookup) where TMember : MemberInfo
    {
        for (Type candidate = type; candidate != null; candidate = candidate.BaseType)
        {
            TMember member = lookup(candidate);
            if (member != null) return member;
        }
        return null;
    }

    /// <summary>Copies an enumerable into a list (null gives an empty list).</summary>
    /// <param name="sequence">The enumerable, or null.</param>
    /// <returns>The items.</returns>
    private static List<object> ToList(object sequence)
    {
        List<object> items = new List<object>();
        if (sequence is IEnumerable enumerable)
        {
            foreach (object item in enumerable) items.Add(item);
        }
        return items;
    }

    /// <summary>Converts an enum or number to an integer.</summary>
    /// <param name="value">The value, or null.</param>
    /// <param name="fallback">The value when null.</param>
    /// <returns>The integer.</returns>
    private static int ToInt(object value, int fallback)
    {
        return value == null ? fallback : Convert.ToInt32(value);
    }
}
#endif
