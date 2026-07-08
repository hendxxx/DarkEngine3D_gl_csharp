with open('Engine/Scene/GameScene.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Insert frustum debug code before "//  Record total render time"
marker = "            //  Record total render time (from the dedicated total timer, not the section timer)"

code_to_add = """            //  Frustum Frozen Debug Visualization (toggled with P key — freezes at capture)
            var _frozenCorners = Keyboard.GetFrozenCorners();
            if (_frozenCorners != null && _camera != null)
            {
                GL.Disable(Const.GL_DEPTH_TEST);

                // 12 edges of the frozen frustum -> 24 vertices (GL_LINES)
                var _verts = new List<Vector3>(24);
                // Near plane (indices 0,1,3,2)
                _verts.Add(_frozenCorners[0]); _verts.Add(_frozenCorners[1]);
                _verts.Add(_frozenCorners[1]); _verts.Add(_frozenCorners[3]);
                _verts.Add(_frozenCorners[3]); _verts.Add(_frozenCorners[2]);
                _verts.Add(_frozenCorners[2]); _verts.Add(_frozenCorners[0]);
                // Far plane (indices 4,5,7,6)
                _verts.Add(_frozenCorners[4]); _verts.Add(_frozenCorners[5]);
                _verts.Add(_frozenCorners[5]); _verts.Add(_frozenCorners[7]);
                _verts.Add(_frozenCorners[7]); _verts.Add(_frozenCorners[6]);
                _verts.Add(_frozenCorners[6]); _verts.Add(_frozenCorners[4]);
                // Connecting lines: near->far
                _verts.Add(_frozenCorners[0]); _verts.Add(_frozenCorners[4]);
                _verts.Add(_frozenCorners[1]); _verts.Add(_frozenCorners[5]);
                _verts.Add(_frozenCorners[2]); _verts.Add(_frozenCorners[6]);
                _verts.Add(_frozenCorners[3]); _verts.Add(_frozenCorners[7]);

                // Draw in cyan
                TerrainChunk.DrawLineSegments(_verts, new Vector3(0f, 1f, 1f), _camera);
                GL.Enable(Const.GL_DEPTH_TEST);
            }

"""

if marker not in content:
    print("ERROR: Marker not found!")
    exit(1)

content = content.replace(marker, code_to_add + marker, 1)

# Verify braces
open_b = content.count('{')
close_b = content.count('}')
print(f"Braces: {open_b} open, {close_b} close, balanced={open_b == close_b}")

if open_b == close_b:
    with open('Engine/Scene/GameScene.cs', 'w', encoding='utf-8') as f:
        f.write(content)
    print("File written!")
else:
    print("ERROR: Unbalanced braces!")
