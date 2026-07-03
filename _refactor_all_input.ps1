param()

$ErrorActionPreference = "Stop"
$path = "Engine/Scene/GameScene.cs"
$lines = Get-Content $path
$count = $lines.Count

Write-Host "Read $count lines from $path"

# ============================================================
# 1. Remove unused field declarations
# ============================================================
# Line 104 (idx 103): private bool _saveLoadMouseWasDown = false;
# Line 118 (idx 117): private bool _confirmMouseWasDown = false;
# Line 131 (idx 130): private bool _settingsMouseWasDown = false;

$removeLines = @{}

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s+private bool _saveLoadMouseWasDown\s*=\s*false;') {
        $removeLines[$i] = $true
        Write-Host "Will remove field: $($lines[$i].Trim()) (line $i)"
    }
    if ($lines[$i] -match '^\s+private bool _confirmMouseWasDown\s*=\s*false;') {
        $removeLines[$i] = $true
        Write-Host "Will remove field: $($lines[$i].Trim()) (line $i)"
    }
    if ($lines[$i] -match '^\s+private bool _settingsMouseWasDown\s*=\s*false;') {
        $removeLines[$i] = $true
        Write-Host "Will remove field: $($lines[$i].Trim()) (line $i)"
    }
}

# Also remove _saveLoadMouseWasDown = false; in Enter() if it exists
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s+_saveLoadMouseWasDown\s*=\s*false;') {
        $removeLines[$i] = $true
        Write-Host "Will remove: $($lines[$i].Trim()) (line $i)"
    }
}

# ============================================================
# 2. Replace HandleConfirmInput - replace mouse section
# ============================================================
# Find the method
$confirmStart = -1
$confirmMouseEnd = -1  # end of mouse section (before keyboard)
$confirmMethodEnd = -1

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'private void HandleConfirmInput\(nint window\)') {
        $confirmStart = $i
    }
    if ($confirmStart -ge 0 -and $i -gt $confirmStart) {
        # Find the line that starts the keyboard section: "// Keyboard"
        if ($lines[$i] -match '^\s+// ── Keyboard ──' -or $lines[$i] -match '^\s+// Keyboard') {
            $confirmMouseEnd = $i - 1  # line before keyboard comment
        }
        # Find end of method
        if ($lines[$i] -match '^\s{8}\}' -and $i -gt $confirmStart) {
            $confirmMethodEnd = $i
            break
        }
    }
}

if ($confirmStart -ge 0 -and $confirmMouseEnd -ge 0) {
    Write-Host "HandleConfirmInput: start=$confirmStart, mouseEnd=$confirmMouseEnd, methodEnd=$confirmMethodEnd"
    
    $newConfirmInput = @(
        '        private void HandleConfirmInput(nint window)',
        '        {',
        '            int w = Glfw.WindowWidth;',
        '            int h = Glfw.WindowHeight;',
        '',
        '            // Register buttons for hover/click detection via HUD system',
        '            _hud.ClearButtons();',
        '            for (int i = 0; i < 2; i++)',
        '            {',
        '                ConfirmDialog.GetButtonRect(w, h, _confirmDlgScale, i,',
        '                    out float bx, out float btnY, out float btnW, out float btnH);',
        '                int captured = i;',
        '                _hud.AddButton("", bx, btnY, btnW, btnH, () => ExecuteConfirmAction(captured));',
        '            }',
        '            _hud.UpdateButtons();',
        '',
        '            // Sync selection from hover (only when hover changes)',
        '            int hoveredIdx = -1;',
        '            for (int i = 0; i < _hud.ButtonCount; i++)',
        '                if (_hud.Buttons[i].IsHovered)',
        '                    hoveredIdx = i;',
        '            if (hoveredIdx >= 0 && hoveredIdx != _confirmLastHovered)',
        '                _confirmSelection = hoveredIdx;',
        '            _confirmLastHovered = hoveredIdx;'
    )
    
    # Remove old lines from confirmStart to confirmMouseEnd (inclusive)
    $removeCount = $confirmMouseEnd - $confirmStart + 1
    for ($i = 0; $i -lt $removeCount; $i++) {
        $lines.RemoveAt($confirmStart)
    }
    
    # Insert new lines
    for ($i = 0; $i -lt $newConfirmInput.Count; $i++) {
        $lines.Insert($confirmStart + $i, $newConfirmInput[$i])
    }
    
    Write-Host "HandleConfirmInput replaced successfully"
} else {
    Write-Host "ERROR: HandleConfirmInput not found! start=$confirmStart, mouseEnd=$confirmMouseEnd"
}

# Recompute count after modifications
$count = $lines.Count

# ============================================================
# 3. Replace HandleSettingsInput - replace mouse section
# ============================================================
$settingsStart = -1
$settingsMouseEnd = -1
$settingsMethodEnd = -1

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'private void HandleSettingsInput\(nint window\)') {
        $settingsStart = $i
    }
    if ($settingsStart -ge 0 -and $i -gt $settingsStart) {
        # Find the line that starts the keyboard section
        if ($lines[$i] -match '^\s+// ── Keyboard navigation ──') {
            $settingsMouseEnd = $i - 1
        }
        if ($lines[$i] -match '^\s{8}\}' -and $i -gt $settingsStart) {
            $settingsMethodEnd = $i
            break
        }
    }
}

if ($settingsStart -ge 0 -and $settingsMouseEnd -ge 0) {
    Write-Host "HandleSettingsInput: start=$settingsStart, mouseEnd=$settingsMouseEnd, methodEnd=$settingsMethodEnd"
    
    $newSettingsInput = @(
        '        private void HandleSettingsInput(nint window)',
        '        {',
        '            int w = Glfw.WindowWidth;',
        '            int h = Glfw.WindowHeight;',
        '',
        '            float panelX = w * 0.25f, panelW = w * 0.5f;',
        '            float rowH = 42f, rowGap = 8f;',
        '            float titleY = h * 0.28f;',
        '            float startY = titleY + 70f;',
        '',
        '            // Register interactive rows via HUD system',
        '            _hud.ClearButtons();',
        '            for (int i = 0; i < _inGameSettingLabels.Length; i++)',
        '            {',
        '                float ry = startY + i * (rowH + rowGap);',
        '                int captured = i;',
        '                if (i == _inGameSettingLabels.Length - 1) // BACK button',
        '                {',
        '                    _hud.AddButton("", panelX, ry, panelW, rowH,',
        '                        () => ApplyAndExitInGameSettings());',
        '                }',
        '                else',
        '                {',
        '                    _hud.AddButton("", panelX, ry, panelW, rowH,',
        '                        () => CycleInGameSetting(captured, 1));',
        '                }',
        '            }',
        '            _hud.UpdateButtons();',
        '',
        '            // Sync selection from hover (only when hover changes)',
        '            int hoveredIdx = -1;',
        '            for (int i = 0; i < _hud.ButtonCount; i++)',
        '                if (_hud.Buttons[i].IsHovered)',
        '                    hoveredIdx = i;',
        '            if (hoveredIdx >= 0 && hoveredIdx != _settingsLastHovered)',
        '                _settingsSelection = hoveredIdx;',
        '            _settingsLastHovered = hoveredIdx;'
    )
    
    # Remove old lines from settingsStart to settingsMouseEnd (inclusive)
    $removeCount = $settingsMouseEnd - $settingsStart + 1
    for ($i = 0; $i -lt $removeCount; $i++) {
        $lines.RemoveAt($settingsStart)
    }
    
    # Insert new lines
    for ($i = 0; $i -lt $newSettingsInput.Count; $i++) {
        $lines.Insert($settingsStart + $i, $newSettingsInput[$i])
    }
    
    Write-Host "HandleSettingsInput replaced successfully"
} else {
    Write-Host "ERROR: HandleSettingsInput not found! start=$settingsStart, mouseEnd=$settingsMouseEnd"
}

# ============================================================
# 4. Replace HandleSaveLoadInput - replace mouse section
# ============================================================
$saveLoadStart = -1
$saveLoadMouseEnd = -1
$saveLoadMethodEnd = -1

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'private void HandleSaveLoadInput\(nint window\)') {
        $saveLoadStart = $i
    }
    if ($saveLoadStart -ge 0 -and $i -gt $saveLoadStart) {
        # Find the line that starts the keyboard section
        if ($lines[$i] -match '^\s+// ── Keyboard navigation ──') {
            $saveLoadMouseEnd = $i - 1
        }
        if ($lines[$i] -match '^\s{8}\}' -and $i -gt $saveLoadStart) {
            $saveLoadMethodEnd = $i
            break
        }
    }
}

if ($saveLoadStart -ge 0 -and $saveLoadMouseEnd -ge 0) {
    Write-Host "HandleSaveLoadInput: start=$saveLoadStart, mouseEnd=$saveLoadMouseEnd, methodEnd=$saveLoadMethodEnd"
    
    $newSaveLoadInput = @(
        '        private void HandleSaveLoadInput(nint window)',
        '        {',
        '            int w = Glfw.WindowWidth;',
        '            int h = Glfw.WindowHeight;',
        '',
        '            SaveSlotUI.GetPanelRect(w, h, out float panelX, out float panelW,',
        '                out float panelY, out float panelH, out float startY);',
        '',
        '            float bx = panelX + GridLayout.Gutter * 0.5f;',
        '            float bw = panelW - GridLayout.Gutter;',
        '',
        '            // Register save slot rows for hover/click detection via HUD system',
        '            _hud.ClearButtons();',
        '            for (int i = 0; i < SaveManager.NumSlots; i++)',
        '            {',
        '                float sy = startY + i * (SaveSlotUI.SlotRowH + SaveSlotUI.SlotGap);',
        '                int captured = i;',
        '                _hud.AddButton("", bx, sy, bw, SaveSlotUI.SlotRowH, () =>',
        '                {',
        '                    if (_isSaveMode)',
        '                        SaveGameToSlot(captured);',
        '                    else if (_saveSlots[captured].HasData)',
        '                        LoadGameFromSlot(captured);',
        '                });',
        '            }',
        '            _hud.UpdateButtons();',
        '',
        '            // Sync selection from hover',
        '            for (int i = 0; i < _hud.ButtonCount; i++)',
        '                if (_hud.Buttons[i].IsHovered)',
        '                    _saveLoadSelection = i;'
    )
    
    # Remove old lines from saveLoadStart to saveLoadMouseEnd (inclusive)
    $removeCount = $saveLoadMouseEnd - $saveLoadStart + 1
    for ($i = 0; $i -lt $removeCount; $i++) {
        $lines.RemoveAt($saveLoadStart)
    }
    
    # Insert new lines
    for ($i = 0; $i -lt $newSaveLoadInput.Count; $i++) {
        $lines.Insert($saveLoadStart + $i, $newSaveLoadInput[$i])
    }
    
    Write-Host "HandleSaveLoadInput replaced successfully"
} else {
    Write-Host "ERROR: HandleSaveLoadInput not found! start=$saveLoadStart, mouseEnd=$saveLoadMouseEnd"
}

# ============================================================
# 5. Remove all remaining references to unused fields
# ============================================================
# Now that we've modified the methods, remove the field declarations
# and any remaining references

$finalLines = New-Object System.Collections.ArrayList

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    
    # Skip removed field declarations
    if ($removeLines.ContainsKey($i)) {
        continue
    }
    
    # Skip lines that reference removed fields (mouseWasDown assignments)
    # These are in the old mouse sections, but some might be scattered
    if ($line -match '^\s+_confirmMouseWasDown\s*=\s*(true|false);') {
        continue
    }
    if ($line -match '^\s+if \(!mousePressed\) _confirmMouseWasDown = false;') {
        continue
    }
    if ($line -match '^\s+_settingsMouseWasDown\s*=\s*(true|false);') {
        # Check context: this could be in ApplyAndExit... but let's skip only the mouse sections
        # The remaining references are at lines after the keyboard sections - check context
        continue
    }
    if ($line -match '^\s+if \(mousePressed && !_settingsMouseWasDown\) _settingsMouseWasDown = true;') {
        continue
    }
    
    [void]$finalLines.Add($line)
}

# ============================================================
# 6. Write back
# ============================================================
$finalLines | Set-Content $path
Write-Host "Done! Wrote back to $path ($($finalLines.Count) lines)"
