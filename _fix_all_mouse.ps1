param()
$ErrorActionPreference = "Stop"
$path = "Engine/Scene/GameScene.cs"
$arr = [System.Collections.ArrayList](Get-Content $path)
Write-Host "Read " $arr.Count " lines"

# --- Replacement 1: HandleSaveLoadInput ---
$saveStart = -1; $saveMouseEnd = -1
for ($i = 0; $i -lt $arr.Count; $i++) {
    if ($arr[$i] -match 'private void HandleSaveLoadInput\(nint window\)') { $saveStart = $i }
    if ($saveStart -ge 0 -and $arr[$i] -match 'Keyboard navigation') { $saveMouseEnd = $i - 1; break }
}
Write-Host "HandleSaveLoadInput: start=$saveStart mouseEnd=$saveMouseEnd"

if ($saveStart -ge 0 -and $saveMouseEnd -ge 0) {
    $newSaveLoad = @(
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
        '            for (int i = 0; i < _hud.ButtonCount; i++)',
        '                if (_hud.Buttons[i].IsHovered)',
        '                    _saveLoadSelection = i;'
    )
    for ($i = $saveMouseEnd; $i -ge $saveStart; $i--) { $arr.RemoveAt($i) }
    for ($i = 0; $i -lt $newSaveLoad.Count; $i++) { $arr.Insert($saveStart + $i, $newSaveLoad[$i]) }
    Write-Host "  -> OK"
}

# --- Replacement 2: HandleSettingsInput ---
$setStart = -1; $setMouseEnd = -1
for ($i = 0; $i -lt $arr.Count; $i++) {
    if ($arr[$i] -match 'private void HandleSettingsInput\(nint window\)') { $setStart = $i }
    if ($setStart -ge 0 -and $arr[$i] -match 'Keyboard navigation') { $setMouseEnd = $i - 1; break }
}
Write-Host "HandleSettingsInput: start=$setStart mouseEnd=$setMouseEnd"

if ($setStart -ge 0 -and $setMouseEnd -ge 0) {
    $newSettings = @(
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
        '            _hud.ClearButtons();',
        '            for (int i = 0; i < _inGameSettingLabels.Length; i++)',
        '            {',
        '                float ry = startY + i * (rowH + rowGap);',
        '                int captured = i;',
        '                if (i == _inGameSettingLabels.Length - 1)',
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
        '            int hoveredIdx = -1;',
        '            for (int i = 0; i < _hud.ButtonCount; i++)',
        '                if (_hud.Buttons[i].IsHovered)',
        '                    hoveredIdx = i;',
        '            if (hoveredIdx >= 0 && hoveredIdx != _settingsLastHovered)',
        '                _settingsSelection = hoveredIdx;',
        '            _settingsLastHovered = hoveredIdx;'
    )
    for ($i = $setMouseEnd; $i -ge $setStart; $i--) { $arr.RemoveAt($i) }
    for ($i = 0; $i -lt $newSettings.Count; $i++) { $arr.Insert($setStart + $i, $newSettings[$i]) }
    Write-Host "  -> OK"
}

# --- Replacement 3: HandleConfirmInput ---
$confStart = -1; $confMouseEnd = -1
for ($i = 0; $i -lt $arr.Count; $i++) {
    if ($arr[$i] -match 'private void HandleConfirmInput\(nint window\)') { $confStart = $i }
    if ($confStart -ge 0 -and $arr[$i] -match 'bool leftDown') { $confMouseEnd = $i - 1; break }
}
Write-Host "HandleConfirmInput: start=$confStart mouseEnd=$confMouseEnd"

if ($confStart -ge 0 -and $confMouseEnd -ge 0) {
    $newConfirm = @(
        '        private void HandleConfirmInput(nint window)',
        '        {',
        '            int w = Glfw.WindowWidth;',
        '            int h = Glfw.WindowHeight;',
        '',
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
        '            int hoveredIdx = -1;',
        '            for (int i = 0; i < _hud.ButtonCount; i++)',
        '                if (_hud.Buttons[i].IsHovered)',
        '                    hoveredIdx = i;',
        '            if (hoveredIdx >= 0 && hoveredIdx != _confirmLastHovered)',
        '                _confirmSelection = hoveredIdx;',
        '            _confirmLastHovered = hoveredIdx;'
    )
    for ($i = $confMouseEnd; $i -ge $confStart; $i--) { $arr.RemoveAt($i) }
    for ($i = 0; $i -lt $newConfirm.Count; $i++) { $arr.Insert($confStart + $i, $newConfirm[$i]) }
    Write-Host "  -> OK"
}

# --- Replacement 4: Remove remaining refs to removed fields ---
$patterns = @(
    '_saveLoadMouseWasDown = Mouse.IsButtonPressed',
    '_settingsMouseWasDown = Mouse.IsButtonPressed',
    '_confirmMouseWasDown = Mouse.IsButtonPressed'
)
foreach ($pat in $patterns) {
    for ($i = 0; $i -lt $arr.Count; $i++) {
        if ($arr[$i] -match $pat) {
            $arr.RemoveAt($i)
            Write-Host "Removed line with $pat"
            break
        }
    }
}

$arr | Set-Content $path
Write-Host "Done! " $arr.Count " lines written"
