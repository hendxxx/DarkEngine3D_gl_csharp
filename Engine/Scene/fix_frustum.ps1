$path = "Engine/Scene/GameScene.cs"
$content = [System.IO.File]::ReadAllText($path)

# Try with just \n (LF) line endings
$old1 = "                if (_objectManager.staticObjectManagers != null)\n                {\n                // 1B. Register static objects as occluders\n                    // Extract frustum planes for frustum culling\n                    var frustumVP = _camera.GetViewMatrix() * _camera.GetProjectionMatrix();\n                    var frustumPlanes = StaticObjectManager.ExtractCameraFrustum(frustumVP);"

$new1 = "                // 1B. Register static objects as occluders\n                var frustumVP = _camera.GetViewMatrix() * _camera.GetProjectionMatrix();\n                var frustumPlanes = StaticObjectManager.ExtractCameraFrustum(frustumVP);\n\n                if (_objectManager.staticObjectManagers != null)\n                {"

if ($content.Contains($old1)) {
    $content = $content.Replace($old1, $new1)
    [System.IO.File]::WriteAllText($path, $content)
    Write-Host "SUCCESS: Frustum declarations moved!"
} else {
    Write-Host "ERROR: Pattern not found in LF mode"
    # Also try CRLF
    $old2 = $old1.Replace("`n", "`r`n")
    $new2 = $new1.Replace("`n", "`r`n")
    if ($content.Contains($old2)) {
        $content = $content.Replace($old2, $new2)
        [System.IO.File]::WriteAllText($path, $content)
        Write-Host "SUCCESS: Frustum declarations moved (CRLF)!"
    } else {
        Write-Host "ERROR: Pattern not found in either LF or CRLF mode"
        # Debug: grab 200 chars around "staticObjectManagers"
        $idx = $content.IndexOf("staticObjectManagers")
        if ($idx -ge 0) {
            $chunk = $content.Substring([Math]::Max(0, $idx - 80), [Math]::Min(400, $content.Length - $idx + 80))
            Write-Host "---CONTEXT---"
            Write-Host $chunk
            Write-Host "---END---"
        }
    }
}
