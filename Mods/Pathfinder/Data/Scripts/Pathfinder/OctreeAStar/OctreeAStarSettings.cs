using VRageMath;
using System;
using Sandbox.ModAPI;

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
        public bool RenderOctants = true;
        public bool? RenderPath;
        public int MaxNodesPerFrame = 1;
        public double ExploreCostFactor = 0.75;
        public double ExploreCostFactorInTerrain = 10.0;
        public double AltitudeCostFactor = 1.0;
        public int MaxPathOptimizationSteps = 100;
        public double MinAltitude = 0.0;
        public double MaxRootSize = 1000.0;
        public double MinRootSize = 500.0;
        public float PathRenderThickness = 0.2f;
    }

    public class OctreeAStarSettings
    {
        private static OctreeAStarSettings _instance;
        private const string SETTINGS_FILE = "PathfinderSettings.xml";

        private const int DEFAULT_MAX_NODES_PER_FRAME = 1;
        private const double DEFAULT_EXPLORE_COST_FACTOR = 0.75;
        private const double DEFAULT_EXPLORE_COST_FACTOR_IN_TERRAIN = 10.0;
        private const double DEFAULT_ALTITUDE_COST_FACTOR = 1.0;
        private const int DEFAULT_MAX_PATH_OPTIMIZATION_STEPS = 100;
        private static readonly DebugRenderSetting DEFAULT_OPEN_SETTING = new DebugRenderSetting("Open", true, Color.Green, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_CLOSED_SETTING = new DebugRenderSetting("Closed", true, Color.Red, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_OPEN_REVERSE_SETTING = new DebugRenderSetting("OpenReverse", true, Color.Blue, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_CLOSED_REVERSE_SETTING = new DebugRenderSetting("ClosedReverse", true, Color.Orange, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_NON_LEAF_SETTING = new DebugRenderSetting("NonLeaf", true, Color.White, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_UNEXPLORED_SETTING = new DebugRenderSetting("Unexplored", true, Color.White, true, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_OCCUPIED_SETTING = new DebugRenderSetting("Occupied", true, Color.Red, false, 0.02f);
        private static readonly DebugRenderSetting DEFAULT_MEET_SETTING = new DebugRenderSetting("Meet", true, Color.Yellow, true, 0.02f);
        private const bool DEFAULT_RENDER_OCTANTS = false;
        private const bool DEFAULT_RENDER_PATH = false;
        private const double DEFAULT_MAX_ROOT_SIZE = 1000.0;
        private const double DEFAULT_MIN_ROOT_SIZE = 500.0;
        private const float DEFAULT_PATH_RENDER_THICKNESS = 0.2f;
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
        public bool RenderOctants = DEFAULT_RENDER_OCTANTS;
        public bool RenderPath = DEFAULT_RENDER_PATH;
        public int MaxNodesPerFrame = DEFAULT_MAX_NODES_PER_FRAME;
        public double ExploreCostFactor = DEFAULT_EXPLORE_COST_FACTOR;
        public double ExploreCostFactorInTerrain = DEFAULT_EXPLORE_COST_FACTOR_IN_TERRAIN;
        public double AltitudeCostFactor = DEFAULT_ALTITUDE_COST_FACTOR;
        public int MaxPathOptimizationSteps = DEFAULT_MAX_PATH_OPTIMIZATION_STEPS;
        public bool RenderPathEdges = false;
        public double MaxRootSize = DEFAULT_MAX_ROOT_SIZE;
        public double MinRootSize = DEFAULT_MIN_ROOT_SIZE;
        public float PathRenderThickness = DEFAULT_PATH_RENDER_THICKNESS;

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
            RenderOctants = DEFAULT_RENDER_OCTANTS;
            RenderPath = DEFAULT_RENDER_PATH;
            MaxNodesPerFrame = DEFAULT_MAX_NODES_PER_FRAME;
            ExploreCostFactor = DEFAULT_EXPLORE_COST_FACTOR;
            ExploreCostFactorInTerrain = DEFAULT_EXPLORE_COST_FACTOR_IN_TERRAIN;
            AltitudeCostFactor = DEFAULT_ALTITUDE_COST_FACTOR;
            MaxPathOptimizationSteps = DEFAULT_MAX_PATH_OPTIMIZATION_STEPS;
            MaxRootSize = DEFAULT_MAX_ROOT_SIZE;
            MinRootSize = DEFAULT_MIN_ROOT_SIZE;
            PathRenderThickness = DEFAULT_PATH_RENDER_THICKNESS;
        }


        public void Load()
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(SETTINGS_FILE, typeof(OctreeAStarSettings)))
                {
                    InitializeDefaults();
                    Save();
                    MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Created default settings file: {SETTINGS_FILE}");
                    return;
                }

                string xmlContent;
                using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(SETTINGS_FILE, typeof(OctreeAStarSettings)))
                {
                    xmlContent = reader.ReadToEnd();
                }

                if (string.IsNullOrEmpty(xmlContent))
                {
                    MyAPIGateway.Utilities.ShowMessage("Pathfinder", "Settings file is empty, restoring defaults");
                    InitializeDefaults();
                    Save();
                    return;
                }

                OctreeAStarSettingsData settingsData = MyAPIGateway.Utilities.SerializeFromXML<OctreeAStarSettingsData>(xmlContent);

                if (settingsData == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("Pathfinder", "No valid settings found, restoring defaults");
                    InitializeDefaults();
                    Save();
                    return;
                }

                bool wroteDefaults = false;

                if (settingsData.OpenSetting == null) 
                { 
                    OpenSetting = CloneSetting(DEFAULT_OPEN_SETTING); 
                    wroteDefaults = true; 
                } 
                else 
                { 
                    OpenSetting = settingsData.OpenSetting; 
                }
                if (settingsData.ClosedSetting == null) 
                { 
                    ClosedSetting = CloneSetting(DEFAULT_CLOSED_SETTING); 
                    wroteDefaults = true; 
                } 
                else 
                { 
                    ClosedSetting = settingsData.ClosedSetting; 
                }
                if (settingsData.OpenReverseSetting == null) 
                { 
                    OpenReverseSetting = CloneSetting(DEFAULT_OPEN_REVERSE_SETTING); 
                    wroteDefaults = true; 
                } 
                else 
                { 
                    OpenReverseSetting = settingsData.OpenReverseSetting; 
                }
                if (settingsData.ClosedReverseSetting == null) 
                { 
                    ClosedReverseSetting = CloneSetting(DEFAULT_CLOSED_REVERSE_SETTING); 
                    wroteDefaults = true; 
                } 
                else 
                { 
                    ClosedReverseSetting = settingsData.ClosedReverseSetting; 
                }
                if (settingsData.NonLeafSetting == null) 
                { 
                    NonLeafSetting = CloneSetting(DEFAULT_NON_LEAF_SETTING); 
                    wroteDefaults = true; 
                } 
                else 
                { 
                    NonLeafSetting = settingsData.NonLeafSetting; 
                }
                if (settingsData.UnexploredSetting == null) 
                { 
                    UnexploredSetting = CloneSetting(DEFAULT_UNEXPLORED_SETTING); 
                    wroteDefaults = true; 
                } 
                else 
                { 
                    UnexploredSetting = settingsData.UnexploredSetting; 
                }
                if (settingsData.OccupiedSetting == null) 
                { 
                    OccupiedSetting = CloneSetting(DEFAULT_OCCUPIED_SETTING); 
                    wroteDefaults = true; 
                } 
                else 
                { 
                    OccupiedSetting = settingsData.OccupiedSetting; 
                }
                if (settingsData.MeetSetting == null) 
                { 
                    MeetSetting = CloneSetting(DEFAULT_MEET_SETTING); 
                    wroteDefaults = true; 
                } 
                else
                { 
                    MeetSetting = settingsData.MeetSetting; 
                }

                MaxNodesPerFrame = settingsData.MaxNodesPerFrame > 0 ? settingsData.MaxNodesPerFrame : DEFAULT_MAX_NODES_PER_FRAME;
                if (settingsData.MaxNodesPerFrame <= 0) 
                {
                    wroteDefaults = true;
                }

                if (settingsData.ExploreCostFactor <= 0)
                {
                    ExploreCostFactor = DEFAULT_EXPLORE_COST_FACTOR;
                    wroteDefaults = true;
                }
                else
                {
                    ExploreCostFactor = settingsData.ExploreCostFactor;
                }

                if (settingsData.AltitudeCostFactor <= 0)
                {
                    AltitudeCostFactor = DEFAULT_ALTITUDE_COST_FACTOR;
                    wroteDefaults = true;
                }
                else
                {
                    AltitudeCostFactor = settingsData.AltitudeCostFactor;
                }

                if (settingsData.MaxPathOptimizationSteps <= 0)
                {
                    MaxPathOptimizationSteps = DEFAULT_MAX_PATH_OPTIMIZATION_STEPS;
                    wroteDefaults = true;
                }
                else
                {
                    MaxPathOptimizationSteps = settingsData.MaxPathOptimizationSteps;
                }

                RenderOctants = settingsData.RenderOctants;

                if (settingsData.RenderPath.HasValue)
                {
                    RenderPath = settingsData.RenderPath.Value;
                }
                else
                {
                    RenderPath = DEFAULT_RENDER_PATH;
                    wroteDefaults = true;
                }

                if (settingsData.MaxRootSize <= 0)
                {
                    MaxRootSize = DEFAULT_MAX_ROOT_SIZE;
                    wroteDefaults = true;
                }
                else
                {
                    MaxRootSize = settingsData.MaxRootSize;
                }

                if (settingsData.MinRootSize <= 0)
                {
                    MinRootSize = DEFAULT_MIN_ROOT_SIZE;
                    wroteDefaults = true;
                }
                else
                {
                    MinRootSize = settingsData.MinRootSize;
                }

                if (settingsData.PathRenderThickness <= 0f)
                {
                    PathRenderThickness = DEFAULT_PATH_RENDER_THICKNESS;
                    wroteDefaults = true;
                }
                else
                {
                    PathRenderThickness = settingsData.PathRenderThickness;
                }

                if (wroteDefaults)
                {
                    // Persist newly introduced defaults back to storage so future loads have them
                    Save();
                }
            }
            catch (Exception ex)
            {
                MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Error loading settings: {ex.Message}. Restoring defaults.");
                InitializeDefaults();
                Save();
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
                    MaxNodesPerFrame = MaxNodesPerFrame,
                    ExploreCostFactor = ExploreCostFactor,
                    ExploreCostFactorInTerrain = ExploreCostFactorInTerrain,
                    AltitudeCostFactor = AltitudeCostFactor,
                    MaxPathOptimizationSteps = MaxPathOptimizationSteps,
                    MaxRootSize = MaxRootSize,
                    MinRootSize = MinRootSize,
                    PathRenderThickness = PathRenderThickness,
                };

                string xmlContent = MyAPIGateway.Utilities.SerializeToXML(settingsData);

                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(SETTINGS_FILE, typeof(OctreeAStarSettings)))
                {
                    writer.Write(xmlContent);
                }
            }
            catch (Exception ex)
            {
                MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Error saving settings: {ex.Message}");
            }
        }

        public void Update()
        {
            Load();
        }
    }
}

