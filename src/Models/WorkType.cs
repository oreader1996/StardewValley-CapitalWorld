using System;

namespace FarmHelper.Models
{
    [Flags]
    public enum WorkType
    {
        None = 0,
        Weeds = 1,
        Stone = 2,
        Wood = 4,
        Watering = 8,
        Collecting = 16,  // 收集东西
        All = 31  // 全干（所有任务）
    }
}
