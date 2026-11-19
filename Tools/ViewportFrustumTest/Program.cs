using System.Numerics;
using System.Reflection;
using System.Text.Json;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Scene;
using DarkEngine3D_gl_csharp.Engine.Visual;
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

var markerBoundsMethod = typeof(EditorObjectManager).GetMethod("GetEditorMarkerCullBounds", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Editor marker cull bounds helper not found");
var markerCamera = new Camera(0f, 0f, 0f, 180f, 0f, 1f, 90f, 0.1f, 100f);
var offscreenCameraMarker = new EditorObject(EditorPrimitiveType.Camera)
{
    Position = new Vector3(30f, 0f, 0f),
    CameraFar = 10f,
    CameraFov = 90f
};
var cameraMarkerBounds = (AABB)(markerBoundsMethod.Invoke(null, [offscreenCameraMarker, markerCamera])
    ?? throw new InvalidOperationException("Editor marker cull bounds returned null"));
Check("offscreen camera gizmo is outside viewport frustum", identity, cameraMarkerBounds, false);
var visibleSpriteMarker = new EditorObject(EditorPrimitiveType.Sprite2D) { Position = Vector3.Zero };
var spriteMarkerBounds = (AABB)(markerBoundsMethod.Invoke(null, [visibleSpriteMarker, markerCamera])
    ?? throw new InvalidOperationException("Sprite marker cull bounds returned null"));
Check("visible sprite marker bounds intersect viewport", identity, spriteMarkerBounds, true);
var edgeStartMarker = new EditorObject(EditorPrimitiveType.Start2D) { Position = new Vector3(0f, 1.1f, 0f) };
var startMarkerBounds = (AABB)(markerBoundsMethod.Invoke(null, [edgeStartMarker, markerCamera])
    ?? throw new InvalidOperationException("Editor marker cull bounds returned null"));
Check("billboard start marker crossing viewport edge remains visible", identity, startMarkerBounds, true);
var lightRadiusMethod = typeof(EditorObjectManager).GetMethod("LightGizmoRadius", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Light gizmo bounds helper not found");
var spotlight = new EditorObject(EditorPrimitiveType.Light)
{
    LightTypeEnum = LightType.Spotlight,
    LightIntensity = 1f,
    LightConeAngle = 45f
};
float spotlightRadius = (float)(lightRadiusMethod.Invoke(null, [spotlight])
    ?? throw new InvalidOperationException("Light gizmo bounds helper returned null"));
if (spotlightRadius < MathF.Sqrt(7f * 7f + 7f * 7f))
    throw new InvalidOperationException("Spotlight cull radius does not cover the cone tip corner");
Console.WriteLine("PASS spotlight cone tip is inside its conservative cull radius");

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
var chunkBoundsMethod = typeof(EditorObject).GetMethod("GetTerrainChunkLocalBounds", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Terrain chunk bounds helper not found");
var defaultChunkedPlane = new EditorObject(EditorPrimitiveType.Plane);
if (defaultChunkedPlane.PbrVertexChunk != 8)
    throw new InvalidOperationException($"New terrain should default to 8 chunks per side, got {defaultChunkedPlane.PbrVertexChunk}");
Console.WriteLine("PASS terrain defaults to 8×8 frustum-cullable chunks");
var chunkBounds = (AABB)chunkBoundsMethod.Invoke(null, [0, 0, 8])!;
if (chunkBounds.Max.X - chunkBounds.Min.X > 0.126f || chunkBounds.Max.Z - chunkBounds.Min.Z > 0.126f)
    throw new InvalidOperationException("Terrain chunk bounds are not localized to a small terrain section");
Console.WriteLine("PASS terrain chunk bounds remain localized");
var octreeType = typeof(EditorObject).Assembly.GetType("DarkEngine3D_gl_csharp.Engine.Objects.TerrainChunkOctree")
    ?? throw new InvalidOperationException("Terrain octree not found");
var buildOctree = octreeType.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Terrain octree builder not found");
var chunkBoundsGrid = Enumerable.Range(0, 64)
    .Select(i => (AABB)chunkBoundsMethod.Invoke(null, [i % 8, i / 8, 8])!).ToArray();
var octreeRoot = buildOctree.Invoke(null, [chunkBoundsGrid, 8])
    ?? throw new InvalidOperationException("Terrain octree build returned no root");
var octreeNodeType = octreeRoot.GetType();
var octreeBoundsProperty = octreeNodeType.GetProperty("Bounds", BindingFlags.Instance | BindingFlags.NonPublic)!;
var octreeChunkIndexProperty = octreeNodeType.GetProperty("ChunkIndex", BindingFlags.Instance | BindingFlags.NonPublic)!;
var octreeChildrenProperty = octreeNodeType.GetProperty("Children", BindingFlags.Instance | BindingFlags.NonPublic)!;
var octreeRootBounds = (AABB)octreeBoundsProperty.GetValue(octreeRoot)!;
var octreeLeafIndices = new HashSet<int>();
int visitedOctreeNodes = 0;
int CountOctreeNodes(object node, Matrix4x4 transform)
{
    visitedOctreeNodes++;
    var bounds = ((AABB)octreeBoundsProperty.GetValue(node)!).Transform(transform);
    bool intersects = (bool)test.Invoke(null, [identity, bounds])!;
    if (!intersects) return 1;
    int chunkIndex = (int)octreeChunkIndexProperty.GetValue(node)!;
    if (chunkIndex >= 0)
    {
        if (!octreeLeafIndices.Add(chunkIndex)) throw new InvalidOperationException("Terrain octree has duplicate chunk leaves");
        return 1;
    }
    int nodes = 1;
    foreach (var child in (Array)octreeChildrenProperty.GetValue(node)!)
        if (child != null) nodes += CountOctreeNodes(child, transform);
    return nodes;
}
int octreeNodeCount = CountOctreeNodes(octreeRoot, Matrix4x4.Identity);
if (octreeLeafIndices.Count != 64 || octreeNodeCount != 85 || octreeRootBounds.Min.X > -0.5f || octreeRootBounds.Max.X < 0.5f)
    throw new InvalidOperationException("Terrain octree bounds/tree do not cover the complete chunk grid");
visitedOctreeNodes = 0;
int outsideOctreeVisits = CountOctreeNodes(octreeRoot, Matrix4x4.CreateTranslation(4f, 0f, 0f));
if (outsideOctreeVisits != 1 || visitedOctreeNodes != 1)
    throw new InvalidOperationException("An off-frustum terrain root should prune its whole subtree in one node test");
Console.WriteLine("PASS terrain octree covers all chunks and prunes an off-frustum subtree");
var terrainLocal = new AABB(new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f));
var terrainTransform = Matrix4x4.CreateRotationZ(MathF.PI / 2f) * Matrix4x4.CreateTranslation(2f, 0f, 0f);
var terrainWorld = (AABB)terrainBounds.Invoke(null, [terrainLocal, terrainTransform, 2f])!;
Check("rotated displaced terrain reaches view", identity, terrainWorld, true);
if (terrainWorld.Min.X > 0.001f || terrainWorld.Max.X < 3.999f)
    throw new InvalidOperationException("Terrain bound does not cover displacement along rotated normal");
Console.WriteLine("PASS rotated terrain displacement bound");

var viewportCullBoundsProperty = typeof(EditorObject).GetProperty("ViewportCullBounds", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Viewport cull bounds not found");
var displacedPrimitive = new EditorObject(EditorPrimitiveType.Box)
{
    PbrVertexDisplaceEnabled = true,
    PbrVertexDisplaceScale = 0.8f,
    PbrVertexOffset = -0.2f,
    Scale = new Vector3(2f, 1f, 1f)
};
var displacedPrimitiveBounds = (AABB)(viewportCullBoundsProperty.GetValue(displacedPrimitive)
    ?? throw new InvalidOperationException("Viewport cull bounds returned null"));
if (displacedPrimitiveBounds.Min.X > -3f || displacedPrimitiveBounds.Max.X < 3f
    || displacedPrimitiveBounds.Min.Y > -2.5f || displacedPrimitiveBounds.Max.Y < 2.5f)
    throw new InvalidOperationException("Displaced Box viewport bounds do not include the scaled displacement envelope");
Console.WriteLine("PASS displaced primitive viewport bounds");

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
var prepareOccluder = occType.GetMethod("Prepare", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Occluder preparation method not found");
var preparedOcclusion = occType.GetMethod("IsOccludedPrepared", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Prepared occlusion method not found");
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
    var prepared = prepareOccluder.Invoke(null, [box, Vector3.Zero, perspective])!;
    bool preparedResult = (bool)preparedOcclusion.Invoke(null, [target, prepared, perspective])!;
    if (preparedResult != result)
        throw new InvalidOperationException($"Prepared occlusion disagrees with the reference test at sample {sample}");
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
