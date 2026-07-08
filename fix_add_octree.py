import re

# Read StaticObjectManager.cs
with open('Engine/Objects/StaticObjectManager.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Add Octree fields after UseTerrainGrid
old_field = "public bool UseTerrainGrid = false;"
new_field = old_field + """

        // -- Octree spatial partitioning for hierarchical culling --
        /// <summary>Octree for hierarchical frustum + occlusion culling. Built after all objects added.</summary>
        public Octree? SpatialOctree { get; private set; }
        public bool UseOctree = false;
"""
content = content.replace(old_field, new_field, 1)

# 2. Add BuildOctree method - insert before the first method after SpatialGrid section
# Find BuildSpatialGrid method and add BuildOctree right after it
build_spatial_grid = """public void BuildSpatialGrid()"""
build_octree_method = """
        /// <summary>
        /// Build Octree from object world AABBs for hierarchical frustum + occlusion culling.
        /// Leaf size ~16m (minLeafSize parameter). Must be called after all objects added.
        /// </summary>
        public void BuildOctree()
        {
            if (!UseOctree || _objects.Count == 0) return;

            // Compute world bounds from all objects' world AABBs
            var boundsList = new List<(AABB bounds, int index)>(_objects.Count);
            Vector3 worldMin = new(float.MaxValue), worldMax = new(float.MinValue);

            for (int i = 0; i < _objects.Count; i++)
            {
                var aabb = _objects[i].CachedWorldAABB;
                boundsList.Add((aabb, i));
                worldMin = Vector3.Min(worldMin, aabb.Min);
                worldMax = Vector3.Max(worldMax, aabb.Max);
            }

            var worldBounds = new AABB(worldMin, worldMax);
            SpatialOctree = new Octree();
            SpatialOctree.Build(boundsList, worldBounds, 16f);
            Console.WriteLine($"[Octree] Built for {_objects.Count} objects (bounds: {worldMin.X:F1},{worldMin.Z:F1} -> {worldMax.X:F1},{worldMax.Z:F1})");
        }
"""

content = content.replace(build_spatial_grid, build_octree_method + "\n" + build_spatial_grid, 1)

with open('Engine/Objects/StaticObjectManager.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print("StaticObjectManager.cs updated successfully!")
