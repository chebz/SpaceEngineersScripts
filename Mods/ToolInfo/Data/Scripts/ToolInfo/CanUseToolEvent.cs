using System.Collections.Generic;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace ToolInfo
{
    [MyComponentBuilder(typeof(ObjectBuilderCanUseToolEvent))]
    [MyComponentType(typeof(CanUseToolEvent))]
    [MyEntityDependencyType(typeof(IMyEventControllerBlock))]
    public class CanUseToolEvent : MyEventProxyEntityComponent, IMyEventComponentWithGui, IEventControllerBooleanEvent
    {
        private readonly Dictionary<IMyShipToolBase, ToolInfoComponent> _subscribedTools = 
            new Dictionary<IMyShipToolBase, ToolInfoComponent>();
        private readonly Dictionary<IMyShipToolBase, bool> _cachedToolStates = 
            new Dictionary<IMyShipToolBase, bool>();

        private IMyEventControllerBlock Block => Entity as IMyEventControllerBlock;

        private readonly EventControllerGenericBooleanEvent<IMyShipToolBase> _eventGeneric;

        public CanUseToolEvent()
        {
            _eventGeneric = new EventControllerGenericBooleanEvent<IMyShipToolBase>
            {
                EventName = EventDisplayName,
                GetTriggerStateValueString = b => b.HasValue ? (b.Value ? "Can Use" : "Cannot Use") : "Unknown",
                GetTriggerStateValue = b => 
                {
                    bool state;
                    return _cachedToolStates.TryGetValue(b, out state) ? state : false;
                },
                GetTriggerStateKey = b => (IMyShipToolBase)b,
                SubscribeBlockEvent = b =>
                {
                    var tool = (IMyShipToolBase)b;
                    var glComponent = tool.Components.Get<MyGameLogicComponent>();
                    var component = glComponent as ToolInfoComponent;
                    if (component != null)
                    {
                        _subscribedTools[tool] = component;
                        component.CanUseStateChanged += OnToolStateChanged;
                    }
                },
                UnsubscribeBlockEvent = b =>
                {
                    var tool = (IMyShipToolBase)b;
                    ToolInfoComponent component;
                    if (_subscribedTools.TryGetValue(tool, out component))
                    {
                        component.CanUseStateChanged -= OnToolStateChanged;
                    }
                    _subscribedTools.Remove(tool);
                    _cachedToolStates.Remove(tool);
                }
            };

            if (!MyAPIGateway.Multiplayer.IsServer) return;

            MyAPIGateway.Entities.OnEntityRemove += EntitiesOnEntityRemove;
        }

        private void OnToolStateChanged(IMyShipToolBase tool, bool canTool)
        {
            _cachedToolStates[tool] = canTool;
            if (Block != null)
            {
                _eventGeneric.RaiseEvent(tool, Block, canTool);
            }
        }

        private void EntitiesOnEntityRemove(IMyEntity obj)
        {
            var tool = obj as IMyShipToolBase;
            if (tool != null && _subscribedTools.ContainsKey(tool))
            {
                _eventGeneric.RaiseEvent(tool, Block, false);
            }
        }

        public string YesNoToolbarYesDescription => "Can Use";
        public string YesNoToolbarNoDescription => "Cannot Use";


        public override string ComponentTypeDebugString => nameof(CanUseToolEvent);

        public void CreateTerminalInterfaceControls<T>() where T : IMyTerminalBlock
        {
        }

        public long UniqueSelectionId => 6844807;
        public MyStringId EventDisplayName => MyStringId.GetOrCompute("Can Use Tool");
        public bool IsSelected { get; set; }

        public void NotifyValuesChanged()
        {
            if (Block != null)
                _eventGeneric.NotifyValuesChanged(Block);
        }

        public bool IsBlockValidForList(IMyTerminalBlock block)
        {
            return block is IMyShipToolBase;
        }

        public void AddBlocks(List<IMyTerminalBlock> blocks)
        {
            if (Block != null)
                _eventGeneric.AddBlocks(Block, blocks);
        }

        public void RemoveBlocks(IEnumerable<IMyTerminalBlock> blocks)
        {
            _eventGeneric.RemoveBlocks(blocks);
        }

        public bool IsThresholdUsed => false;
        public bool IsConditionSelectionUsed => false;
        public bool IsBlocksListUsed => true;

        public void UpdateDetailedInfo(StringBuilder info, int slot, long entityId, bool value)
        {
            if (Block != null)
                _eventGeneric.UpdateDetailedInfo(info, 0f, slot, entityId, value);
        }
    }
}
