using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    static class CustomDataConnector
    {
        private static bool FindValue(IMyTerminalBlock block, string key, out string valueString)
        {
            valueString = string.Empty;
            var customData = block.CustomData;
            var lines = customData.Split('\n');
            key = (key + ":").ToLower();
            var index = -1;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().ToLower() == key)
                {
                    index = i + 1;
                    break;
                }
            }
            if (index != -1 && index < lines.Length)
            {
                valueString = lines[index];
                return true;
            }
            return false;
        }

        public static bool ParseGPS(IMyTerminalBlock block, string key, out Vector3D position)
        {
            position = Vector3D.Zero;
            string valueString;
            if (!FindValue(block, key, out valueString))
            {
                return false;
            }
            valueString = valueString.Trim();
            if (!valueString.StartsWith("GPS:"))
            {
                return false;
            }
            string[] parts = valueString.Split(':');
            if (parts.Length < 5)
            {
                return false;
            }

            double x, y, z;
            if (!double.TryParse(parts[2], out x) ||
                !double.TryParse(parts[3], out y) ||
                !double.TryParse(parts[4], out z))
            {
                return false;
            }

            position = new Vector3D(x, y, z);
            return true;
        }

        public static bool ParseDouble(IMyTerminalBlock block, string key, out double value)
        {
            value = 0;
            string valueString;
            if (!FindValue(block, key, out valueString))
            {
                return false;
            }
            valueString = valueString.Trim();
            if (!double.TryParse(valueString, out value))
            {
                return false;
            }
            return true;
        }

        public static bool ParseBool(IMyTerminalBlock block, string key, out bool value)
        {
            value = false;
            string valueString;
            if (!FindValue(block, key, out valueString))
            {
                return false;
            }
            valueString = valueString.Trim();
            if (!bool.TryParse(valueString, out value))
            {
                return false;
            }
            return true;
        }
    }
}