// src/ModEntry.cs
using FarmHelper.Config;
using FarmHelper.Services;
using StardewModdingAPI;

namespace FarmHelper
{
    public class ModEntry : Mod
    {
        private ModConfig? _config;
        private WorkerService? _workerService;
        private DebugService? _debugService;

        public override void Entry(IModHelper helper)
        {
            // 1. 加载配置
            _config = helper.ReadConfig<ModConfig>();

            // 2. 初始化核心服务 (依赖注入)
            // 服务类在内部自行订阅所需事件
            _workerService = new WorkerService(helper, this.Monitor, _config);
            _debugService = new DebugService(helper, this.Monitor);

            // 3. 日志
            this.Monitor.Log("Farm Helper loaded successfully.", LogLevel.Info);
        }
    }
}
