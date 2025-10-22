using ProtoBuf;

namespace ToolInfo
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class ToolInfoSettings
    {
        [ProtoMember(1)]
        public bool EnableToolInfo = true;

        [ProtoMember(2)]
        public bool ShowDebugDraw = false;
    }
}
