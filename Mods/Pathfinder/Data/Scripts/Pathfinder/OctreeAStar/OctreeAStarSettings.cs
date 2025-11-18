using VRageMath;
using System;
using Sandbox.ModAPI;
using Pathfinder;
using System.Xml.Serialization;

namespace Pathfinder.OctreeAStar
{
    public class DebugRenderSetting
    {
        public string Name;
        public bool ShouldShow;
        public byte ColorR;
        public byte ColorG;
        public byte ColorB;
        public byte ColorA;
        public bool Wireframe;
        public float LineThickness;

        public DebugRenderSetting()
        {
        }

        public DebugRenderSetting(string name, bool shouldShow, Color color, bool wireframe, float lineThickness)
        {
            Name = name;
            ShouldShow = shouldShow;
            ColorR = color.R;
            ColorG = color.G;
            ColorB = color.B;
            ColorA = color.A;
            Wireframe = wireframe;
            LineThickness = lineThickness;
        }

        [XmlIgnore]
        public Color Color
        {
            get { return new Color(ColorR, ColorG, ColorB, ColorA); }
            set
            {
                ColorR = value.R;
                ColorG = value.G;
                ColorB = value.B;
                ColorA = value.A;
            }
        }
    }

    public class OctreeAStarSettingsData
    {
        public DebugRenderSetting OpenSetting;
        public DebugRenderSetting ClosedSetting;
        public DebugRenderSetting OpenReverseSetting;
        public DebugRenderSetting ClosedReverseSetting;
        public DebugRenderSetting NonLeafSetting;
        public DebugRenderSetting UnexploredSetting;
        public DebugRenderSetting OccupiedSetting;
        public DebugRenderSetting MeetSetting;
        public DebugRenderSetting DynamicObstaclesSetting;
        public bool RenderOctants = false;
        public bool RenderPath = true;
        public int MaxNodesPerFrame = 1;
        public double ExploreCostFactor = 0.75;
        public double ExploreCostFactorInTerrain = 10.0;
        public double AltitudeCostFactor = 1.0;
        public int MaxPathOptimizationSteps = 10000;
        public double MaxRootSize = 1000.0;
        public double MinRootSize = 500.0;
        public double MaxDPRRootSize = 250.0;
        public double MinDPRRootSize = 125.0;
        public float PathRenderThickness = 0.2f;
        public bool RenderDynamicObstacles = false;
        public bool ShowPathfinderMessages = false;
        public double DPRMinAltitude = 50.0;
        public double DPRStepSize = 50.0;
    }

    public class OctreeAStarSettings
    {
        private static OctreeAStarSettings _instance;
        private const string SETTINGS_FILE = "PathfinderSettings.xml";
        private const double UPDATE_DELAY_S = 10.0;
        private DateTime _lastUpdateTime = DateTime.MinValue;

        private const int DEFAULT_MAX_NODES_PER_FRAME = 1;
        private const double DEFAULT_EXPLORE_COST_FACTOR = 0.75;
        private const double DEFAULT_EXPLORE_COST_FACTOR_IN_TERRAIN = 10.0;
        private const double DEFAULT_ALTITUDE_COST_FACTOR = 1.0;
        private const int DEFAULT_MAX_PATH_OPTIMIZATION_STEPS = 10000;
        private static readonly DebugRenderSetting DEFAULT_OPEN_SETTING = new DebugRenderSetting("Open", true, Color.Green, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_CLOSED_SETTING = new DebugRenderSetting("Closed", true, Color.Red, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_OPEN_REVERSE_SETTING = new DebugRenderSetting("OpenReverse", true, Color.Blue, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_CLOSED_REVERSE_SETTING = new DebugRenderSetting("ClosedReverse", true, Color.Orange, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_NON_LEAF_SETTING = new DebugRenderSetting("NonLeaf", true, Color.White, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_UNEXPLORED_SETTING = new DebugRenderSetting("Unexplored", true, Color.White, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_OCCUPIED_SETTING = new DebugRenderSetting("Occupied", true, Color.Red, false, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_MEET_SETTING = new DebugRenderSetting("Meet", true, Color.Yellow, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_DYNAMIC_OBSTACLES_SETTING = new DebugRenderSetting("DynamicObstacles", true, Color.Red, true, 0.02f);
        private const bool DEFAULT_RENDER_OCTANTS = false;
        private const bool DEFAULT_RENDER_PATH = false;
        private const bool DEFAULT_RENDER_DYNAMIC_OBSTACLES = false;
        private const double DEFAULT_MAX_ROOT_SIZE = 1000.0;
        private const double DEFAULT_MIN_ROOT_SIZE = 500.0;
        private const double DEFAULT_MAX_DPR_ROOT_SIZE = 250.0;
        private const double DEFAULT_MIN_DPR_ROOT_SIZE = 125.0;
        private const float DEFAULT_PATH_RENDER_THICKNESS = 0.2f;
        private const double DEFAULT_DPR_MIN_ALTITUDE = 50.0;
        private const double DEFAULT_DPR_STEP_SIZE = 50.0;
        private static DebugRenderSetting CloneSetting(DebugRenderSetting setting)
        {
            return new DebugRenderSetting(setting.Name, setting.ShouldShow, setting.Color, setting.Wireframe, setting.LineThickness);
        }

        public DebugRenderSetting OpenSetting;
        public DebugRenderSetting ClosedSetting;
        public DebugRenderSetting OpenReverseSetting;
        public DebugRenderSetting ClosedReverseSetting;
        public DebugRenderSetting NonLeafSetting;
        public DebugRenderSetting UnexploredSetting;
        public DebugRenderSetting MeetSetting;
        public DebugRenderSetting OccupiedSetting;
        public DebugRenderSetting DynamicObstaclesSetting;
        public bool RenderOctants = DEFAULT_RENDER_OCTANTS;
        public bool RenderPath = DEFAULT_RENDER_PATH;
        public float PathRenderThickness = DEFAULT_PATH_RENDER_THICKNESS;
        public bool RenderDynamicObstacles = DEFAULT_RENDER_DYNAMIC_OBSTACLES;
        public int MaxNodesPerFrame = DEFAULT_MAX_NODES_PER_FRAME;
        public double ExploreCostFactor = DEFAULT_EXPLORE_COST_FACTOR;
        public double ExploreCostFactorInTerrain = DEFAULT_EXPLORE_COST_FACTOR_IN_TERRAIN;
        public double AltitudeCostFactor = DEFAULT_ALTITUDE_COST_FACTOR;
        public int MaxPathOptimizationSteps = DEFAULT_MAX_PATH_OPTIMIZATION_STEPS;
        public bool RenderPathEdges = false;
        public double MaxRootSize = DEFAULT_MAX_ROOT_SIZE;
        public double MinRootSize = DEFAULT_MIN_ROOT_SIZE;
        public double MaxDPRRootSize = DEFAULT_MAX_DPR_ROOT_SIZE;
        public double MinDPRRootSize = DEFAULT_MIN_DPR_ROOT_SIZE;
        public bool ShowPathfinderMessages = true;
        public double DPRMinAltitude = DEFAULT_DPR_MIN_ALTITUDE;
        public double DPRStepSize = DEFAULT_DPR_STEP_SIZE;
        private OctreeAStarSettings()
        {
            InitializeDefaults();
        }

        public static OctreeAStarSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new OctreeAStarSettings();
                }
                return _instance;
            }
        }

        public static bool AreMessagesEnabled => _instance?.ShowPathfinderMessages ?? true;

        private void InitializeDefaults()
        {
            OpenSetting = CloneSetting(DEFAULT_OPEN_SETTING);
            ClosedSetting = CloneSetting(DEFAULT_CLOSED_SETTING);
            OpenReverseSetting = CloneSetting(DEFAULT_OPEN_REVERSE_SETTING);
            ClosedReverseSetting = CloneSetting(DEFAULT_CLOSED_REVERSE_SETTING);
            NonLeafSetting = CloneSetting(DEFAULT_NON_LEAF_SETTING);
            UnexploredSetting = CloneSetting(DEFAULT_UNEXPLORED_SETTING);
            MeetSetting = CloneSetting(DEFAULT_MEET_SETTING);
            OccupiedSetting = CloneSetting(DEFAULT_OCCUPIED_SETTING);
            DynamicObstaclesSetting = CloneSetting(DEFAULT_DYNAMIC_OBSTACLES_SETTING);
            RenderOctants = DEFAULT_RENDER_OCTANTS;
            RenderPath = DEFAULT_RENDER_PATH;
            RenderDynamicObstacles = DEFAULT_RENDER_DYNAMIC_OBSTACLES;
            MaxNodesPerFrame = DEFAULT_MAX_NODES_PER_FRAME;
            ExploreCostFactor = DEFAULT_EXPLORE_COST_FACTOR;
            ExploreCostFactorInTerrain = DEFAULT_EXPLORE_COST_FACTOR_IN_TERRAIN;
            AltitudeCostFactor = DEFAULT_ALTITUDE_COST_FACTOR;
            MaxPathOptimizationSteps = DEFAULT_MAX_PATH_OPTIMIZATION_STEPS;
            MaxRootSize = DEFAULT_MAX_ROOT_SIZE;
            MinRootSize = DEFAULT_MIN_ROOT_SIZE;
            MaxDPRRootSize = DEFAULT_MAX_DPR_ROOT_SIZE;
            MinDPRRootSize = DEFAULT_MIN_DPR_ROOT_SIZE;
            PathRenderThickness = DEFAULT_PATH_RENDER_THICKNESS;
            ShowPathfinderMessages = true;
            DPRMinAltitude = DEFAULT_DPR_MIN_ALTITUDE;
            DPRStepSize = DEFAULT_DPR_STEP_SIZE;
        }

        public void ResetToDefaults()
        {
            InitializeDefaults();
            Save();
        }


        public void Load()
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(SETTINGS_FILE, typeof(OctreeAStarSettings)))
                {
                    InitializeDefaults();
                    Save();
                    Utils.ShowHudMessage($"Created default settings file: {SETTINGS_FILE}");
                    return;
                }

                string xmlContent;
                using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(SETTINGS_FILE, typeof(OctreeAStarSettings)))
                {
                    xmlContent = reader.ReadToEnd();
                }

                if (string.IsNullOrEmpty(xmlContent))
                {
                    Utils.ShowHudMessage("Settings file is empty, restoring defaults");
                    InitializeDefaults();
                    Save();
                    return;
                }

                bool showMessagesSpecified = xmlContent.IndexOf("ShowPathfinderMessages", StringComparison.OrdinalIgnoreCase) >= 0;

                OctreeAStarSettingsData settingsData = MyAPIGateway.Utilities.SerializeFromXML<OctreeAStarSettingsData>(xmlContent);

                if (settingsData == null)
                {
                    Utils.ShowHudMessage("No valid settings found, restoring defaults");
                    InitializeDefaults();
                    Save();
                    return;
                }

                ShowPathfinderMessages = settingsData.ShowPathfinderMessages;

                // Open
                if (settingsData.OpenSetting != null) 
                { 
                    OpenSetting = settingsData.OpenSetting; 
                }
                // Closed
                if (settingsData.ClosedSetting != null) 
                { 
                    ClosedSetting = settingsData.ClosedSetting; 
                }
                // OpenReverse
                if (settingsData.OpenReverseSetting != null) 
                { 
                    OpenReverseSetting = settingsData.OpenReverseSetting; 
                }
                // ClosedReverse
                if (settingsData.ClosedReverseSetting != null) 
                { 
                    ClosedReverseSetting = settingsData.ClosedReverseSetting; 
                }
                // NonLeaf
                if (settingsData.NonLeafSetting != null) 
                { 
                    NonLeafSetting = settingsData.NonLeafSetting; 
                }
                // Unexplored
                if (settingsData.UnexploredSetting != null) 
                { 
                    UnexploredSetting = settingsData.UnexploredSetting; 
                }
                // Occupied
                if (settingsData.OccupiedSetting != null) 
                { 
                    OccupiedSetting = settingsData.OccupiedSetting; 
                }
                // Meet
                if (settingsData.MeetSetting != null) 
                { 
                    MeetSetting = settingsData.MeetSetting; 
                }

                // DynamicObstacles
                if (settingsData.DynamicObstaclesSetting != null) 
                {
                    DynamicObstaclesSetting = settingsData.DynamicObstaclesSetting;
                }

                // MaxNodesPerFrame
                MaxNodesPerFrame = MathHelper.Clamp(settingsData.MaxNodesPerFrame, 1, 100);

                // ExploreCostFactor
                ExploreCostFactor = MathHelper.Clamp(settingsData.ExploreCostFactor, 0.0, 1000.0);

                // AltitudeCostFactor
                AltitudeCostFactor = MathHelper.Clamp(settingsData.AltitudeCostFactor, 0.0, 1000.0);

                // MaxPathOptimizationSteps
                MaxPathOptimizationSteps = MathHelper.Clamp(settingsData.MaxPathOptimizationSteps, 0, 10000);

                // RenderOctants
                RenderOctants = settingsData.RenderOctants;

                // RenderPath
                RenderPath = settingsData.RenderPath;

                // RenderDynamicObstacles
                RenderDynamicObstacles = settingsData.RenderDynamicObstacles;

                // PathRenderThickness
                PathRenderThickness = MathHelper.Clamp(settingsData.PathRenderThickness, 0.01f, 1.0f);
                
                // MaxRootSize
                MaxRootSize = MathHelper.Clamp(settingsData.MaxRootSize, 10.0, 10000.0);

                // MinRootSize
                MinRootSize = MathHelper.Clamp(settingsData.MinRootSize, 10.0, 10000.0);

                // MaxDPRRootSize
                MaxDPRRootSize = MathHelper.Clamp(settingsData.MaxDPRRootSize, 10.0, 10000.0);

                // MinDPRRootSize
                MinDPRRootSize = MathHelper.Clamp(settingsData.MinDPRRootSize, 10.0, 10000.0);

                // DPRMinAltitude
                DPRMinAltitude = MathHelper.Clamp(settingsData.DPRMinAltitude, 0.0, 10000.0);

                // DPRStepSize
                DPRStepSize = MathHelper.Clamp(settingsData.DPRStepSize, 0.0, 10000.0);
            }
            catch (Exception ex)
            {
                Utils.ShowHudMessage($"Error loading settings: {ex.Message}. Restoring defaults.");
                InitializeDefaults();
            }
        }

        public void Save()
        {
            try
            {
                var settingsData = new OctreeAStarSettingsData
                {
                    OpenSetting = OpenSetting,
                    ClosedSetting = ClosedSetting,
                    OpenReverseSetting = OpenReverseSetting,
                    ClosedReverseSetting = ClosedReverseSetting,
                    NonLeafSetting = NonLeafSetting,
                    UnexploredSetting = UnexploredSetting,
                    MeetSetting = MeetSetting,
                    OccupiedSetting = OccupiedSetting,
                    RenderOctants = RenderOctants,
                    RenderPath = RenderPath,
                    RenderDynamicObstacles = RenderDynamicObstacles,
                    MaxNodesPerFrame = MaxNodesPerFrame,
                    ExploreCostFactor = ExploreCostFactor,
                    ExploreCostFactorInTerrain = ExploreCostFactorInTerrain,
                    AltitudeCostFactor = AltitudeCostFactor,
                    MaxPathOptimizationSteps = MaxPathOptimizationSteps,
                    MaxRootSize = MaxRootSize,
                    MinRootSize = MinRootSize,
                    MaxDPRRootSize = MaxDPRRootSize,
                    MinDPRRootSize = MinDPRRootSize,
                    PathRenderThickness = PathRenderThickness,
                    ShowPathfinderMessages = ShowPathfinderMessages,
                    DPRMinAltitude = DPRMinAltitude,
                    DPRStepSize = DPRStepSize,
                };

                string xmlContent = MyAPIGateway.Utilities.SerializeToXML(settingsData);

                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(SETTINGS_FILE, typeof(OctreeAStarSettings)))
                {
                    writer.Write(xmlContent);
                }
            }
            catch (Exception ex)
            {
                Utils.ShowHudMessage($"Error saving settings: {ex.Message}");
            }
        }

        public void Update()
        {
            if (DateTime.UtcNow - _lastUpdateTime > TimeSpan.FromSeconds(UPDATE_DELAY_S))
            {
                Load();
                _lastUpdateTime = DateTime.UtcNow;
            }
        }
    }
}

