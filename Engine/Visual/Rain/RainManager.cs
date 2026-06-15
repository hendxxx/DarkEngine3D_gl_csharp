using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;

public unsafe class RainManager
{
    private Rain rain;
    private RainOverlay overlay;

    private Vector3 windDir = new Vector3(0.3f, 0f, 0.2f); // default angin

    public RainManager(uint rainShader, uint overlayShader, int rainCount)
    {
        rain = new Rain(rainShader, rainCount);
        overlay = new RainOverlay(overlayShader);
    }

    // Smoothstep manual (biar ga ribet)
    private float SmoothStep(float edge0, float edge1, float x)
    {
        x = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    // 1. Hitung intensitas hujan dari weatherMode
    float rainAmount = 0;

    // ============================
    // UPDATE + DRAW (1 CALL)
    // ============================
    public void UpdateAndDraw(
        Camera camera, 
        float weatherMode,
        float time,
        uint sceneTexture)
    {
        rainAmount = SmoothStep(0.6f, 1.0f, weatherMode);
        // 2. Update world-space rain
        rain.Update(camera.Position, windDir);

        // 3. Draw world-space rain
        rain.Draw(camera.GetViewMatrix(), camera.GetProjectionMatrix(), camera.Position, windDir, rainAmount, time);

        // 4. Draw overlay (kamera basah)
        if (weatherMode == 1)
        {

            overlay.Draw(sceneTexture, rainAmount, time);
        } 


    }

    // ============================
    // OPSIONAL: Ganti arah angin
    // ============================
    public void SetWind(Vector3 newWind)
    {
        windDir = newWind;
    }
}
