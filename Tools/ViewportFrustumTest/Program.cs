using System.Numerics;
using System.Reflection;
using System.Text.Json;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Scene;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

var test = typeof(EditorObject).GetMethod("IsAABBInFrustum", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Frustum test not found");

void Check(string name, Matrix4x4 vp, AABB bounds, bool expected)
{
    bool actual = (bool)(test.Invoke(null, [vp, bounds])
        ?? throw new InvalidOperationException("Frustum test returned null"));
    if (actual != expected)
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    Console.WriteLine($"PASS {name}");
}

var identity = Matrix4x4.Identity;
Check("inside", identity, new AABB(new(-0.5f), new(0.5f)), true);
Check("outside", identity, new AABB(new(2f, -0.5f, -0.5f), new(3f, 0.5f, 0.5f)), false);
Check("wide strip crosses view without inside corners", identity,
    new AABB(new(-10f, -0.5f, -0.1f), new(10f, 0.5f, 0.1f)), true);
Check("view contained inside bounds", identity, new AABB(new(-10f), new(10f)), true);
Check("touching frustum edge", identity,
    new AABB(new(1f, -0.5f, -0.5f), new(2f, 0.5f, 0.5f)), true);
var perspective = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, 0.1f, 100f);
Check("perspective strip crossing view", perspective,
    new AABB(new(-10f, -0.5f, -2f), new(10f, 0.5f, -1.5f)), true);
Check("behind camera", perspective,
    new AABB(new(-0.5f, -0.5f, 1f), new(0.5f, 0.5f, 2f)), false);

var glbLocal = new AABB(new Vector3(3f, -1f, -0.5f), new Vector3(5f, 1f, 0.5f));
var glbTransform = Matrix4x4.CreateScale(2f) * Matrix4x4.CreateRotationY(MathF.PI / 2f)
    * Matrix4x4.CreateTranslation(0f, 0f, 8f);
var glbWorld = glbLocal.Transform(glbTransform);
Check("offset rotated GLB geometry intersects view", identity, glbWorld, true);
var countGlbTriangles = typeof(GltfObject).GetMethod("CountDrawableTriangles", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("GLB triangle counter not found");
MeshGpu[] meshes = [new MeshGpu { IndexCount = 12, VertexCount = 100 },
    new MeshGpu { VertexCount = 9 }, new MeshGpu { IndexCount = 30 }];
long glbTriangles = (long)countGlbTriangles.Invoke(null, [meshes, new HashSet<int> { 2 }])!;
if (glbTriangles != 7) throw new InvalidOperationException($"GLB triangle count: expected 7, got {glbTriangles}");
Console.WriteLine("PASS GLB indexed, non-indexed and hidden mesh counts");

var terrainBounds = typeof(EditorObject).GetMethod("ExpandTerrainChunkBounds", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Terrain bounds helper not found");
var terrainLocal = new AABB(new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f));
var terrainTransform = Matrix4x4.CreateRotationZ(MathF.PI / 2f) * Matrix4x4.CreateTranslation(2f, 0f, 0f);
var terrainWorld = (AABB)terrainBounds.Invoke(null, [terrainLocal, terrainTransform, 2f])!;
Check("rotated displaced terrain reaches view", identity, terrainWorld, true);
if (terrainWorld.Min.X > 0.001f || terrainWorld.Max.X < 3.999f)
    throw new InvalidOperationException("Terrain bound does not cover displacement along rotated normal");
Console.WriteLine("PASS rotated terrain displacement bound");

var stitchGenerator = typeof(EditorObject).GetMethod("BuildTerrainStitchVertices", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Terrain skirt generator not found");
foreach (int chunks in new[] { 2, 3 })
foreach (int segments in new[] { 1, 2, 7 })
{
    const float height = 0.75f;
    var vertices = (Vertex[])stitchGenerator.Invoke(null, [chunks, segments, height, Vector3.One])!;
    if (vertices.Length != chunks * chunks * segments * 24)
        throw new InvalidOperationException("Wrong skirt triangle count");
    for (int c = 0; c < chunks * chunks; c++)
    for (int segment = 0; segment < segments; segment++)
    for (int edge = 0; edge < 4; edge++)
    {
        int offset = (c * segments * 4 + segment * 4 + edge) * 6;
        var a = vertices[offset]; var b = vertices[offset + 1]; var d = vertices[offset + 2];
        var expectedNormal = edge switch { 0 => -Vector3.UnitZ, 1 => Vector3.UnitX, 2 => Vector3.UnitZ, _ => -Vector3.UnitX };
        if (Vector3.Dot(Vector3.Cross(b.Position - a.Position, d.Position - a.Position), expectedNormal) <= 0f ||
            Vector3.Dot(Vector3.Cross(vertices[offset + 4].Position - vertices[offset + 3].Position,
                vertices[offset + 5].Position - vertices[offset + 3].Position), expectedNormal) <= 0f)
            throw new InvalidOperationException($"Skirt winding incorrect: chunk {c}, edge {edge}");
        float span = 1f / chunks, step = span / segments;
        float u = (c % chunks) * span, v = (c / chunks) * span;
        Vector2 expectedA = edge switch
        {
            0 => new(u + segment * step, v),
            1 => new(u + span, v + segment * step),
            2 => new(u + span - segment * step, v + span),
            _ => new(u, v + span - segment * step)
        };
        Vector2 expectedB = edge switch
        {
            0 => new(u + (segment + 1) * step, v),
            1 => new(u + span, v + (segment + 1) * step),
            2 => new(u + span - (segment + 1) * step, v + span),
            _ => new(u, v + span - (segment + 1) * step)
        };
        if (Vector2.Distance(a.UV, expectedA) > 1e-6f || Vector2.Distance(b.UV, expectedB) > 1e-6f)
            throw new InvalidOperationException("Skirt edge does not match the terrain LOD grid");
        for (int i = 0; i < 6; i++)
        {
            var vertex = vertices[offset + i];
            if (vertex.Normal != Vector3.UnitY || MathF.Abs(vertex.UV.X - (vertex.Position.X + 0.5f)) > 1e-6f ||
                MathF.Abs(vertex.UV.Y - (vertex.Position.Z + 0.5f)) > 1e-6f ||
                (vertex.Position.Y != 0f && vertex.Position.Y != -height))
                throw new InvalidOperationException("Skirt displacement UV or height mismatch");
        }
        if (a.UV != d.UV || vertices[offset + 1].UV != vertices[offset + 4].UV)
            throw new InvalidOperationException("Skirt top and bottom must sample the same height");
    }
}
var savedStitch = new EditorObjectData { TerrainStitchEnabled = true, TerrainStitchHeight = 2.5f, TerrainStitchCull = 2 };
var loadedStitch = JsonSerializer.Deserialize<EditorObjectData>(JsonSerializer.Serialize(savedStitch))!;
if (!loadedStitch.TerrainStitchEnabled || loadedStitch.TerrainStitchHeight != 2.5f || loadedStitch.TerrainStitchCull != 2)
    throw new InvalidOperationException("Terrain skirt settings did not round-trip");
var legacyStitch = JsonSerializer.Deserialize<EditorObjectData>("{}")!;
if (legacyStitch.TerrainStitchEnabled || legacyStitch.TerrainStitchHeight != 20f || legacyStitch.TerrainStitchCull != 0)
    throw new InvalidOperationException("Terrain skirt legacy defaults changed");
var skirtObject = new EditorObject(EditorPrimitiveType.Plane) { PbrVertexChunk = 2, TerrainStitchEnabled = true, TerrainStitchHeight = 5f };
var skirtBottom = Vector3.Transform(new Vector3(0f, -skirtObject.TerrainStitchHeight, 0f), skirtObject.WorldMatrix);
if (skirtObject.WorldAABB.Min.Y > skirtBottom.Y) throw new InvalidOperationException("Terrain skirt missing from world bounds");
Console.WriteLine("PASS terrain skirt geometry, winding, bounds, and JSON defaults/round-trip");

var occType = typeof(EditorObject).Assembly.GetType("DarkEngine3D_gl_csharp.Engine.Objects.ViewportOcclusion")
    ?? throw new InvalidOperationException("Occlusion test not found");
var occlusion = occType.GetMethod("IsOccluded", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("IsOccluded not found");
void CheckOcclusion(string name, AABB bounds, Matrix4x4 box, Vector3 camera, bool expected)
{
    bool actual = (bool)(occlusion.Invoke(null, [bounds, box, camera, perspective])
        ?? throw new InvalidOperationException("Occlusion test returned null"));
    if (actual != expected) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    Console.WriteLine($"PASS {name}");
}
var wall = Matrix4x4.CreateScale(4f, 4f, 0.5f) * Matrix4x4.CreateTranslation(0f, 0f, -2f);
var hidden = new AABB(new(-0.5f, -0.5f, -6f), new(0.5f, 0.5f, -5f));
CheckOcclusion("fully occluded", hidden, wall, Vector3.Zero, true);
CheckOcclusion("partially visible", new AABB(new(4f, -0.5f, -6f), new(6f, 0.5f, -5f)), wall, Vector3.Zero, false);
CheckOcclusion("in front of wall", new AABB(new(-0.1f, -0.1f, -1.5f), new(0.1f, 0.1f, -1f)), wall, Vector3.Zero, false);
CheckOcclusion("camera inside wall", hidden, Matrix4x4.CreateScale(4f), Vector3.Zero, false);
CheckOcclusion("target crosses near plane", new AABB(new(-0.1f, -0.1f, -1f), new(0.1f, 0.1f, 0f)), wall, Vector3.Zero, false);
CheckOcclusion("small wall cannot cover target", hidden, Matrix4x4.CreateScale(0.1f) * Matrix4x4.CreateTranslation(0f, 0f, -2f), Vector3.Zero, false);
CheckOcclusion("wall crosses near plane", hidden, Matrix4x4.CreateScale(4f, 4f, 1f) * Matrix4x4.CreateTranslation(0f, 0f, -0.5f), Vector3.Zero, false);

// Independently trace rays to all target corners when the screen-space test culls.
// A false positive would hide visible geometry, so it must fail the harness.
var random = new Random(1234);
int culledCases = 0;
for (int sample = 0; sample < 2000; sample++)
{
    float x = (float)(random.NextDouble() * 4 - 2);
    float y = (float)(random.NextDouble() * 4 - 2);
    float z = (float)(-3 - random.NextDouble() * 8);
    var target = new AABB(new Vector3(x - 0.3f, y - 0.3f, z - 0.3f), new Vector3(x + 0.3f, y + 0.3f, z + 0.3f));
    float angle = (float)(random.NextDouble() - 0.5);
    var box = Matrix4x4.CreateScale(4f, 4f, 0.5f) * Matrix4x4.CreateRotationY(angle) * Matrix4x4.CreateTranslation(0f, 0f, -2f);
    bool result = (bool)occlusion.Invoke(null, [target, box, Vector3.Zero, perspective])!;
    if (!result) continue;
    culledCases++;
    if (!Matrix4x4.Invert(box, out var inv)) throw new InvalidOperationException("Invalid test box");
    Vector3 origin = Vector3.Transform(Vector3.Zero, inv);
    for (int corner = 0; corner < 8; corner++)
    {
        var world = new Vector3((corner & 1) == 0 ? target.Min.X : target.Max.X,
            (corner & 2) == 0 ? target.Min.Y : target.Max.Y,
            (corner & 4) == 0 ? target.Min.Z : target.Max.Z);
        var direction = Vector3.Transform(world, inv) - origin;
        float enter = 0f, exit = 1f;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            if (MathF.Abs(d) < 1e-8f)
            {
                if (o < -0.5f || o > 0.5f) exit = -1f;
                continue;
            }
            float a = (-0.5f - o) / d, b = (0.5f - o) / d;
            enter = MathF.Max(enter, MathF.Min(a, b));
            exit = MathF.Min(exit, MathF.Max(a, b));
        }
        if (enter > exit || exit < 0f || enter >= 1f)
            throw new InvalidOperationException($"False occlusion in sample {sample}, corner {corner}");
    }
}
if (culledCases == 0) throw new InvalidOperationException("Random test did not exercise occlusion");
Console.WriteLine($"PASS randomized ray checks ({culledCases} occluded of 2000 cases)");
