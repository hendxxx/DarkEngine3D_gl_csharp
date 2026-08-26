using System.Numerics;
using System.Text.Json.Serialization;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>
    /// Sky rendering mode. Controls which renderer and shader are used.
    /// </summary>
    public enum SkyType
    {
        Procedural = 0,  // Default: procedural realtime sky (sun, moon, stars, clouds, scattering)
        Skybox = 1,      // Classic cubemap skybox with 6 face textures
        Dome = 2         // Dome/sphere with a single panoramic texture
    }

    /// <summary>
    /// Sun settings for the procedural realtime sky.
    /// </summary>
    public class SunSettings
    {
        /// <summary>Sun disc size (angular radius multiplier). Default 1.0.</summary>
        public float Size { get; set; } = 1.0f;
        /// <summary>Sun disc edge softness. Default 0.8.</summary>
        public float Softness { get; set; } = 0.8f;
        /// <summary>Sun core color. Default white.</summary>
        public Vector3 Color { get; set; } = new(1.0f, 1.0f, 1.0f);
        /// <summary>Sun glow intensity multiplier. Default 1.0.</summary>
        public float GlowIntensity { get; set; } = 1.0f;

        public SunSettings Clone() => new()
        {
            Size = Size,
            Softness = Softness,
            Color = Color,
            GlowIntensity = GlowIntensity
        };
    }

    /// <summary>
    /// Atmospheric scattering settings (Rayleigh + Mie).
    /// </summary>
    public class AtmosphericScatteringSettings
    {
        /// <summary>Overall scattering intensity. Default 18.0.</summary>
        public float Intensity { get; set; } = 18.0f;

        /// <summary>Rayleigh scattering strength. Default 0.5.</summary>
        public float Rayleigh { get; set; } = 0.5f;
        /// <summary>Rayleigh color tint. Default soft blue-white for realistic sky.</summary>
        public Vector3 RayColor { get; set; } = new(0.6f, 0.8f, 1.0f);
        /// <summary>Rayleigh height scale (lower = denser near surface). Default 0.8.</summary>
        public float RayHeight { get; set; } = 0.8f;

        /// <summary>Mie scattering strength. Default 0.35.</summary>
        public float Mie { get; set; } = 0.35f;
        /// <summary>Mie color tint. Default warm orange for horizon haze.</summary>
        public Vector3 MieColor { get; set; } = new(1.0f, 0.6f, 0.35f);
        /// <summary>Mie directionality (asymmetry factor g). Default 0.8.</summary>
        public float MieFocus { get; set; } = 0.8f;
        /// <summary>Mie height scale (lower = denser near surface). Default 0.6.</summary>
        public float MieHeight { get; set; } = 0.6f;

        public AtmosphericScatteringSettings Clone() => new()
        {
            Intensity = Intensity,
            Rayleigh = Rayleigh,
            RayColor = RayColor,
            RayHeight = RayHeight,
            Mie = Mie,
            MieColor = MieColor,
            MieFocus = MieFocus,
            MieHeight = MieHeight
        };
    }

    /// <summary>
    /// Procedural volumetric cloud settings.
    /// </summary>
    public class VolumetricCloudSettings
    {
        /// <summary>Cloud density/coverage 0..1. Default 0.3.</summary>
        public float Density { get; set; } = 0.3f;
        /// <summary>Cloud altitude (world units). Default 2.5.</summary>
        public float Altitude { get; set; } = 2.5f;
        /// <summary>Cloud speed (world units/sec). Default 0.035.</summary>
        public float Speed { get; set; } = 0.035f;
        /// <summary>Cloud detail (FBM detail amount). Default 0.35.</summary>
        public float Detail { get; set; } = 0.35f;
        /// <summary>Cloud erosion amount. Default 0.25.</summary>
        public float Erosion { get; set; } = 0.25f;
        /// <summary>Cloud shadow strength. Default 0.65.</summary>
        public float ShadowStrength { get; set; } = 0.65f;
        /// <summary>Cloud scatter (sun light through clouds). Default 0.25.</summary>
        public float Scatter { get; set; } = 0.25f;
        /// <summary>Cloud tint color. Default white.</summary>
        public Vector3 TintColor { get; set; } = new(1.0f, 1.0f, 1.0f);
        /// <summary>Cirrus cloud strength. Default 0.1.</summary>
        public float CirrusStrength { get; set; } = 0.1f;
        /// <summary>Enable/disable procedural volumetric clouds. Default true.</summary>
        public bool Enabled { get; set; } = true;

        public VolumetricCloudSettings Clone() => new()
        {
            Density = Density,
            Altitude = Altitude,
            Speed = Speed,
            Detail = Detail,
            Erosion = Erosion,
            ShadowStrength = ShadowStrength,
            Scatter = Scatter,
            TintColor = TintColor,
            CirrusStrength = CirrusStrength,
            Enabled = Enabled
        };
    }

    /// <summary>
    /// Moon visual settings.
    /// </summary>
    public class MoonSettings
    {
        /// <summary>Moon brightness multiplier. Default 1.25.</summary>
        public float Brightness { get; set; } = 1.25f;
        /// <summary>Moon size (angular radius multiplier). Default 1.0.</summary>
        public float Size { get; set; } = 1.0f;
        /// <summary>Moon glow radius multiplier. Default 1.0.</summary>
        public float GlowRadius { get; set; } = 1.0f;
        /// <summary>Moon tint color. Default white.</summary>
        public Vector3 TintColor { get; set; } = new(1.0f, 1.0f, 1.0f);
        /// <summary>Path to the moon texture file (.png, .jpg). Empty = default.</summary>
        public string TexturePath { get; set; } = "Artifacts/Textures/moon.png";
        /// <summary>Moon rotation speed (radians/sec around sky). Default 0.</summary>
        public float RotationSpeed { get; set; } = 0f;
        /// <summary>Moon phase offset 0..1 (0=new, 0.5=full). Default 0.5 (full moon).</summary>
        public float PhaseOffset { get; set; } = 0.5f;

        public MoonSettings Clone() => new()
        {
            Brightness = Brightness,
            Size = Size,
            GlowRadius = GlowRadius,
            TintColor = TintColor,
            TexturePath = TexturePath,
            RotationSpeed = RotationSpeed,
            PhaseOffset = PhaseOffset
        };
    }

    /// <summary>
    /// Stars settings.
    /// </summary>
    public class StarSettings
    {
        /// <summary>Star brightness multiplier. Default 1.0.</summary>
        public float Brightness { get; set; } = 1.0f;
        /// <summary>Star density (amount of stars visible). Default 1.0.</summary>
        public float Density { get; set; } = 1.0f;
        /// <summary>Star twinkle speed. Default 1.0.</summary>
        public float TwinkleSpeed { get; set; } = 1.0f;
        /// <summary>Star color tint. Default white.</summary>
        public Vector3 Color { get; set; } = new(1.0f, 1.0f, 1.0f);
        /// <summary>Enable/disable stars. Default true.</summary>
        public bool Enabled { get; set; } = true;

        public StarSettings Clone() => new()
        {
            Brightness = Brightness,
            Density = Density,
            TwinkleSpeed = TwinkleSpeed,
            Color = Color,
            Enabled = Enabled
        };
    }

    /// <summary>
    /// Eclipse settings (lunar + solar).
    /// </summary>
    public class EclipseSettings
    {
        /// <summary>Solar eclipse amount 0..1 (0=none, 1=total). Default 0.</summary>
        public float SolarEclipse { get; set; } = 0f;
        /// <summary>Lunar eclipse amount 0..1 (0=none, 1=total). Default 0.</summary>
        public float LunarEclipse { get; set; } = 0f;
        /// <summary>Eclipse glow color. Default white.</summary>
        public Vector3 GlowColor { get; set; } = new(1.0f, 1.0f, 1.0f);

        public EclipseSettings Clone() => new()
        {
            SolarEclipse = SolarEclipse,
            LunarEclipse = LunarEclipse,
            GlowColor = GlowColor
        };
    }

    /// <summary>
    /// Sun ray / god ray settings.
    /// </summary>
    public class SunRaySettings
    {
        /// <summary>Sun ray intensity 0..1. Default 0.3.</summary>
        public float Intensity { get; set; } = 0.3f;
        /// <summary>Number of ray spokes. Default 12.</summary>
        public int RayCount { get; set; } = 12;
        /// <summary>Ray length multiplier. Default 1.0.</summary>
        public float Length { get; set; } = 1.0f;
        /// <summary>Ray color tint. Default white.</summary>
        public Vector3 Color { get; set; } = new(1.0f, 1.0f, 1.0f);
        /// <summary>Enable/disable sun rays. Default true.</summary>
        public bool Enabled { get; set; } = true;

        public SunRaySettings Clone() => new()
        {
            Intensity = Intensity,
            RayCount = RayCount,
            Length = Length,
            Color = Color,
            Enabled = Enabled
        };
    }

    /// <summary>
    /// Skybox face texture paths (6 faces for cubemap).
    /// </summary>
    public class SkyboxFaceTextures
    {
        public string Right { get; set; } = "";   // +X
        public string Left { get; set; } = "";    // -X
        public string Top { get; set; } = "";     // +Y
        public string Bottom { get; set; } = "";  // -Y
        public string Front { get; set; } = "";   // +Z
        public string Back { get; set; } = "";    // -Z

        public SkyboxFaceTextures Clone() => new()
        {
            Right = Right,
            Left = Left,
            Top = Top,
            Bottom = Bottom,
            Front = Front,
            Back = Back
        };

        /// <summary>Check if at least one face has a valid texture path.</summary>
        public bool HasAnyTexture() =>
            !string.IsNullOrEmpty(Right) || !string.IsNullOrEmpty(Left) ||
            !string.IsNullOrEmpty(Top) || !string.IsNullOrEmpty(Bottom) ||
            !string.IsNullOrEmpty(Front) || !string.IsNullOrEmpty(Back);
    }

    /// <summary>
    /// Dome sphere settings.
    /// </summary>
    public class DomeSettings
    {
        /// <summary>Path to panoramic/equirectangular texture. Empty = solid color.</summary>
        public string TexturePath { get; set; } = "";
        /// <summary>Dome radius (world units). Default 500.</summary>
        public float Radius { get; set; } = 500f;
        /// <summary>Dome color tint (multiplied with texture). Default white.</summary>
        public Vector3 TintColor { get; set; } = new(1.0f, 1.0f, 1.0f);
        /// <summary>Manual rotation offset (radians). Default 0.</summary>
        public float RotationY { get; set; } = 0f;

        // ── Auto Rotate ──
        /// <summary>Enable automatic rotation. Default false.</summary>
        public bool AutoRotate { get; set; } = false;
        /// <summary>Rotation speed (degrees per second). Default 10.</summary>
        public float RotateSpeed { get; set; } = 10f;
        /// <summary>Rotation axis: 0=Horizontal(Y), 1=Vertical(X), 2=Both(XY). Default 0.</summary>
        public int RotateAxis { get; set; } = 0;
        /// <summary>Random variation in speed (0..1). 0=constant, 1=fully random. Default 0.</summary>
        public float RotateVariation { get; set; } = 0f;
        /// <summary>Ping-pong mode: oscillate between -amplitude and +amplitude instead of full rotation. Default false.</summary>
        public bool PingPong { get; set; } = false;
        /// <summary>Ping-pong amplitude in degrees. Default 30.</summary>
        public float PingPongAmplitude { get; set; } = 30f;

        public DomeSettings Clone() => new()
        {
            TexturePath = TexturePath,
            Radius = Radius,
            TintColor = TintColor,
            RotationY = RotationY,
            AutoRotate = AutoRotate,
            RotateSpeed = RotateSpeed,
            RotateAxis = RotateAxis,
            RotateVariation = RotateVariation,
            PingPong = PingPong,
            PingPongAmplitude = PingPongAmplitude
        };
    }

    /// <summary>
    /// Master sky settings container. Holds all parameters for the 3 sky types.
    /// Stored per Sky EditorObject and serialized with the scene.
    /// </summary>
    public class SkySettings
    {
        /// <summary>Active sky type (0=Procedural, 1=Skybox, 2=Dome).</summary>
        public SkyType Type { get; set; } = SkyType.Procedural;

        // ── Skybox (Type == Skybox) ──
        public SkyboxFaceTextures SkyboxFaces { get; set; } = new();

        // ── Dome (Type == Dome) ──
        public DomeSettings Dome { get; set; } = new();

        // ── Procedural Realtime (Type == Procedural) ──
        public SunSettings Sun { get; set; } = new();
        public AtmosphericScatteringSettings Scattering { get; set; } = new();
        public VolumetricCloudSettings Clouds { get; set; } = new();
        public MoonSettings Moon { get; set; } = new();
        public StarSettings Stars { get; set; } = new();
        public EclipseSettings Eclipses { get; set; } = new();
        public SunRaySettings SunRays { get; set; } = new();

        /// <summary>Clone all settings.</summary>
        public SkySettings Clone() => new()
        {
            Type = Type,
            SkyboxFaces = SkyboxFaces.Clone(),
            Dome = Dome.Clone(),
            Sun = Sun.Clone(),
            Scattering = Scattering.Clone(),
            Clouds = Clouds.Clone(),
            Moon = Moon.Clone(),
            Stars = Stars.Clone(),
            Eclipses = Eclipses.Clone(),
            SunRays = SunRays.Clone()
        };

        /// <summary>
        /// Randomize all procedural sky values for creative exploration.
        /// </summary>
        public void Randomize()
        {
            var rng = Random.Shared;

            // Sun
            Sun.Size = 0.5f + rng.NextSingle() * 1.5f;
            Sun.Softness = 0.3f + rng.NextSingle() * 0.7f;
            Sun.Color = new Vector3(
                0.8f + rng.NextSingle() * 0.2f,
                0.7f + rng.NextSingle() * 0.3f,
                0.5f + rng.NextSingle() * 0.5f);
            Sun.GlowIntensity = 0.5f + rng.NextSingle() * 1.5f;

            // Scattering
            Scattering.Intensity = 1f + rng.NextSingle() * 49f;           // 1-50: match slider range
            Scattering.Rayleigh = 0.05f + rng.NextSingle() * 4.95f;       // 0.05-5: match slider
            Scattering.RayColor = new Vector3(
                rng.NextSingle() * 0.6f,                                    // R: 0-0.6 (bluer skies)
                0.1f + rng.NextSingle() * 0.7f,                            // G: 0.1-0.8
                0.4f + rng.NextSingle() * 0.6f);                           // B: 0.4-1.0 (always some blue)
            Scattering.RayHeight = 0.1f + rng.NextSingle() * 4.9f;        // 0.1-5: match slider
            Scattering.Mie = 0.05f + rng.NextSingle() * 1.95f;            // 0.05-2: match slider
            Scattering.MieColor = new Vector3(
                0.4f + rng.NextSingle() * 0.6f,                            // R: 0.4-1.0 (warm horizon)
                0.3f + rng.NextSingle() * 0.5f,                            // G: 0.3-0.8
                0.3f + rng.NextSingle() * 0.4f);                           // B: 0.3-0.7 (warm bias)
            Scattering.MieFocus = 0.05f + rng.NextSingle() * 0.9f;        // 0.05-0.95: match slider
            Scattering.MieHeight = 0.1f + rng.NextSingle() * 1.9f;        // 0.1-2: match slider

            // Clouds
            Clouds.Density = 0.1f + rng.NextSingle() * 0.8f;
            Clouds.Altitude = 1.5f + rng.NextSingle() * 4f;
            Clouds.Speed = rng.NextSingle() * 0.1f;
            Clouds.Detail = rng.NextSingle() * 0.7f;
            Clouds.Erosion = rng.NextSingle() * 0.5f;
            Clouds.ShadowStrength = 0.2f + rng.NextSingle() * 0.8f;
            Clouds.Scatter = 0.1f + rng.NextSingle() * 0.5f;
            Clouds.CirrusStrength = rng.NextSingle() * 0.3f;

            // Moon
            Moon.Brightness = 0.5f + rng.NextSingle() * 1.5f;
            Moon.Size = 0.7f + rng.NextSingle() * 0.6f;
            Moon.GlowRadius = 0.5f + rng.NextSingle() * 1.5f;
            Moon.PhaseOffset = rng.NextSingle();

            // Stars
            Stars.Brightness = 0.3f + rng.NextSingle() * 1.7f;
            Stars.Density = 0.3f + rng.NextSingle() * 1.7f;
            Stars.TwinkleSpeed = 0.3f + rng.NextSingle() * 2f;

            // Eclipses
            Eclipses.SolarEclipse = rng.NextSingle() > 0.7f ? rng.NextSingle() : 0f;
            Eclipses.LunarEclipse = rng.NextSingle() > 0.8f ? rng.NextSingle() * 0.5f : 0f;

            // SunRays
            SunRays.Intensity = rng.NextSingle() > 0.4f ? rng.NextSingle() * 0.6f : 0f;
            SunRays.RayCount = 6 + rng.Next(0, 20);
            SunRays.Length = 0.3f + rng.NextSingle() * 1.5f;
            SunRays.Color = new Vector3(
                0.8f + rng.NextSingle() * 0.2f,
                0.6f + rng.NextSingle() * 0.4f,
                0.4f + rng.NextSingle() * 0.4f);
        }

        /// <summary>Reset to bright blue sky defaults (clouds off, sunrays off).</summary>
        public void ResetDefaults()
        {
            Type = SkyType.Procedural;
            Sun = new SunSettings();
            Scattering = new AtmosphericScatteringSettings();
            Clouds = new VolumetricCloudSettings() { Enabled = false };
            Moon = new MoonSettings();
            Stars = new StarSettings();
            Eclipses = new EclipseSettings();
            SunRays = new SunRaySettings() { Intensity = 0f };
        }

        /// <summary>Clear sky preset: bright blue, no clouds, minimal haze.</summary>
        public void ApplyClearSky()
        {
            Type = SkyType.Procedural;
            Sun.Size = 1.0f;
            Sun.Softness = 0.8f;
            Sun.Color = new Vector3(1f, 1f, 1f);
            Sun.GlowIntensity = 1.0f;
            Scattering.Intensity = 22f;
            Scattering.Rayleigh = 0.5f;
            Scattering.RayColor = new Vector3(0.5f, 0.7f, 1.0f);
            Scattering.RayHeight = 1.0f;
            Scattering.Mie = 0.2f;
            Scattering.MieColor = new Vector3(0.8f, 0.7f, 0.6f);
            Scattering.MieFocus = 0.75f;
            Scattering.MieHeight = 0.8f;
            Clouds.Enabled = false;
            Moon.Brightness = 0f;
            Stars.Brightness = 0f;
            Eclipses.SolarEclipse = 0f;
            Eclipses.LunarEclipse = 0f;
            SunRays.Intensity = 0f;
        }

        /// <summary>Sunset preset: warm golden hour.</summary>
        public void ApplySunset()
        {
            Type = SkyType.Procedural;
            Sun.Size = 1.4f;
            Sun.Softness = 0.9f;
            Sun.Color = new Vector3(1.0f, 0.65f, 0.3f);
            Sun.GlowIntensity = 1.8f;
            Scattering.Intensity = 30f;
            Scattering.Rayleigh = 1.2f;
            Scattering.RayColor = new Vector3(0.9f, 0.4f, 0.15f);
            Scattering.RayHeight = 0.6f;
            Scattering.Mie = 0.8f;
            Scattering.MieColor = new Vector3(1.0f, 0.5f, 0.2f);
            Scattering.MieFocus = 0.9f;
            Scattering.MieHeight = 0.4f;
            Clouds.Enabled = true;
            Clouds.Density = 0.35f;
            Clouds.Altitude = 2.0f;
            Clouds.Speed = 0.02f;
            Clouds.Detail = 0.3f;
            Clouds.Erosion = 0.2f;
            Clouds.Scatter = 0.4f;
            Clouds.CirrusStrength = 0.15f;
            Moon.Brightness = 0f;
            Stars.Brightness = 0.2f;
            SunRays.Intensity = 0.4f;
            SunRays.RayCount = 12;
            SunRays.Length = 0.8f;
            SunRays.Color = new Vector3(1.0f, 0.7f, 0.3f);
        }

        /// <summary>Night preset: dark sky with moon and stars.</summary>
        public void ApplyNight()
        {
            Type = SkyType.Procedural;
            Sun.Size = 0.8f;
            Sun.Softness = 0.5f;
            Sun.Color = new Vector3(0.6f, 0.65f, 0.8f);
            Sun.GlowIntensity = 0.3f;
            Scattering.Intensity = 8f;
            Scattering.Rayleigh = 0.15f;
            Scattering.RayColor = new Vector3(0.1f, 0.15f, 0.3f);
            Scattering.RayHeight = 0.5f;
            Scattering.Mie = 0.1f;
            Scattering.MieColor = new Vector3(0.15f, 0.12f, 0.2f);
            Scattering.MieFocus = 0.5f;
            Scattering.MieHeight = 0.3f;
            Clouds.Enabled = true;
            Clouds.Density = 0.15f;
            Clouds.Altitude = 3.0f;
            Clouds.Speed = 0.01f;
            Clouds.Detail = 0.2f;
            Clouds.Erosion = 0.15f;
            Clouds.Scatter = 0.05f;
            Clouds.CirrusStrength = 0.05f;
            Moon.Brightness = 1.2f;
            Moon.Size = 1.0f;
            Moon.GlowRadius = 1.2f;
            Stars.Brightness = 1.5f;
            Stars.Density = 1.5f;
            Stars.TwinkleSpeed = 1.0f;
            SunRays.Intensity = 0f;
        }
    }
}
