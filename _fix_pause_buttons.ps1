$path = "C:\Users\hend_\source\repos\DarkEngine3D_gl_csharp\Engine\Scene\GameScene.cs"
$content = [System.IO.File]::ReadAllText($path)

# Replace HandlePauseInput - replace manual mouse handling with HUD buttons
$old = @"
        private void HandlePauseInput(nint window)
        {
            Mouse.GetCursorPosition(out double mouseX, out double mouseY);
            bool mousePressed = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);

            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;
            var grid = new GridLayout(w, h);
            float pauseBtnW = grid.SpanW(PauseBtnColStart, PauseBtnColEnd);
            float titleY = h * 0.28f;
            float startY = titleY + 70f;

            // ── Mouse hover detection ── only update when hovering a different button ──
            int hoveredIndex = -1;
            float pauseBx = (w - pauseBtnW) * 0.5f;
            for (int i = 0; i < PauseItemCount; i++)
            {
                float by = startY + i * (PauseBtnH + PauseBtnSpacing);
                if (mouseX >= pauseBx && mouseX <= pauseBx + pauseBtnW &&
                    mouseY >= by && mouseY <= by + PauseBtnH)
                {
                    hoveredIndex = i;
                    break;
                }
            }
            if (hoveredIndex >= 0 && hoveredIndex != _pauseLastHovered)
                _pauseSelection = hoveredIndex;
            _pauseLastHovered = hoveredIndex;

            // ── Mouse click ──
            if (mousePressed && !_pauseMouseWasDown)
            {
                _pauseMouseWasDown = true;
                if (hoveredIndex >= 0)
                    ExecutePauseAction(hoveredIndex);
            }
            if (!mousePressed)
                _pauseMouseWasDown = false;

            // ── Keyboard navigation ──
            bool upDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_W);
            bool downDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);
            bool enterDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE);

            if (upDown && !_pauseUpWasDown)
                _pauseSelection = (_pauseSelection - 1 + PauseItemCount) % PauseItemCount;
            if (downDown && !_pauseDownWasDown)
                _pauseSelection = (_pauseSelection + 1) % PauseItemCount;
            if (enterDown && !_pauseEnterWasDown)
                ExecutePauseAction(_pauseSelection);

            _pauseUpWasDown = upDown;
            _pauseDownWasDown = downDown;
            _pauseEnterWasDown = enterDown;
        }
"@

$new = @"
        private void HandlePauseInput(nint window)
        {
            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;
            var grid = new GridLayout(w, h);
            float pauseBtnW = grid.SpanW(PauseBtnColStart, PauseBtnColEnd);
            float titleY = h * 0.28f;
            float startY = titleY + 70f;

            // ── HUD Button System for pause menu ──
            _hud.ClearButtons();
            float pauseBx = (w - pauseBtnW) * 0.5f;
            for (int i = 0; i < PauseItemCount; i++)
            {
                int captured = i;
                float by = startY + i * (PauseBtnH + PauseBtnSpacing);
                _hud.AddButton("", pauseBx, by, pauseBtnW, PauseBtnH,
                    () => ExecutePauseAction(captured));
            }
            _hud.UpdateButtons();
            // Sync keyboard selection from hover
            for (int i = 0; i < _hud.ButtonCount; i++)
                if (_hud.Buttons[i].IsHovered)
                    _pauseSelection = i;

            // ── Keyboard navigation ──
            bool upDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_W);
            bool downDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);
            bool enterDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE);

            if (upDown && !_pauseUpWasDown)
                _pauseSelection = (_pauseSelection - 1 + PauseItemCount) % PauseItemCount;
            if (downDown && !_pauseDownWasDown)
                _pauseSelection = (_pauseSelection + 1) % PauseItemCount;
            if (enterDown && !_pauseEnterWasDown)
                ExecutePauseAction(_pauseSelection);

            _pauseUpWasDown = upDown;
            _pauseDownWasDown = downDown;
            _pauseEnterWasDown = enterDown;
        }
"@

$content = $content.Replace($old, $new)

# Replace RenderPauseMenu - replace manual button rendering with HUD.DrawButtons
$old2 = @"
        private void RenderPauseMenu()
        {
            if (_hud == null) return;

            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;

            // Dark overlay
            _hud.DrawBox(0, 0, w, h, new Vector3(0f, 0f, 0f) * 0.45f);

            // Title — centered using grid
            var pgGrid = new GridLayout(w, h);
            float pauseCenterX = pgGrid.CenterX(2, 10);
            string title = "PAUSED";
            var titleExtents = _hud.GetTextExtents(title);
            float titleX = pauseCenterX - titleExtents.Width * 0.5f;
            float titleY = h * 0.28f;
            _hud.DrawText(title, titleX, titleY, new Vector3(0.9f, 0.9f, 1.0f));

            // Decorative line — positioned below title using text height, centered in grid
            float lineW = 120f;
            float lineX = pauseCenterX - lineW * 0.5f;
            _hud.DrawBox(lineX, titleY + titleExtents.Height + 14f, lineW, 1f, new Vector3(0.4f, 0.5f, 0.9f) * 0.6f);

            // Buttons (responsive grid width)
            string worldStatus = Config.GameplayConfig.PauseOnEsc ? "RUNNING" : "PAUSED";
            string[] pauseItems = ["RESUME", "SAVE GAME", "LOAD GAME", "SETTINGS", $"World: [{worldStatus}]", "BACK TO MAIN MENU"];
            var pGrid = new GridLayout(w, h);
            float pauseBtnW = pGrid.SpanW(PauseBtnColStart, PauseBtnColEnd);
            float btnH = 50f;
            float btnSpacing = 14f;
            float startY = titleY + titleExtents.Height + 40f;

            for (int i = 0; i < pauseItems.Length; i++)
            {
                float bx = (w - pauseBtnW) * 0.5f;
                float by = startY + i * (btnH + btnSpacing);
                bool isSelected = (i == _pauseSelection);

                // Selected glow
                if (isSelected)
                {
                    float glowPulse = 0.5f + 0.5f * MathF.Sin(_time * 3f);
                    float glowAlpha = 0.07f + glowPulse * 0.05f;
                    _hud.DrawBox(bx - 8f, by - 6f, pauseBtnW + 16f, btnH + 12f,
                        new Vector3(0.3f, 0.4f, 0.9f) * glowAlpha);
                }

                // Background
                _hud.DrawBox(bx, by, pauseBtnW, btnH,
                    isSelected ? new Vector3(0.22f, 0.28f, 0.45f) : new Vector3(0.10f, 0.12f, 0.18f));

                // Borders
                Vector3 border = isSelected
                    ? new Vector3(0.5f, 0.6f, 1.0f)
                    : new Vector3(0.15f, 0.18f, 0.25f);
                _hud.DrawBox(bx, by, pauseBtnW, 1f, border);
                _hud.DrawBox(bx, by + btnH - 1f, pauseBtnW, 1f, border);

                // Selected side bar
                if (isSelected)
                {
                    _hud.DrawBox(bx - 3f, by + 4f, 3f, btnH - 8f, new Vector3(0.4f, 0.5f, 0.9f));
                }
"@

$new2 = @"
        private void RenderPauseMenu()
        {
            if (_hud == null) return;

            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;

            // Dark overlay
            _hud.DrawBox(0, 0, w, h, new Vector3(0f, 0f, 0f) * 0.45f);

            // Title — centered using grid
            var pgGrid = new GridLayout(w, h);
            float pauseCenterX = pgGrid.CenterX(2, 10);
            string title = "PAUSED";
            var titleExtents = _hud.GetTextExtents(title);
            float titleX = pauseCenterX - titleExtents.Width * 0.5f;
            float titleY = h * 0.28f;
            _hud.DrawText(title, titleX, titleY, new Vector3(0.9f, 0.9f, 1.0f));

            // Decorative line — positioned below title using text height, centered in grid
            float lineW = 120f;
            float lineX = pauseCenterX - lineW * 0.5f;
            _hud.DrawBox(lineX, titleY + titleExtents.Height + 14f, lineW, 1f, new Vector3(0.4f, 0.5f, 0.9f) * 0.6f);

            // Buttons using HUD.DrawButtons with custom palette
            _hud.DrawButtons(_time, _pauseSelection);
"@

$content = $content.Replace($old2, $new2)

[System.IO.File]::WriteAllText($path, $content)
Write-Host "Done! GameScene.cs updated."
