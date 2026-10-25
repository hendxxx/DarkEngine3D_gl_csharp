namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>Attachment shape kind. Only <see cref="Region"/> (a rectangular sprite region)
/// exists in v1; the enum lets Mesh/BoundingBox land later without a schema break.</summary>
public enum AttachmentKind
{
    Region,
}

/// <summary>One bone of a rig. Parents are referenced by name (index 0 should be the
/// root, sitting at the feet anchor). All values are the SETUP pose (local space) —
/// keyframed animation lives on the clip's tracks, not here.</summary>
public sealed class Bone2D
{
    public string Name { get; set; } = "";
    /// <summary>Parent bone name; empty/null = root.</summary>
    public string? Parent { get; set; }
    /// <summary>Length in pixels (for drawing gizmos/handles; not needed to bake).</summary>
    public float Length { get; set; }

    // Setup-pose local transform.
    public float X { get; set; }
    public float Y { get; set; }
    public float Rotation { get; set; }
    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;
    public float ShearX { get; set; }
    public float ShearY { get; set; }

    public Bone2D Clone() => (Bone2D)MemberwiseClone();
}

/// <summary>One draw slot. Slot list order IS the draw order (back to front).</summary>
public sealed class Slot2D
{
    public string Name { get; set; } = "";
    /// <summary>Bone this slot is attached to.</summary>
    public string Bone { get; set; } = "";
    /// <summary>Default attachment shown when the skin does not override this slot.</summary>
    public string? Attachment { get; set; }
    // Tint applied to the attachment (1,1,1,1 = untouched).
    public float R { get; set; } = 1f;
    public float G { get; set; } = 1f;
    public float B { get; set; } = 1f;
    public float A { get; set; } = 1f;
    public bool Visible { get; set; } = true;

    public Slot2D Clone() => (Slot2D)MemberwiseClone();
}

/// <summary>Region attachment: one frame of one source sheet, placed on a slot.
/// Pivot is 0..1 inside the region (0.5/1 = bottom-center = feet anchor, matching the
/// sheet master-box convention the renderer already uses).</summary>
public sealed class Attachment2D
{
    public string Name { get; set; } = "";
    /// <summary>Slot this attachment is bound to.</summary>
    public string Slot { get; set; } = "";
    public AttachmentKind Kind { get; set; } = AttachmentKind.Region;

    /// <summary>Source sheet (NOT a baked atlas) the region is cropped from.</summary>
    public string SheetName { get; set; } = "";
    public int FrameIndex { get; set; }

    // Placement relative to the slot's bone.
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Rotation { get; set; }
    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;
    /// <summary>Pivot inside the region, 0..1 (0.5, 1 = bottom-center / feet).</summary>
    public float PivotX { get; set; } = 0.5f;
    public float PivotY { get; set; } = 1f;

    public Attachment2D Clone() => (Attachment2D)MemberwiseClone();
}

/// <summary>A skin: per-slot attachment overrides (outfit/weapon/hair variants). Slots
/// missing from the map fall back to <see cref="Slot2D.Attachment"/>.</summary>
public sealed class Skin2D
{
    public string Name { get; set; } = "default";
    /// <summary>slot name → attachment name.</summary>
    public Dictionary<string, string> AttachmentBySlot { get; set; } = new();

    public Skin2D Clone() => new()
    {
        Name = Name,
        AttachmentBySlot = new Dictionary<string, string>(AttachmentBySlot),
    };
}

/// <summary>A rig: bones + slots + attachments + skins (Phase 2 authoring source).
/// Phase 0 stores it in sprites.sheets.json so the format is settled and round-trips;
/// nothing outside the editor reads it — gameplay only ever sees baked atlas clips.</summary>
public sealed class AnimationRig
{
    /// <summary>Stable id (guid) referenced by <see cref="AnimationClip2D.RigId"/>.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Canonical character height in pixels → becomes the baked clip's MasterHeight.</summary>
    public float DesignHeightPx { get; set; } = 64f;

    public List<Bone2D> Bones { get; set; } = new();
    public List<Slot2D> Slots { get; set; } = new();
    public List<Attachment2D> Attachments { get; set; } = new();
    public List<Skin2D> Skins { get; set; } = new();
    public string DefaultSkin { get; set; } = "default";

    public AnimationRig Clone() => new()
    {
        Id = Id,
        Name = Name,
        DesignHeightPx = DesignHeightPx,
        Bones = Bones.Select(b => b.Clone()).ToList(),
        Slots = Slots.Select(s => s.Clone()).ToList(),
        Attachments = Attachments.Select(a => a.Clone()).ToList(),
        Skins = Skins.Select(s => s.Clone()).ToList(),
        DefaultSkin = DefaultSkin,
    };
}
