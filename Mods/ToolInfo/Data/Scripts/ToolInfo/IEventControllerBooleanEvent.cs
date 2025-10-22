using System.Text;

namespace ToolInfo
{
    // Credit to MoreEvents mod from zznty
    public interface IEventControllerBooleanEvent
    {
        void UpdateDetailedInfo(StringBuilder info, int slot, long entityId, bool value);
    }
}
