using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>
    /// Static utility for rendering consistent confirm dialogs across all scenes.
    /// Produces a centered dialog box with red accent bars, a title,
    /// a message, and two interactive buttons — matching the reference style
    /// first established in the Settings ESC confirm dialog.
    /// </summary>
    public static class ConfirmDialog
    {
        /// <summary>
        /// Base dialog size for scale = 1.0.
        /// Public so hit-testing code can reference without duplicating literals.
        /// </summary>
        public const float BaseDlgW = 380f;
        public const float BaseDlgH = 160f;

        /// <summary>Base button dimensions / gap / offset for scale = 1.0.</summary>
        public const float BaseBtnW = 150f;
        public const float BaseBtnH = 40f;
        public const float BaseBtnGap = 20f;
        /// <summary>Y offset of the button row from the dialog top, at scale = 1.0.</summary>
        public const float BaseBtnOffsetY = 94f;

        /// <summary>
        /// Draw a confirm dialog overlay with the standard visual style.
        /// Call this from a scene's Render() method when a confirm box is active.
        /// </summary>
        /// <param name="hud">The HUD instance used for drawing.</param>
        /// <param name="w">Screen width in pixels.</param>
        /// <param name="h">Screen height in pixels.</param>
        /// <param name="title">Dialog title text (e.g. "Unsaved Changes").</param>
        /// <param name="message">Dialog body text below the title.</param>
        /// <param name="btnLabels">Two button labels, index 0 = left, 1 = right.</param>
        /// <param name="btnBgColors">Two background colours used when each button is selected.</param>
        /// <param name="btnSideColors">Two side-bar accent colours used when each button is selected.</param>
        /// <param name="btnTextColors">Two text colours used when each button is selected.</param>
        /// <param name="selection">Currently selected button index (0 or 1).</param>
        /// <param name="scale">Size multiplier — pass &gt; 1.0 for a more prominent dialog (e.g. 1.4 for in-game overlays).</param>
        public static void DrawBox(HUD hud, int w, int h, string title, string message,
            string[] btnLabels, Vector3[] btnBgColors, Vector3[] btnSideColors,
            Vector3[] btnTextColors, int selection, float scale = 1.0f)
        {
            // Full-screen dark overlay
            hud.DrawBox(0, 0, w, h, new Vector3(0f, 0f, 0f) * 0.55f);

            // Dialog background & accent bars — scaled
            float dlgW = BaseDlgW * scale, dlgH = BaseDlgH * scale;
            float dlgX = (w - dlgW) * 0.5f, dlgY = (h - dlgH) * 0.5f;

            hud.DrawBox(dlgX, dlgY, dlgW, dlgH, new Vector3(0.15f, 0.16f, 0.22f));
            hud.DrawBox(dlgX, dlgY, dlgW, 3f * scale, new Vector3(1.0f, 0.3f, 0.2f));
            hud.DrawBox(dlgX, dlgY + dlgH - 3f * scale, dlgW, 3f * scale, new Vector3(1.0f, 0.3f, 0.2f) * 0.6f);

            // Title
            var titleExt = hud.GetTextExtents(title);
            hud.DrawText(title, dlgX + (dlgW - titleExt.Width) * 0.5f, dlgY + 35f * scale,
                new Vector3(1.0f, 1.0f, 1.0f));

            // Message
            var msgExt = hud.GetTextExtents(message);
            hud.DrawText(message, dlgX + (dlgW - msgExt.Width) * 0.5f, dlgY + 60f * scale,
                new Vector3(0.85f, 0.85f, 0.9f));

            // Buttons — scaled (uses public constants so hit-testing can stay in sync)
            float btnW = BaseBtnW * scale, btnH = BaseBtnH * scale, btnGap = BaseBtnGap * scale;
            float totalBtnW = btnW * 2 + btnGap;
            float btnStartX = dlgX + (dlgW - totalBtnW) * 0.5f;
            float btnY = dlgY + BaseBtnOffsetY * scale;

            Vector3 unselectedBg = new(0.12f, 0.13f, 0.18f);
            Vector3 unselectedText = new(0.55f, 0.55f, 0.65f);

            for (int b = 0; b < 2; b++)
            {
                bool isSel = (b == selection);
                float bx = btnStartX + b * (btnW + btnGap);

                hud.DrawBox(bx, btnY, btnW, btnH, isSel ? btnBgColors[b] : unselectedBg);

                if (isSel)
                {
                    hud.DrawBox(bx + 2f * scale, btnY + 3f * scale, 2f * scale, btnH - 6f * scale, btnSideColors[b]);
                    hud.DrawBox(bx, btnY, btnW, 1f * scale, btnSideColors[b] * 0.5f);
                    hud.DrawBox(bx, btnY + btnH - 1f * scale, btnW, 1f * scale, btnSideColors[b] * 0.5f);
                }

                string label = isSel ? btnLabels[b] : btnLabels[b];
                var ext = hud.GetTextExtents(label);
                hud.DrawText(label, bx + (btnW - ext.Width) * 0.5f,
                    ext.GetCenteredBaselineY(btnY, btnH),
                    isSel ? btnTextColors[b] : unselectedText);
            }
        }

        /// <summary>
        /// Compute the bounding rectangle of a confirm-dialog button in screen coordinates.
        /// Use this from input-handling code so hit-testing always matches what DrawBox renders.
        /// </summary>
        /// <param name="w">Screen width in pixels.</param>
        /// <param name="h">Screen height in pixels.</param>
        /// <param name="scale">Size multiplier (same value passed to DrawBox).</param>
        /// <param name="buttonIndex">Button index (0 = left, 1 = right).</param>
        /// <param name="x">Left edge of the button in screen coordinates.</param>
        /// <param name="y">Top edge of the button in screen coordinates.</param>
        /// <param name="width">Width of the button.</param>
        /// <param name="height">Height of the button.</param>
        public static void GetButtonRect(int w, int h, float scale, int buttonIndex,
            out float x, out float y, out float width, out float height)
        {
            float dlgW = BaseDlgW * scale;
            float dlgH = BaseDlgH * scale;
            float dlgX = (w - dlgW) * 0.5f;
            float dlgY = (h - dlgH) * 0.5f;

            float btnGap = BaseBtnGap * scale;
            float totalBtnW = BaseBtnW * scale * 2 + btnGap;
            float btnStartX = dlgX + (dlgW - totalBtnW) * 0.5f;

            x = btnStartX + buttonIndex * (BaseBtnW * scale + btnGap);
            y = dlgY + BaseBtnOffsetY * scale;
            width = BaseBtnW * scale;
            height = BaseBtnH * scale;
        }
    }
}
