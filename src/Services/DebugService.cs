using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace FarmHelper.Services
{
    /// <summary>
    /// 处理调试功能的逻辑 (Ctrl+S, Ctrl+L)
    /// </summary>
    public class DebugService
    {
        private readonly IModHelper _helper;
        private readonly IMonitor _monitor;
        
        // 用于存储 Ctrl+L 触发时的存档名
        private string? _pendingSaveToLoad;

        public DebugService(IModHelper helper, IMonitor monitor)
        {
            _helper = helper;
            _monitor = monitor;

            // 自我管理事件订阅
            _helper.Events.Input.ButtonPressed += OnButtonPressed;
            _helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // Debug: Ctrl + S to Save (Sleep)
            if (e.Button == SButton.S && (e.IsDown(SButton.LeftControl) || e.IsDown(SButton.RightControl)))
            {
                Game1.activeClickableMenu = new SaveGameMenu();
                _monitor.Log("Debug: Forced Save Menu (Sleep)", LogLevel.Info);
            }

            // Debug: Ctrl + L to Reload (Quit to Title -> Auto Load)
            if (e.Button == SButton.L && (e.IsDown(SButton.LeftControl) || e.IsDown(SButton.RightControl)))
            {
                // 1. Capture current save name
                _pendingSaveToLoad = Constants.SaveFolderName;
                
                // 2. Exit to title (which triggers ReturnedToTitle event)
                Game1.exitToTitle = true;
                
                _monitor.Log($"Debug: Reloading save '{_pendingSaveToLoad}'...", LogLevel.Info);
            }
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            // If we have a pending save to reload (triggered by Ctrl+L)
            if (!string.IsNullOrEmpty(_pendingSaveToLoad))
            {
                _monitor.Log($"Auto-reloading save: {_pendingSaveToLoad}", LogLevel.Info);
                
                try 
                {
                    SaveGame.Load(_pendingSaveToLoad);
                }
                catch (Exception ex)
                {
                    _monitor.Log($"Failed to auto-load save: {ex.Message}", LogLevel.Error);
                }
                
                _pendingSaveToLoad = null;
            }
        }
    }
}
