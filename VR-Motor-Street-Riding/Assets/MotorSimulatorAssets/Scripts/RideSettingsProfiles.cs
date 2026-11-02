using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AIT.Riding
{
    public static class RideSettingsProfiles
    {
        [Serializable] sealed class VersionHeader { public int profileVersion; }
        public static string DirectoryPath
        {
            get
            {
#if UNITY_EDITOR
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Personal", "RideProfiles"));
#else
                return Path.Combine(Application.persistentDataPath, "RideProfiles");
#endif
            }
        }

        public static string[] Names() => Directory.Exists(DirectoryPath)
            ? Directory.GetFiles(DirectoryPath, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToArray()
            : Array.Empty<string>();

        public static string ProfilePath(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 80 || name == "." || name == ".." ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("/") || name.Contains("\\"))
                throw new ArgumentException("Use a profile name without path separators (1-80 characters).");
            return Path.Combine(DirectoryPath, name + ".json");
        }

        public static void Save(string name, RideTestSettings settings)
        {
            Validate(settings);
            string path = ProfilePath(name);
            Directory.CreateDirectory(DirectoryPath);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(settings, true));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public static void Delete(string name)
        {
            // ProfilePath rejects traversal; only the selected JSON file is removed.
            File.Delete(ProfilePath(name));
        }

        public static void Rename(string name, string newName)
        {
            string source = ProfilePath(name), destination = ProfilePath(newName);
            if (!File.Exists(source)) throw new FileNotFoundException("Profile no longer exists.", source);
            if (source == destination) return;
            bool sameWindowsPath = Application.platform == RuntimePlatform.WindowsEditor ||
                Application.platform == RuntimePlatform.WindowsPlayer;
            sameWindowsPath &= string.Equals(source, destination, StringComparison.OrdinalIgnoreCase);
            if (File.Exists(destination) && !sameWindowsPath)
                throw new IOException("A profile with that name already exists.");
            if (!sameWindowsPath) { File.Move(source, destination); return; }
            // Windows needs a temporary name for case-only changes. Preserve the
            // original bytes, and restore the original name if the second move fails.
            string temporary = source + ".rename-" + Guid.NewGuid().ToString("N");
            File.Move(source, temporary);
            try { File.Move(temporary, destination); }
            catch { File.Move(temporary, source); throw; }
        }

        public static RideTestSettings Load(string name)
        {
            string path = ProfilePath(name);
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Profile is too large.");
            return FromJson(File.ReadAllText(path));
        }

        public static RideTestSettings FromJson(string json)
        {
            if (JsonUtility.FromJson<VersionHeader>(json)?.profileVersion != 1)
                throw new InvalidDataException("Unsupported or missing profileVersion; expected 1.");
            var settings = ScriptableObject.CreateInstance<RideTestSettings>();
            try { JsonUtility.FromJsonOverwrite(json, settings); Validate(settings); return settings; }
            catch { UnityEngine.Object.DestroyImmediate(settings); throw; }
        }

        public static void Validate(RideTestSettings s)
        {
            if (s == null || s.profileVersion != 1 || !Enum.IsDefined(typeof(RideInputSource), s.inputSource) ||
                !Enum.IsDefined(typeof(RideComfortMode), s.comfortMode)) throw new InvalidDataException("Invalid profile or enum value.");
            Range(s.steeringDeadzone, 0, .5f, "Steering deadzone");
            Range(s.inputTimeoutSeconds, .05f, 2, "Input timeout");
            Range(s.wheelbase, .5f, 3, "Wheelbase"); Range(s.maximumSpeedKph, 1, 120, "Maximum speed");
            Range(s.acceleration, .1f, 10, "Acceleration"); Range(s.brakingDeceleration, .1f, 15, "Braking");
            Range(s.coastingDeceleration, 0, 5, "Coasting"); Range(s.quadraticDrag, 0, 1, "Drag");
            Range(s.lowSpeedSteeringDegrees, 1, 45, "Low-speed steering");
            Range(s.highSpeedSteeringDegrees, 1, 20, "High-speed steering");
            Range(s.steeringRateDegreesPerSecond, 1, 360, "Steering response");
            Range(s.maximumYawDegreesPerSecond, 1, 180, "Yaw limit");
            Range(s.lowSpeedYawDegreesPerSecond, 1, 180, "Low-speed yaw limit");
            Range(s.steeringTransitionSpeedKph, 1, 80, "Steering transition speed");
            Range(s.maximumReverseSpeedKph, .5f, 6, "Reverse speed");
            Range(s.reverseAcceleration, .1f, 3, "Reverse acceleration");
            Range(s.maximumLateralAcceleration, .1f, 15, "Lateral acceleration");
            Range(s.seatedEyePosition.x, -1, 1, "Eye left/right"); Range(s.seatedEyePosition.y, .5f, 2.2f, "Eye height");
            Range(s.seatedEyePosition.z, -1.5f, 1, "Eye forward/back");
            Range(s.desktopLookPitch, -60, 60, "Desktop pitch"); Range(s.desktopLookYaw, -180, 180, "Desktop yaw");
            Range(s.sectionSpeedCapsKph.x, 1, 120, "Section 1 speed");
            Range(s.sectionSpeedCapsKph.y, 1, 120, "Section 2 speed");
            Range(s.sectionSpeedCapsKph.z, 1, 120, "Section 3 speed");
            Range(s.maximumBodyLeanDegrees, 0, 30, "Body lean"); Range(s.cameraLeanGain, 0, .5f, "Camera lean gain");
            Range(s.maximumCameraLeanDegrees, 0, 8, "Camera lean");
            Range(s.mirrorUpdatesPerSecond, 5, 60, "Mirror update rate");
            Range(s.mirrorResolution, 128, 1024, "Mirror resolution"); Range(s.telemetryHz, 1, 90, "Telemetry rate");
        }
        static void Range(float value, float min, float max, string label)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new InvalidDataException($"{label} must be between {min} and {max}.");
        }
    }
}
