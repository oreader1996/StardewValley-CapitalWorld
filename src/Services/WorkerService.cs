using System;
using System.Collections.Generic;
using System.Linq;
using FarmHelper.Config;
using FarmHelper.Models;
using FarmHelper.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace FarmHelper.Services
{
    /// <summary>
    /// 处理工人雇佣、行为逻辑的核心服务
    /// </summary>
    public class WorkerService
    {
        private readonly IModHelper _helper;
        private readonly IMonitor _monitor;
        private readonly ModConfig _config;

        // 使用 PerScreen 确保分屏模式下状态隔离
        // 支持多个工人：使用字典存储每个NPC名称对应的工人实例和任务
        private readonly PerScreen<Dictionary<string, WorkerInfo>> _workers = new(() => new Dictionary<string, WorkerInfo>());
        private readonly PerScreen<Dictionary<string, Vector2?>> _workerTargets = new(() => new Dictionary<string, Vector2?>());

        public bool IsHired(string npcName) => _workers.Value.ContainsKey(npcName);
        public bool HasAnyWorker => _workers.Value.Count > 0;
        
        // 内部类：存储工人信息（公开供UI访问）
        public class WorkerInfo
        {
            public NPC? Npc { get; set; }
            public WorkType Tasks { get; set; }
            public float Stamina { get; set; }
            public float MaxStamina { get; set; }
            public bool IsResting { get; set; } = false; // 是否在休息恢复体力
        }
        
        // 体力相关常量
        private const float STAMINA_COST_PER_ACTION = 8f; // 每次工作消耗的体力
        private const float STAMINA_RECOVERY_RATE = 2f; // 每秒恢复的体力
        private const float REST_THRESHOLD = 50f; // 体力低于此值时开始休息
        
        /// <summary>
        /// 获取工人信息（供UI调用）
        /// </summary>
        public WorkerInfo? GetWorkerInfo(string npcName)
        {
            return _workers.Value.TryGetValue(npcName, out var info) ? info : null;
        }
        
        /// <summary>
        /// 获取主角的最大体力值
        /// </summary>
        private float GetPlayerMaxStamina()
        {
            return Game1.player.MaxStamina;
        }

        public WorkerService(IModHelper helper, IMonitor monitor, ModConfig config)
        {
            _helper = helper;
            _monitor = monitor;
            _config = config;

            // 自我管理事件订阅
            _helper.Events.Input.ButtonPressed += OnButtonPressed;
            _helper.Events.GameLoop.DayStarted += OnDayStarted;
            _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            _helper.Events.GameLoop.TimeChanged += OnTimeChanged;
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            // 清除所有工人
            foreach (var worker in _workers.Value.Values)
            {
                if (worker.Npc != null && Game1.getFarm().characters.Contains(worker.Npc))
                {
                    Game1.getFarm().characters.Remove(worker.Npc);
                }
            }
            _workers.Value.Clear();
            _workerTargets.Value.Clear();
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            if (Game1.timeOfDay >= 1800 && _workers.Value.Count > 0)
            {
                // 解雇所有工人
                var workerNames = _workers.Value.Keys.ToList();
                foreach (var name in workerNames)
                {
                    DismissWorker(name, _helper.Translation.Get("message.finish_work"));
                }
            }
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (Game1.activeClickableMenu != null) return;

            // F9 打开雇佣界面
            if (e.Button == SButton.F9)
            {
                Game1.activeClickableMenu = new HireMenu(_helper, _config, this);
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (_workers.Value.Count == 0 || !Context.IsWorldReady) return;

            // 每秒执行一次逻辑 (60 ticks)
            if (e.IsMultipleOf(60))
            {
                try
                {
                    // 为每个工人执行逻辑
                    var workerNames = _workers.Value.Keys.ToList();
                    foreach (var name in workerNames)
                    {
                        if (_workers.Value.ContainsKey(name))
                        {
                            DoWorkerLogic(name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _monitor.LogOnce($"Error in worker logic: {ex}", LogLevel.Error);
                    // 解雇出错的工人
                    var workerNames = _workers.Value.Keys.ToList();
                    foreach (var name in workerNames)
                    {
                        DismissWorker(name, "Worker encountered an error and left.");
                    }
                }
            }
        }

        /// <summary>
        /// 实际执行雇佣的方法，由 UI 调用
        /// </summary>
        public void HireWorker(NPC templateNpc, WorkType tasks, int totalCost)
        {
            // 检查是否已经雇佣
            if (IsHired(templateNpc.Name))
            {
                Game1.addHUDMessage(new HUDMessage(_helper.Translation.Get("message.worker_busy"), 2));
                return;
            }

            if (Game1.player.Money < totalCost)
            {
                Game1.addHUDMessage(new HUDMessage(_helper.Translation.Get("message.not_enough_money", new { cost = totalCost }), 3));
                return;
            }

            // 扣费
            Game1.player.Money -= totalCost;

            SpawnWorker(templateNpc, tasks);
            
            Game1.addHUDMessage(new HUDMessage(_helper.Translation.Get("message.hired", new { name = templateNpc.displayName, cost = totalCost }), 1));
        }

        /// <summary>
        /// 解雇指定工人
        /// </summary>
        public void DismissWorker(string npcName)
        {
            DismissWorker(npcName, _helper.Translation.Get("message.dismissed", new { name = npcName }));
        }

        private void SpawnWorker(NPC templateNpc, WorkType tasks)
        {
            Vector2 spawnPos = Game1.player.Position + new Vector2(64f, 0f);
            Farm farm = Game1.getFarm();

            if (Game1.player.currentLocation != farm)
            {
                spawnPos = new Vector2(64 * 64f, 15 * 64f);
            }

            // 使用模板 NPC 的外观
            AnimatedSprite sprite = new AnimatedSprite(templateNpc.Sprite.textureName.Value, 0, 16, 32);

            var npc = new NPC(sprite, spawnPos, 2, templateNpc.Name)
            {
                Portrait = templateNpc.Portrait,
                displayName = templateNpc.displayName,
                currentLocation = farm
            };

            // 获取主角的最大体力值
            float maxStamina = GetPlayerMaxStamina();
            
            // 存储工人信息
            _workers.Value[templateNpc.Name] = new WorkerInfo
            {
                Npc = npc,
                Tasks = tasks,
                Stamina = maxStamina,
                MaxStamina = maxStamina,
                IsResting = false
            };
            _workerTargets.Value[templateNpc.Name] = null;
            
            farm.addCharacter(npc);
            
            Game1.addHUDMessage(new HUDMessage(_helper.Translation.Get("message.start_work", new { name = npc.displayName }), 2));
            _monitor.Log($"Worker {npc.Name} spawned on farm with tasks: {tasks}", LogLevel.Debug);
        }

        private void DismissWorker(string npcName, string message)
        {
            if (_workers.Value.TryGetValue(npcName, out var workerInfo))
            {
                if (workerInfo.Npc != null && Game1.getFarm().characters.Contains(workerInfo.Npc))
                {
                    Game1.getFarm().characters.Remove(workerInfo.Npc);
                }
                _workers.Value.Remove(npcName);
                _workerTargets.Value.Remove(npcName);
                
                if (!string.IsNullOrEmpty(message))
                {
                    Game1.addHUDMessage(new HUDMessage(message, 2));
                }
            }
        }

        private void DoWorkerLogic(string npcName)
        {
            if (!_workers.Value.TryGetValue(npcName, out var workerInfo) || workerInfo.Npc == null) return;
            Farm farm = Game1.getFarm();

            // 体力恢复逻辑（每秒恢复）
            if (workerInfo.IsResting)
            {
                workerInfo.Stamina += STAMINA_RECOVERY_RATE;
                if (workerInfo.Stamina >= workerInfo.MaxStamina)
                {
                    workerInfo.Stamina = workerInfo.MaxStamina;
                    workerInfo.IsResting = false;
                }
                return; // 休息时不工作
            }

            // 检查体力是否耗尽
            if (workerInfo.Stamina <= REST_THRESHOLD)
            {
                workerInfo.IsResting = true;
                return; // 开始休息
            }

            if (!_workerTargets.Value.TryGetValue(npcName, out var targetTile) || targetTile == null)
            {
                // 获取所有其他工人已选择的目标，避免重叠
                var occupiedTiles = _workerTargets.Value
                    .Where(kvp => kvp.Key != npcName && kvp.Value.HasValue)
                    .Select(kvp => kvp.Value!.Value)  // HasValue 已检查，使用 ! 断言非空
                    .ToHashSet();
                
                targetTile = FindTargetFromPlayer(farm, workerInfo.Tasks, workerInfo.Npc.Tile, occupiedTiles);
                _workerTargets.Value[npcName] = targetTile;

                if (targetTile == null)
                {
                    if (Game1.ticks % 300 == 0 && _workers.Value.Count == 1)
                        Game1.addHUDMessage(new HUDMessage(_helper.Translation.Get("message.no_work"), 2));
                    return;
                }
            }

            Vector2 target = targetTile.Value;
            Vector2 current = workerInfo.Npc.Tile;

            // 距离检查
            if (Vector2.Distance(current, target) <= 1.5f)
            {
                bool isDone = PerformOneHit(farm, target, workerInfo.Tasks, workerInfo.Npc);

                if (isDone)
                {
                    // 消耗体力
                    workerInfo.Stamina -= STAMINA_COST_PER_ACTION;
                    if (workerInfo.Stamina < 0) workerInfo.Stamina = 0;
                    
                    _workerTargets.Value[npcName] = null;
                }
            }
            else
            {
                MoveWorkerTowards(workerInfo.Npc, current, target);
            }
        }

        private Vector2? FindTargetFromPlayer(Farm farm, WorkType tasks, Vector2 workerPosition, HashSet<Vector2> occupiedTiles)
        {
            var candidates = new List<Vector2>();
            
            // 如果选择了"全干"，则包括所有任务类型
            if (tasks.HasFlag(WorkType.All))
            {
                tasks = WorkType.Weeds | WorkType.Stone | WorkType.Wood | WorkType.Watering | WorkType.Collecting;
            }

            // 1. Objects (Weeds, Stones, Twigs, Collectibles)
            if (tasks.HasFlag(WorkType.Weeds) || tasks.HasFlag(WorkType.Stone) || tasks.HasFlag(WorkType.Wood) || tasks.HasFlag(WorkType.Collecting))
            {
                foreach (var pair in farm.Objects.Pairs)
                {
                    if (IsTargetObject(pair.Value, tasks) && !occupiedTiles.Contains(pair.Key))
                        candidates.Add(pair.Key);
                }
            }

            // 2. TerrainFeatures (Trees, Flooring/HoeDirt - filtering later)
            foreach (var pair in farm.terrainFeatures.Pairs)
            {
                 // Tree check
                 if (tasks.HasFlag(WorkType.Wood) && IsChoppableTree(pair.Value) && !occupiedTiles.Contains(pair.Key))
                 {
                     candidates.Add(pair.Key);
                     continue;
                 }

                 // Watering check
                 if (tasks.HasFlag(WorkType.Watering) && IsUnwateredCrop(pair.Value) && !occupiedTiles.Contains(pair.Key))
                 {
                     candidates.Add(pair.Key);
                 }
                 
                 // Collecting check - 收集成熟的作物
                 if (tasks.HasFlag(WorkType.Collecting) && IsCollectibleCrop(pair.Value) && !occupiedTiles.Contains(pair.Key))
                 {
                     candidates.Add(pair.Key);
                 }
            }

            if (candidates.Count == 0) return null;

            // 从工人当前位置找最近的目标，而不是从玩家位置
            return candidates
                .OrderBy(v => Vector2.DistanceSquared(v, workerPosition))
                .FirstOrDefault();
        }

        private bool IsTargetObject(StardewValley.Object obj, WorkType tasks)
        {
            if (obj == null) return false;
            
            bool isWeed = obj.IsWeeds();
            bool isStone = obj.Name != null && obj.Name.Contains("Stone");
            bool isTwig = obj.IsTwig();
            
            // 收集任务：收集可收集的物品（不是杂草、石头、树枝的物品）
            bool isCollectible = tasks.HasFlag(WorkType.Collecting) && 
                                !isWeed && !isStone && !isTwig && 
                                obj.CanBeGrabbed;

            if (tasks.HasFlag(WorkType.Weeds) && isWeed) return true;
            if (tasks.HasFlag(WorkType.Stone) && isStone) return true;
            if (tasks.HasFlag(WorkType.Wood) && isTwig) return true;
            if (isCollectible) return true;

            return false;
        }
        
        private bool IsCollectibleCrop(TerrainFeature tf)
        {
            if (tf is HoeDirt dirt && dirt.crop != null)
            {
                // 检查作物是否成熟
                if (dirt.crop.phaseDays != null && dirt.crop.phaseDays.Count > 0)
                {
                    return dirt.crop.currentPhase.Value >= dirt.crop.phaseDays.Count - 1 && 
                           dirt.crop.fullyGrown.Value;
                }
            }
            return false;
        }

        private bool IsChoppableTree(TerrainFeature tf)
        {
            if (tf is Tree tree)
            {
                if (tree.stump.Value || tree.growthStage.Value >= 5)
                    return true;
            }
            // Add FruitTree logic if needed, but usually we don't chop fruit trees automatically safely
            return false;
        }

        private bool IsUnwateredCrop(TerrainFeature tf)
        {
            if (tf is HoeDirt dirt)
            {
                // Has crop, not watered (state 0)
                if (dirt.crop != null && dirt.state.Value == 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void MoveWorkerTowards(NPC npc, Vector2 current, Vector2 target)
        {
            if (npc == null) return;

            Vector2 direction = target - current;
            if (direction != Vector2.Zero) direction.Normalize();

            if (Math.Abs(direction.X) > Math.Abs(direction.Y))
                npc.faceDirection(direction.X > 0 ? 1 : 3);
            else
                npc.faceDirection(direction.Y > 0 ? 2 : 0);

            npc.setTilePosition(new Point((int)target.X, (int)target.Y));

            var animFrames = new List<FarmerSprite.AnimationFrame>
            {
                new FarmerSprite.AnimationFrame(0, 100),
                new FarmerSprite.AnimationFrame(1, 100)
            };
            npc.Sprite.setCurrentAnimation(animFrames);
        }

        private bool PerformOneHit(Farm farm, Vector2 tile, WorkType tasks, NPC npc)
        {
            if (npc == null) return true;

            // 动画
            var animFrames = new List<FarmerSprite.AnimationFrame>
            {
                new FarmerSprite.AnimationFrame(166, 100),
                new FarmerSprite.AnimationFrame(167, 100)
            };
            
            // 如果是浇水，换动画? 暂时通用
            if (tasks.HasFlag(WorkType.Watering) && farm.terrainFeatures.ContainsKey(tile) && farm.terrainFeatures[tile] is HoeDirt)
            {
                 // Watering animation frame usually
                 // For simplified logic, keep standard or simple
            }
            
            npc.Sprite.setCurrentAnimation(animFrames);

            // 如果选择了"全干"，则包括所有任务类型
            if (tasks.HasFlag(WorkType.All))
            {
                tasks = WorkType.Weeds | WorkType.Stone | WorkType.Wood | WorkType.Watering | WorkType.Collecting;
            }

            // 1. Terrain Features (Trees, Crops)
            if (farm.terrainFeatures.TryGetValue(tile, out TerrainFeature tf))
            {
                // Collecting - 收集成熟的作物
                if (tf is HoeDirt dirt && tasks.HasFlag(WorkType.Collecting))
                {
                    if (dirt.crop != null && dirt.crop.phaseDays != null && dirt.crop.phaseDays.Count > 0 &&
                        dirt.crop.currentPhase.Value >= dirt.crop.phaseDays.Count - 1 && 
                        dirt.crop.fullyGrown.Value)
                    {
                        // 收获作物
                        if (dirt.crop.harvest((int)tile.X, (int)tile.Y, dirt))
                        {
                            if (_config.EnableSoundEffects) Game1.playSound("harvest");
                            return true;
                        }
                    }
                }
                
                // Watering
                if (tf is HoeDirt dirt2 && tasks.HasFlag(WorkType.Watering))
                {
                    if (dirt2.state.Value == 0) // if dry
                    {
                         WateringCan can = new WateringCan();
                         can.WaterLeft = 100;
                         dirt2.performToolAction(can, 0, tile);
                         if (_config.EnableSoundEffects) Game1.playSound("wateringCan");
                         return true; // Done
                    }
                    return true; // Already watered or invalid
                }

                // Trees
                if (tf is Tree tree && tasks.HasFlag(WorkType.Wood))
                {
                    return ProcessTree(farm, tile, tree);
                }
            }

            // 2. Objects
            if (farm.Objects.TryGetValue(tile, out StardewValley.Object obj))
            {
                return ProcessObject(farm, tile, obj, tasks);
            }

            return true;
        }

        private bool ProcessObject(Farm farm, Vector2 tile, StardewValley.Object obj, WorkType tasks)
        {
            // 收集任务：收集可收集的物品
            if (tasks.HasFlag(WorkType.Collecting) && obj.CanBeGrabbed && !obj.IsWeeds() && 
                (obj.Name == null || (!obj.Name.Contains("Stone") && !obj.IsTwig())))
            {
                // 收集物品到玩家背包
                if (Game1.player.addItemToInventoryBool(obj.getOne()))
                {
                    if (_config.EnableSoundEffects) Game1.playSound("pickUp");
                    farm.Objects.Remove(tile);
                    return true;
                }
                return false; // 背包满了
            }
            
            // 其他任务：清理杂草、石头、树枝
            Tool tool = (obj.Name != null && obj.Name.Contains("Stone")) ? (Tool)new Pickaxe() : (Tool)new Axe();
            tool.UpgradeLevel = 4;

            if (_config.EnableSoundEffects)
            {
                if (obj.Name != null && obj.Name.Contains("Stone")) Game1.playSound("hammer");
                else Game1.playSound("cut");
            }

            try { obj.performToolAction(tool); } catch { }

            if (farm.Objects.ContainsKey(tile))
            {
                Game1.createItemDebris(obj.getOne(), tile * 64f, -1);
                farm.Objects.Remove(tile);
            }
            return true;
        }

        private bool ProcessTree(Farm farm, Vector2 tile, Tree tree)
        {
            if (_config.EnableSoundEffects) Game1.playSound("axchop");

            bool success = false;
            try
            {
                Tool axe = new Axe();
                axe.UpgradeLevel = 4;
                tree.Location = farm;
                if (tree.performToolAction(axe, 0, tile)) success = true;
            }
            catch {}

            if (!success)
            {
                tree.health.Value -= 2.0f;
                tree.shake(tile, true);

                if (tree.health.Value <= 0)
                {
                    if (tree.stump.Value)
                    {
                        farm.terrainFeatures.Remove(tile);
                        Game1.createItemDebris(ItemRegistry.Create("(O)388", 1), tile * 64f, -1);
                        if (_config.EnableSoundEffects) Game1.playSound("stumpCrack");
                        return true;
                    }
                    else
                    {
                        tree.stump.Value = true;
                        tree.health.Value = 10;
                        Game1.createMultipleObjectDebris("(O)388", (int)tile.X, (int)tile.Y, 12, farm);
                        if (_config.EnableSoundEffects) Game1.playSound("treecrack");
                        return false; 
                    }
                }
            }

            return !farm.terrainFeatures.ContainsKey(tile);
        }
    }
}
