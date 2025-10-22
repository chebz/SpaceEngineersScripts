using System.Text;

namespace ToolInfo
{
    // Credit to MoreEvents mod from zznty
    public interface IEventControllerEvent
    {
        void UpdateDetailedInfo(StringBuilder info, int slot, long entityId, float value);
    }
}
