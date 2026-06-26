using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DarkEngine3D_gl_csharp.Engine.Scene
{
    /// <summary>
    /// Serializable scene data for save/load functionality.
    /// Stores camera state, time of day, and object references.
    /// </summary>
    public class SceneData
    {
        // ── Metadata ──
        public string SceneName { get; set; } = "Untitled";
        public string SaveVersion { get; set; } = "1.0";
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;

        // ── Camera ──
        public CameraStateData Camera { get; set; } = new();

        // ── World ──
        public WorldStateData World { get; set; } = new();

        // ── Objects ──
        public List<ObjectStateData> Objects { get; set; } = [];

        /// <summary>Save scene data to a JSON file.</summary>
        public static void SaveToFile(string path, SceneData data)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            string json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(path, json);
            Console.WriteLine($"[SceneData] Saved to: {path}");
        }

        /// <summary>Load scene data from a JSON file.</summary>
        public static SceneData? LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[SceneData] File not found: {path}");
                return null;
            }
            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<SceneData>(json);
            Console.WriteLine($"[SceneData] Loaded from: {path}");
            return data;
        }

        /// <summary>
        /// Capture current scene state from GameScene into a SceneData object.
        /// </summary>
        public static SceneData Capture(GameScene gameScene, Camera camera, Lights light, ObjectManager? objectManager)
        {
            var data = new SceneData
            {
                SceneName = "GameScene",
                Camera = new CameraStateData
                {
                    Position = new SerializableVector3(camera.Position),
                    Yaw = camera.Yaw,
                    Pitch = camera.Pitch,
                    Mode = camera.CurrentMode.ToString()
                },
                World = new WorldStateData
                {
                    TimeOfDay = light.GetFormattedTime(),
                    WorldTimeRadians = light.WorldTime
                }
            };

            if (objectManager != null)
            {
                foreach (var obj in objectManager.GetObjects())
                {
                    // Extract Yaw from Quaternion
                    float yaw = MathF.Atan2(
                        2f * (obj.Rotation.Y * obj.Rotation.W - obj.Rotation.X * obj.Rotation.Z),
                        1f - 2f * (obj.Rotation.Y * obj.Rotation.Y + obj.Rotation.Z * obj.Rotation.Z)
                    ) * (180f / MathF.PI);

                    data.Objects.Add(new ObjectStateData
                    {
                        Position = new SerializableVector3(obj.Position),
                        Yaw = yaw,
                        IsPlayer = obj.IsPlayer
                    });
                }
            }

            return data;
        }
    }

    // ── Sub-data classes ──

    public class CameraStateData
    {
        public SerializableVector3 Position { get; set; } = new();
        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public string Mode { get; set; } = "Orbit";
    }

    public class WorldStateData
    {
        public string TimeOfDay { get; set; } = "12:00";
        public float WorldTimeRadians { get; set; }
    }

    public class ObjectStateData
    {
        public SerializableVector3 Position { get; set; } = new();
        public float Yaw { get; set; }
        public bool IsPlayer { get; set; }
    }

    /// <summary>
    /// JSON-serializable Vector3 wrapper.
    /// System.Numerics.Vector3 is not directly serializable by System.Text.Json.
    /// </summary>
    public struct SerializableVector3
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public SerializableVector3(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }

        public SerializableVector3(Vector3 v)
        {
            X = v.X; Y = v.Y; Z = v.Z;
        }

        public Vector3 ToVector3() => new(X, Y, Z);

        public static implicit operator Vector3(SerializableVector3 v) => v.ToVector3();
        public static implicit operator SerializableVector3(Vector3 v) => new(v);
    }
}
