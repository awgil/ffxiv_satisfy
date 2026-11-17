using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace Satisfy;

public static unsafe class EventFrameworkExtensions
{
    extension(EventFramework)
    {
        public static bool HasPendingSceneTasks => EventFramework.Instance()->EventSceneModule.TaskManager.Tasks.Count > 0;

        public static string PendingSceneTasks
        {
            get
            {
                ref var tasks = ref EventFramework.Instance()->EventSceneModule.TaskManager.Tasks;
                if (tasks.Count == 0)
                    return "none";
                var types = new List<string>();
                foreach (var task in tasks)
                    types.Add(task.Value != null ? $"{task.Value->Type}" : "null");
                return string.Join(",", types);
            }
        }
    }
}
