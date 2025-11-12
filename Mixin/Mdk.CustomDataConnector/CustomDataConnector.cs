using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public interface IProperty
    {
        string Key { get; }
        bool ShowInCustomData { get; }
        string Comment { get; }
        void ValueFromString(string valueString);
        string ValueToString();
    }

    public abstract class PropertyBase<T> : IProperty
    {
        private T _value;
        public event Action<T> ValueChanged;

        public string Key { get; }
        public bool ShowInCustomData { get; set; }
        public string Comment { get; set; }
        public T Value 
        {
            get { return _value; }
            set 
            {
                if (_value != null && _value.Equals(value))
                    return;

                _value = value;
                ValueChanged?.Invoke(value);
            }
        }

        protected PropertyBase(string key, T value, bool showInCustomData = true, string comment = null)
        {
            Key = key;
            Value = value;
            ShowInCustomData = showInCustomData;
            Comment = comment;
        }

        public void ValueFromString(string valueString)
        {
            Value = StringToValue(valueString);
        }

        public abstract string ValueToString();

        protected abstract T StringToValue(string valueString);
    }

    public class DoubleProperty : PropertyBase<double>
    {
        private readonly int _precision;

        public DoubleProperty(string key, double value, int precision = 2, bool showInCustomData = true, string comment = null)
            : base(key, value, showInCustomData, comment)
        {
            _precision = precision;
        }

        protected override double StringToValue(string valueString)
        {
            return double.Parse(valueString);
        }

        public override string ValueToString()
        {
            return Value.ToString($"F{_precision}");
        }
    }

    public class BoolProperty : PropertyBase<bool>
    {
        public BoolProperty(string key, bool value, bool showInCustomData = true, string comment = null)
            : base(key, value, showInCustomData, comment)
        {
        }
        
        protected override bool StringToValue(string valueString)
        {
            return bool.Parse(valueString);
        }

        public override string ValueToString()
        {
            return Value.ToString();
        }
    }

    public class GPSProperty : PropertyBase<Vector3D>
    {
        private string _gpsName;

        public GPSProperty(string key, Vector3D value, string gpsName = null, bool showInCustomData = true, string comment = null)
            : base(key, value, showInCustomData, comment)
        {
            _gpsName = string.IsNullOrEmpty(gpsName) ? key : gpsName;
        }

        protected override Vector3D StringToValue(string valueString)
        {
            // if string starts with GPS: then remove the first 4 characters
            if (valueString.StartsWith("GPS:"))
            {
                valueString = valueString.Substring(4);
            }

            var parts = valueString.Split(':');
            if (parts.Length < 4)
            {
                return Vector3D.Zero;
            }

            _gpsName = parts[0];

            double x, y, z;
            if (double.TryParse(parts[1], out x) && double.TryParse(parts[2], out y) && double.TryParse(parts[3], out z))
            {
                return new Vector3D(x, y, z);
            }
            return Vector3D.Zero;
        }

        public override string ValueToString()
        {
            return $"{_gpsName}:{Value.X:F2}:{Value.Y:F2}:{Value.Z:F2}";
        }
    }

    public class StringProperty : PropertyBase<string>
    {
        public StringProperty(string key, string value, bool showInCustomData = true, string comment = null)
            : base(key, value, showInCustomData, comment)
        {
        }

        protected override string StringToValue(string valueString)
        {
            return valueString;
        }

        public override string ValueToString()
        {
            return Value;
        }
    }

    public abstract class Section
    {
        protected List<IProperty> _properties = new List<IProperty>();
        public string Name { get; }

        protected Section(string name)
        {
            Name = name;
        }

        public string ToCustomData()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {Name} Start ===");
            foreach (var setting in _properties)
            {
                if (!setting.ShowInCustomData)
                    continue;

                if (!string.IsNullOrWhiteSpace(setting.Comment))
                {
                    sb.AppendLine($"# {setting.Comment}");
                }
                sb.AppendLine($"{setting.Key}: {setting.ValueToString()}");
            }
            sb.AppendLine($"=== {Name} End ===");
            return sb.ToString();
        }

        public void FromCustomData(string customData)
        {
            var lines = customData.Split('\n');
            var startIdx = Array.IndexOf(lines, $"=== {Name} Start ===");
            var endIdx = Array.IndexOf(lines, $"=== {Name} End ===");
            if (startIdx == -1 || endIdx == -1)
                return;

            for (int i = startIdx + 1; i < endIdx; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("===") || line.StartsWith("#"))
                    continue;

                var colonIndex = line.IndexOf(':');
                if (colonIndex == -1)
                    continue;

                var key = line.Substring(0, colonIndex).Trim();
                var valueSegment = line.Substring(colonIndex + 1).Trim();
                var hashIndex = valueSegment.IndexOf('#');
                if (hashIndex != -1)
                {
                    valueSegment = valueSegment.Substring(0, hashIndex).Trim();
                }

                if (string.IsNullOrEmpty(valueSegment))
                    continue;

                var existingSetting = _properties.FirstOrDefault(s => s.Key == key);
                existingSetting?.ValueFromString(valueSegment);
            }
        }

        public string ToStorageString()
        {
            return string.Join("\n", _properties.Select(s => $"{Name}|{s.Key}={s.ValueToString()}"));
        }

        public void FromStorageString(string storageString)
        {
            var lines = storageString.Split('\n');
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line))
                    continue;

                var colonIndex = line.IndexOf('=');
                if (colonIndex == -1)
                    continue;

                var sectionAndKey = line.Substring(0, colonIndex).Trim().Split('|');
                if (sectionAndKey.Length != 2)
                    continue;

                var value = line.Substring(colonIndex + 1).Trim();

                var section = sectionAndKey[0].Trim();
                var key = sectionAndKey[1].Trim();

                if (section != Name) continue;
                var property = _properties.FirstOrDefault(s => s.Key == key);
                property?.ValueFromString(value);
            }
        }
    }

    public class CustomDataConnector
    {
        private const double UPDATE_FREQUENCY = 10.0; // seconds
        private Program _program;
        private readonly List<Section> _sections = new List<Section>();

        private DateTime _lastUpdateTime = DateTime.MinValue;

        public void Initialize(Program program)
        {
            _program = program;
        }

        public void AddSection(Section section)
        {
            if (_sections.Contains(section))
                return;

            _sections.Add(section);
        }


        public string Save()
        {
            ReadFromCustomData();
            var sb = new StringBuilder();
            foreach (var section in _sections)
            {
                sb.AppendLine(section.ToStorageString());
            }
            return sb.ToString();
        }

        public void Load()
        {
            if (string.IsNullOrEmpty(_program.Storage))
                return;

            foreach (var section in _sections)
            {
                section.FromStorageString(_program.Storage);
            }
            WriteToCustomData();
        }

        public void Update()
        {
            if (DateTime.Now - _lastUpdateTime > TimeSpan.FromSeconds(UPDATE_FREQUENCY))
            {
                ReadFromCustomData();
                _lastUpdateTime = DateTime.Now;
            }
        }

        public void WriteToCustomData()
        {
            var customData = new StringBuilder();
            foreach (var section in _sections)
            {
                customData.AppendLine(section.ToCustomData());
            }
            _program.Me.CustomData = customData.ToString();
        }

        public void ReadFromCustomData()
        {
            var customData = _program.Me.CustomData;
            if (string.IsNullOrEmpty(customData))
                return;

            foreach (var section in _sections)
            {
                section.FromCustomData(customData);
            }

            WriteToCustomData();
        }
    }
}
