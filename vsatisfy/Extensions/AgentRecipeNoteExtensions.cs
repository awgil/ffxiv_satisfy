using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Satisfy;

public static unsafe class AgentRecipeNoteExtensions
{
    extension(AgentRecipeNote)
    {
        public static void Close() => AgentRecipeNote.Instance()->Hide();
    }
}
