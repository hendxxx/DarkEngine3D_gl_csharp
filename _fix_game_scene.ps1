param()
$ErrorActionPreference = "Stop"
$filePath = "Engine/Scene/GameScene.cs"
$content = Get-Content $filePath -Raw

# Fix 1: Move string title8 = ""; outside the if block, next to string title7 = "";
$target1 = "`$`"string title8 = `$`" BVH: rays={bvhRays} nodes={bvhNodes} aabb={bvhAABBs} tris={bvhTris}  OC: checkVis={ocCheckVisMs:N3}ms isOcc={ocIsOccMs:N3}ms`";`r`n                string ocMode = Inputs.Keyboard.GetOcclusionModeName();"
$replacement1 = "`$`"string title8 = `$`" BVH: rays={bvhRays} nodes={bvhNodes} aabb={bvhAABBs} tris={bvhTris}  OC: checkVis={ocCheckVisMs:N3}ms isOcc={ocIsOccMs:N3}ms`";`r`n            string ocMode = Inputs.Keyboard.GetOcclusionModeName();"
$content = $content.Replace($target1, $replacement1)

# Wait, this won't work well with all the escaping. Let me use a different approach.
# Just directly Read/Write lines
