$path = "Engine/Scene/GameScene.cs"
$lines = Get-Content $path

# --- Line numbers (0-based index) ---
# Line 82 (idx 81): remove _pauseLastHovered field decl
# Line 89 (idx 88): remove _pauseMouseWasDown field decl
# Line 261 (idx 260): remove _pauseLastHovered = -1 from Enter()

# --- 1. Remove unused field declarations ---
# Line 82: private int _pauseLastHovered ...
# Line 89: private bool _pauseMouseWasDown ...
$newLines = New-Object System.Collections.ArrayList
$skipIndices = @(81, 88, 260)  # 0-based

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($skipIndices -contains $i) { continue }
    [void]$newLines.Add($lines[$i])
}

# --- 2. Replace HandlePauseInput ---
# Find the method start and end
$oldMethodStart = $null
$oldMethodEnd = $null
for ($i = 0; $i -lt $newLines.Count; $i++) {
    if ($newLines[$i] -match 'private void HandlePauseInput\(nint window\)') {
        $oldMethodStart = $i
    }
    if ($oldMethodStart -ne $null -and $newLines[$i] -match '^\s{8}\}' -and $i -gt $oldMethodStart) {
        $oldMethodEnd = $i
        break
    }
}

if ($oldMethodStart -ne $null -and $oldMethodEnd -ne $null) {
    $newMethod = @(
        '        private void HandlePauseInput(nint window)',
        '        {',
        '            int w = Glfw.WindowWidth;',
        '            int h = Glfw.WindowHeight;',
        '            var grid = new GridLayout(w, h);',
        '            float pauseBtnW = grid.SpanW(PauseBtnColStart, PauseBtnColEnd);',
        '            float titleY = h * 0.28f;',
        '            float startY = titleY + 70f;',
        '            string worldStatus = Config.GameplayConfig.PauseOnEsc ? "RUNNING" : "PAUSED";',
        '            string[] pauseItems = ["RESUME", "SAVE GAME", "LOAD GAME", "SETTINGS", $"World: [{worldStatus}]", "BACK TO MAIN MENU"];',
        '',
        '            float pauseBx = (w - pauseBtnW) * 0.5f;',
        '            _hud.ClearButtons();',
        '            for (int i = 0; i < PauseItemCount; i++)',
        '            {',
        '                float by = startY + i * (PauseBtnH + PauseBtnSpacing);',
        '                int captured = i;',
        '                _hud.AddButton(pauseItems[i], pauseBx, by, pauseBtnW, PauseBtnH,',
        '                    () => ExecutePauseAction(captured));',
        '            }',
        '            _hud.UpdateButtons();',
        '',
        '            // Sync keyboard selection from hover',
        '            for (int i = 0; i < _hud.ButtonCount; i++)',
        '                if (_hud.Buttons[i].IsHovered)',
        '                    _pauseSelection = i;',
        '',
        '            // Keyboard navigation',
        '            bool upDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_W);',
        '            bool downDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);',
        '            bool enterDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE);',
        '',
        '            if (upDown && !_pauseUpWasDown)',
        '                _pauseSelection = (_pauseSelection - 1 + PauseItemCount) % PauseItemCount;',
        '            if (downDown && !_pauseDownWasDown)',
        '                _pauseSelection = (_pauseSelection + 1) % PauseItemCount;',
        '            if (enterDown && !_pauseEnterWasDown)',
        '                ExecutePauseAction(_pauseSelection);',
        '',
        '            _pauseUpWasDown = upDown;',
        '            _pauseDownWasDown = downDown;',
        '            _pauseEnterWasDown = enterDown;',
        '        }'
    )
    
    # Remove old method lines
    for ($i = $oldMethodEnd; $i -ge $oldMethodStart; $i--) {
        $newLines.RemoveAt($i)
    }
    
    # Insert new method at old start position
    for ($i = 0; $i -lt $newMethod.Count; $i++) {
        $newLines.Insert($oldMethodStart + $i, $newMethod[$i])
    }
    
    Write-Host "HandlePauseInput replaced successfully"
} else {
    Write-Host "ERROR: HandlePauseInput not found!"
    exit 1
}

# --- 3. Replace RenderPauseMenu ---
$oldRenderStart = $null
$oldRenderEnd = $null
for ($i = 0; $i -lt $newLines.Count; $i++) {
    if ($newLines[$i] -match 'private void RenderPauseMenu\(\)') {
        $oldRenderStart = $i
    }
    if ($oldRenderStart -ne $null -and $newLines[$i] -match '^\s{8}\}' -and $i -gt $oldRenderStart) {
        $oldRenderEnd = $i
        break
    }
}

if ($oldRenderStart -ne $null -and $oldRenderEnd -ne $null) {
    $newRender = @(
        '        private void RenderPauseMenu()',
        '        {',
        '            if (_hud == null) return;',
        '',
        '            int w = Glfw.WindowWidth;',
        '            int h = Glfw.WindowHeight;',
        '',
        '            // Dark overlay',
        '            _hud.DrawBox(0, 0, w, h, new Vector3(0f, 0f, 0f) * 0.45f);',
        '',
        '            // Title — centered using grid',
        '            var pgGrid = new GridLayout(w, h);',
        '            float pauseCenterX = pgGrid.CenterX(2, 10);',
        '            string title = "PAUSED";',
        '            var titleExtents = _hud.GetTextExtents(title);',
        '            float titleX = pauseCenterX - titleExtents.Width * 0.5f;',
        '            float titleY = h * 0.28f;',
        '            _hud.DrawText(title, titleX, titleY, new Vector3(0.9f, 0.9f, 1.0f));',
        '',
        '            // Decorative line — centered in grid',
        '            float lineW = 120f;',
        '            float lineX = pauseCenterX - lineW * 0.5f;',
        '            _hud.DrawBox(lineX, titleY + titleExtents.Height + 14f, lineW, 1f, new Vector3(0.4f, 0.5f, 0.9f) * 0.6f);',
        '',
        '            // Buttons via HUD system',
        '            _hud.DrawButtons(_time, _pauseSelection);',
        '        }'
    )
    
    # Remove old render method lines
    for ($i = $oldRenderEnd; $i -ge $oldRenderStart; $i--) {
        $newLines.RemoveAt($i)
    }
    
    # Insert new render method at old start position
    for ($i = 0; $i -lt $newRender.Count; $i++) {
        $newLines.Insert($oldRenderStart + $i, $newRender[$i])
    }
    
    Write-Host "RenderPauseMenu replaced successfully"
} else {
    Write-Host "ERROR: RenderPauseMenu not found!"
    exit 1
}

# --- Write back ---
$newLines | Set-Content $path
Write-Host "Done! Wrote back to $path"
