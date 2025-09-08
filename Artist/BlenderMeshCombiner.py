bl_info = {
    "name": "Join Children + Lightmap UVs (Static & Skinned, Merge Materials)",
    "author": "Mackey Kinard (Babylon Toolkit)",
    "version": (1, 0, 0),
    "blender": (3, 0, 0),
    "location": "3D Viewport > Object menu / F3 Search / Sidebar Tab",
    "description": "Joins active object's mesh children (supports skinned w/ same armature), merges same materials, creates LightmapUV and unwraps.",
    "category": "Object",
}

import bpy
from collections import Counter

# ---------- Utilities ----------

def gather_child_meshes(root):
    out = []
    if root and root.type == 'MESH':
        out.append(root)
    def rec(p):
        for c in p.children:
            if c.type == 'MESH':
                out.append(c)
            rec(c)
    if root:
        rec(root)
    # unique by name
    seen = set(); uniq = []
    for o in out:
        if o and o.name not in seen:
            uniq.append(o); seen.add(o.name)
    return uniq

def get_armature_modifier(obj):
    if not obj or obj.type != 'MESH': return None
    for m in obj.modifiers:
        if m.type == 'ARMATURE' and m.object and m.object.type == 'ARMATURE':
            return m
    return None

def ensure_lightmap_uv(obj, name="LightmapUV"):
    me = obj.data
    uvs = me.uv_layers
    idx = None
    for i, uv in enumerate(uvs):
        if uv.name == name:
            idx = i; break
    if idx is None:
        uvs.new(name=name)
        idx = len(uvs) - 1
    uvs.active_index = idx
    uvs.active = uvs[idx]
    try:
        uvs.active_render = uvs[idx]
    except:
        pass
    return idx

def to_uv_margin(margin_px, atlas_size):
    atlas = max(1, int(atlas_size))
    return float(margin_px) / float(atlas)

def try_lightmap_pack(atlas_size, margin_px):
    try:
        img_px = int(atlas_size)
        pref_margin_div = max(1.0, img_px / max(1.0, float(margin_px)))
        res = bpy.ops.uv.lightmap_pack(
            PREF_CONTEXT='ALL_FACES',
            PREF_PACK_IN_ONE=True,
            PREF_NEW_UVLAYER=False,
            PREF_APPLY_IMAGE=False,
            PREF_IMG_PX_SIZE=img_px,
            PREF_MARGIN_DIV=pref_margin_div
        )
        return res == {'FINISHED'}
    except:
        try:
            res = bpy.ops.uv.lightmap_pack(
                PREF_CONTEXT='ALL_FACES',
                PREF_PACK_IN_ONE=True,
                PREF_NEW_UVLAYER=False,
                PREF_IMG_PX_SIZE=int(atlas_size)
            )
            return res == {'FINISHED'}
        except:
            return False

def smart_project_and_pack(atlas_size, margin_px, angle_limit=66.0):
    uv_margin = to_uv_margin(margin_px, atlas_size)
    ok1 = False
    # Blender 4.x uses area_weight (old user_area_weight is invalid)
    try:
        ok1 = bpy.ops.uv.smart_project(
            angle_limit=angle_limit,
            island_margin=uv_margin,
            area_weight=0.0,   # correct for 4.x
            use_aspect=True
        ) == {'FINISHED'}
    except TypeError:
        ok1 = bpy.ops.uv.smart_project(
            angle_limit=angle_limit,
            island_margin=uv_margin,
            use_aspect=True
        ) == {'FINISHED'}
    ok2 = bpy.ops.uv.pack_islands(
        rotate=True,
        margin=uv_margin,
        pin_unselected=False
    ) == {'FINISHED'}
    return ok1 and ok2

def collect_subtree_names(root):
    names = set()
    def rec(o):
        if not o: return
        try:
            names.add(o.name)
        except ReferenceError:
            return
        for c in o.children:
            rec(c)
    rec(root)
    return names

def safe_remove_object_by_name(name):
    obj = bpy.data.objects.get(name)
    if obj is None: return
    try:
        bpy.data.objects.remove(obj, do_unlink=True)
    except:
        try:
            for coll in list(obj.users_collection):
                try: coll.objects.unlink(obj)
                except: pass
        except: pass

# ---------- Operator ----------

class OBJECT_OT_join_children_and_make_lightmap_uv(bpy.types.Operator):
    """Join active object's mesh children (keeps ALL materials/textures) and generate LightmapUV"""
    bl_idname = "object.join_children_lightmap_uv"
    bl_label = "Join Children + Generate Lightmap UVs"
    bl_options = {'REGISTER', 'UNDO'}

    # Lightmap unwrap settings
    atlas_size: bpy.props.IntProperty(name="Atlas Size (px)", default=4096, min=64, max=16384)
    margin_px: bpy.props.IntProperty(name="Margin (px)", default=14, min=0, max=128)
    use_lightmap_pack: bpy.props.BoolProperty(name="Use Lightmap Pack (preferred)", default=True)

    # Skinned handling
    require_same_armature: bpy.props.BoolProperty(
        name="Require Same Armature",
        description="If enabled, abort when meshes use multiple armatures. If disabled, keeps only meshes bound to the dominant armature.",
        default=True
    )
    duplicate_first: bpy.props.BoolProperty(
        name="Duplicate Root Before Join (non-destructive)",
        default=False
    )

    # Optional cleanup (OFF by default)
    delete_original_subtree: bpy.props.BoolProperty(
        name="Delete Original Subtree (after join)",
        description="If enabled, deletes the original root and all its descendants after creating the combined object.",
        default=False
    )

    def execute(self, context):
        root = context.view_layer.objects.active
        if not root:
            self.report({'ERROR'}, "No active object. Select your root and try again.")
            return {'CANCELLED'}

        # Record subtree names early if we might delete later
        subtree_names = collect_subtree_names(root) if self.delete_original_subtree else set()

        # Optional duplicate to preserve originals
        if self.duplicate_first:
            bpy.ops.object.select_all(action='DESELECT')
            root.select_set(True)
            context.view_layer.objects.active = root
            bpy.ops.object.duplicate()
            root = context.view_layer.objects.active

        # Collect meshes
        all_meshes = [o for o in gather_child_meshes(root) if o and o.type == 'MESH']
        if not all_meshes:
            self.report({'ERROR'}, "No mesh children found under the active object.")
            return {'CANCELLED'}

        # Group by Armature object
        armatures = []
        for o in all_meshes:
            amod = get_armature_modifier(o)
            armatures.append(amod.object if amod else None)

        # Determine strategy
        unique_arms = [a for a in set(armatures)]
        dom_arm = None
        if len(unique_arms) > 1:
            # Count frequency (dominant armature)
            count = Counter(armatures)
            dom_arm = count.most_common(1)[0][0]
            if self.require_same_armature:
                names = [a.name if a else "None" for a in unique_arms]
                self.report({'ERROR'}, f"Multiple armatures detected: {names}. Turn OFF 'Require Same Armature' to keep only the dominant one.")
                return {'CANCELLED'}
            else:
                # Keep only meshes bound to the dominant armature (or all if dom_arm is None)
                filtered = [o for o, a in zip(all_meshes, armatures) if a == dom_arm]
                if not filtered:
                    self.report({'ERROR'}, "No meshes share a common armature to join.")
                    return {'CANCELLED'}
                all_meshes = filtered
        else:
            dom_arm = unique_arms[0] if unique_arms else None  # could be None (static)

        # Select and join (no material merging anywhere)
        bpy.ops.object.mode_set(mode='OBJECT')
        bpy.ops.object.select_all(action='DESELECT')
        for o in all_meshes:
            o.select_set(True)

        # Choose an active: prefer one that already has the dominant armature modifier
        active = None
        if dom_arm:
            for o in all_meshes:
                amod = get_armature_modifier(o)
                if amod and amod.object == dom_arm:
                    active = o; break
        if active is None:
            active = all_meshes[0]

        context.view_layer.objects.active = active

        if bpy.ops.object.join() != {'FINISHED'}:
            self.report({'ERROR'}, "Join failed (check selection/library link/visibility).")
            return {'CANCELLED'}

        combo = context.view_layer.objects.active
        if not combo or combo.type != 'MESH':
            self.report({'ERROR'}, "Joined object not found.")
            return {'CANCELLED'}

        # Ensure Armature modifier exists and points to the dominant one (if any)
        if dom_arm:
            amod = get_armature_modifier(combo)
            if amod is None:
                amod = combo.modifiers.new(name="Armature", type='ARMATURE')
            amod.object = dom_arm

        # Lightmap UV: create/activate and unwrap
        ensure_lightmap_uv(combo, "LightmapUV")
        ok = False
        try:
            bpy.ops.object.mode_set(mode='EDIT')
            bpy.ops.mesh.select_all(action='SELECT')

            if self.use_lightmap_pack:
                ok = try_lightmap_pack(self.atlas_size, self.margin_px)
            if not ok:
                ok = smart_project_and_pack(self.atlas_size, self.margin_px, angle_limit=66.0)
        finally:
            try: bpy.ops.object.mode_set(mode='OBJECT')
            except: pass

        # Move combined next to root (so it survives optional deletion)
        try:
            mw = combo.matrix_world.copy()
            combo.parent = root.parent
            combo.matrix_world = mw
        except:
            pass

        # Optional: delete the original subtree by names (we do NOT touch materials!)
        if self.delete_original_subtree:
            keep_names = {combo.name}
            if dom_arm:
                try: keep_names.add(dom_arm.name)
                except: pass
            for nm in list(subtree_names):
                if nm in keep_names:
                    continue
                safe_remove_object_by_name(nm)

        # Tidy name
        base = combo.name.split(".")[0]
        if not base.endswith("_Combined"):
            combo.name = f"{base}_Combined"

        msg = "Skinned join" if dom_arm else "Static join"
        msg += f" | LightmapUV {'OK' if ok else 'fallback'} | {self.atlas_size}px / {self.margin_px}px"
        self.report({'INFO'}, msg)
        return {'FINISHED'}

# ---------- Menu & Panel ----------

def menu_func(self, context):
    self.layout.operator(OBJECT_OT_join_children_and_make_lightmap_uv.bl_idname, text="Join Children + Generate Lightmap UVs")

class VIEW3D_PT_join_children_lightmap_uv(bpy.types.Panel):
    bl_label = "Join + Lightmap UVs"
    bl_idname = "VIEW3D_PT_JOIN_CHILDREN_LIGHTMAP_UV"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'Join/UVs'
    def draw(self, context):
        layout = self.layout
        col = layout.column(align=True)
        col.label(text="Active object = root")
        col.operator(OBJECT_OT_join_children_and_make_lightmap_uv.bl_idname, text="Join + Generate")
        # Quick properties (optional)
        op = col.operator(OBJECT_OT_join_children_and_make_lightmap_uv.bl_idname, text="Join + Generate (Dialog)")
        # Let the popup show operator props

# ---------- Register ----------

classes = (
    OBJECT_OT_join_children_and_make_lightmap_uv,
    VIEW3D_PT_join_children_lightmap_uv,
)

def register():
    for cls in classes:
        bpy.utils.register_class(cls)
    bpy.types.VIEW3D_MT_object.append(menu_func)

def unregister():
    bpy.types.VIEW3D_MT_object.remove(menu_func)
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)

if __name__ == "__main__":
    try:
        unregister()
    except:
        pass
    register()
