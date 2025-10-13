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
        void ValueFromString(string valueString);
        string ValueToString();
    }

    public abstract class PropertyBase<T> : IProperty
    {
        private T _value;
        public event Action<T> ValueChanged;

        public string Key { get; }
        public T Value 
        {
            get { return _value; }
            set 
            {
                if (_value.Equals(value))
                    return;

                _value = value;
                ValueChanged?.Invoke(value);
            }
        }

        protected PropertyBase(string key, T value)
        {
            Key = key;
            Value = value;
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

        public DoubleProperty(string key, double value, int precision = 2) : base(key, value)
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
        public BoolProperty(string key, bool value) : base(key, value)
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

        public GPSProperty(string key, Vector3D value) : base(key, value)
        {
        }

        protected override Vector3D StringToValue(string valueString)
        {
            var parts = valueString.Split(':');
            if (parts.Length < 3)
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
            return $"{_gpsName}:{Value.X}:{Value.Y}:{Value.Z}";
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
                if (string.IsNullOrEmpty(line) || line.StartsWith("==="))
                    continue;

                var colonIndex = line.IndexOf(':');
                if (colonIndex == -1)
                    continue;

                var key = line.Substring(0, colonIndex).Trim();
                var value = line.Substring(colonIndex + 1).Trim();

                var existingSetting = _properties.FirstOrDefault(s => s.Key == key);
                existingSetting?.ValueFromString(value);
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
