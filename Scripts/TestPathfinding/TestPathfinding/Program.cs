using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private CustomDataConnector _customDataConnector;
        private PathfindingNavigation _pathfindingNavigation;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);

            var navigation = new Navigation();
            var alignment = new Alignment();
            _pathfindingNavigation = new PathfindingNavigation();

            string errorMessage;
            if (!navigation.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Navigation Error: {errorMessage}");
            }

            if (!alignment.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Alignment Error: {errorMessage}");
            }

            if (!_pathfindingNavigation.Initialize(this, _customDataConnector, navigation, alignment, out errorMessage))
            {
                Echo($"TestPathfinding Error: {errorMessage}");
            }

            _customDataConnector.Load();
        }

        public void Save()
        {
            Storage = _customDataConnector.Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!string.IsNullOrEmpty(argument))
            {
                HandleCommand(argument);
            }

            _pathfindingNavigation.Execute();
            _customDataConnector.Update();
        }

        private void HandleCommand(string argument)
        {
            var command = argument.ToLower();

            switch (command)
            {
                case "start":
                    _pathfindingNavigation.Start();
                    Echo("Starting pathfinding navigation...");
                    break;

                case "stop":
                    _pathfindingNavigation.Stop();
                    Echo("Stopping pathfinding navigation...");
                    break;

                default:
                    Echo("Available commands: start, stop");
                    break;
            }
        }
    }
}
