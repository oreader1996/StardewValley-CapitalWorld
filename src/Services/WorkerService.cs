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
        private readonly PerScreen<NPC?> _workerNpc = new();
        private readonly PerScreen<Vector2?> _currentTargetTile = new();
        private readonly PerScreen<bool> _isHiredToday = new();
        private readonly PerScreen<WorkType> _currentWorkTasks = new();

        public bool IsHired => _isHiredToday.Value;

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
            _isHiredToday.Value = false;
            _workerNpc.Value = null;
            _currentTargetTile.Value = null;
            _currentWorkTasks.Value = WorkType.None;
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            if (_isHiredToday.Value && Game1.timeOfDay >= 1800)
            {
                DismissWorker(_helper.Translation.Get("message.finish_work"));
            }
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (Game1.activeClickableMenu != null) return;

            // F9 打开雇佣界面
            if (e.Button == SButton.F9)
            {
                if (_isHiredToday.Value)
                {
                    Game1.addHUDMessage(new HUDMessage(_helper.Translation.Get("message.worker_busy"), 2));
                }
                else
                {
                    Game1.activeClickableMenu = new HireMenu(_helper, _config, this);
                }
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!_isHiredToday.Value || _workerNpc.Value == null || !Context.IsWorldReady) return;

            // 每秒执行一次逻辑 (60 ticks)
            if (e.IsMultipleOf(60))
            {
                try
                {
                    DoWorkerLogic();
                }
                catch (Exception ex)
                {
                    _monitor.LogOnce($"Error in worker logic: {ex}", LogLevel.Error);
                    DismissWorker("Worker encountered an error and left.");
                }
            }
        }

        /// <summary>
        /// 实际执行雇佣的方法，由 UI 调用
        /// </summary>
        public void HireWorker(NPC templateNpc, WorkType tasks, int totalCost)
        {
            if (Game1.player.Money < totalCost)
            {
                Game1.addHUDMessage(new HUDMessage(_helper.Translation.Get("message.not_enough_money", new { cost = totalCost }), 3));
                return;
            }

            // 扣费
            Game1.player.Money -= totalCost;
            _isHiredToday.Value = true;
            _currentWorkTasks.Value = tasks;

            SpawnWorker(templateNpc);
            
            Game1.exitActiveMenu();
            Game1.addHUDMessage(new HUDMessage(_helper.Translation.Get("message.hired", new { name = templateNpc.displayName, cost = totalCost }), 1));
        }

        private void SpawnWorker(NPC templateNpc)
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

            _workerNpc.Value = npc;
            farm.addCharacter(npc);
            
            Game1.addHUDMessage(new HUDMessage(_helper.Translation.Get("message.start_work", new { name = npc.displayName }), 2));
            _monitor.Log($"Worker {npc.Name} spawned on farm with tasks: {_currentWorkTasks.Value}", LogLevel.Debug);
        }

        private void DismissWorker(string message)
        {
            if (_workerNpc.Value != null)
            {
                Game1.getFarm().characters.Remove(_workerNpc.Value);
                _workerNpc.Value = null;
            }
            _isHiredToday.Value = false;
            _currentTargetTile.Value = null;
            Game1.addHUDMessage(new HUDMessage(message, 2));
        }

        private void DoWorkerLogic()
        {
            if (_workerNpc.Value == null) return;
            Farm farm = Game1.getFarm();

            if (_currentTargetTile.Value == null)
            {
                _currentTargetTile.Value = FindTargetFromPlayer(farm);

                if (_currentTargetTile.Value == null)
                {
                    if (Game1.ticks % 300 == 0)
                        Game1.addHUDMessage(new HUDMessage(_helper.Translation.Get("message.no_work"), 2));
                    return;
                }
            }

            Vector2 target = _currentTargetTile.Value.Value;
            Vector2 current = _workerNpc.Value.Tile;

            // 距离检查
            if (Vector2.Distance(current, target) <= 1.5f)
            {
                bool isDone = PerformOneHit(farm, target);

                if (isDone)
                {
                    _currentTargetTile.Value = null;
                }
            }
            else
            {
                MoveWorkerTowards(current, target);
            }
        }

        private Vector2? FindTargetFromPlayer(Farm farm)
        {
            WorkType tasks = _currentWorkTasks.Value;
            var candidates = new List<Vector2>();

            // 1. Objects (Weeds, Stones, Twigs)
            if (tasks.HasFlag(WorkType.Weeds) || tasks.HasFlag(WorkType.Stone) || tasks.HasFlag(WorkType.Wood))
            {
                foreach (var pair in farm.Objects.Pairs)
                {
                    if (IsTargetObject(pair.Value, tasks)) candidates.Add(pair.Key);
                }
            }

            // 2. TerrainFeatures (Trees, Flooring/HoeDirt - filtering later)
            foreach (var pair in farm.terrainFeatures.Pairs)
            {
                 // Tree check
                 if (tasks.HasFlag(WorkType.Wood) && IsChoppableTree(pair.Value)) 
                 {
                     candidates.Add(pair.Key);
                     continue;
                 }

                 // Watering check
                 if (tasks.HasFlag(WorkType.Watering) && IsUnwateredCrop(pair.Value))
                 {
                     candidates.Add(pair.Key);
                 }
            }

            if (candidates.Count == 0) return null;

            Vector2 playerTile = Game1.player.Tile;

            // 最近邻
            return candidates
                .OrderBy(v => Vector2.DistanceSquared(v, playerTile))
                .FirstOrDefault();
        }

        private bool IsTargetObject(StardewValley.Object obj, WorkType tasks)
        {
            if (obj == null) return false;
            
            bool isWeed = obj.IsWeeds();
            bool isStone = obj.Name != null && obj.Name.Contains("Stone");
            bool isTwig = obj.IsTwig();

            if (tasks.HasFlag(WorkType.Weeds) && isWeed) return true;
            if (tasks.HasFlag(WorkType.Stone) && isStone) return true;
            if (tasks.HasFlag(WorkType.Wood) && isTwig) return true;

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

        private void MoveWorkerTowards(Vector2 current, Vector2 target)
        {
            if (_workerNpc.Value == null) return;

            Vector2 direction = target - current;
            if (direction != Vector2.Zero) direction.Normalize();

            if (Math.Abs(direction.X) > Math.Abs(direction.Y))
                _workerNpc.Value.faceDirection(direction.X > 0 ? 1 : 3);
            else
                _workerNpc.Value.faceDirection(direction.Y > 0 ? 2 : 0);

            _workerNpc.Value.setTilePosition(new Point((int)target.X, (int)target.Y));

            var animFrames = new List<FarmerSprite.AnimationFrame>
            {
                new FarmerSprite.AnimationFrame(0, 100),
                new FarmerSprite.AnimationFrame(1, 100)
            };
            _workerNpc.Value.Sprite.setCurrentAnimation(animFrames);
        }

        private bool PerformOneHit(Farm farm, Vector2 tile)
        {
            if (_workerNpc.Value == null) return true;

            // 动画
            var animFrames = new List<FarmerSprite.AnimationFrame>
            {
                new FarmerSprite.AnimationFrame(166, 100),
                new FarmerSprite.AnimationFrame(167, 100)
            };
            
            // 如果是浇水，换动画? 暂时通用
            if (_currentWorkTasks.Value.HasFlag(WorkType.Watering) && farm.terrainFeatures.ContainsKey(tile) && farm.terrainFeatures[tile] is HoeDirt)
            {
                 // Watering animation frame usually
                 // For simplified logic, keep standard or simple
            }
            
            _workerNpc.Value.Sprite.setCurrentAnimation(animFrames);

            // 1. Terrain Features (Trees, Crops)
            if (farm.terrainFeatures.TryGetValue(tile, out TerrainFeature tf))
            {
                // Watering
                if (tf is HoeDirt dirt && _currentWorkTasks.Value.HasFlag(WorkType.Watering))
                {
                    if (dirt.state.Value == 0) // if dry
                    {
                         WateringCan can = new WateringCan();
                         can.WaterLeft = 100;
                         dirt.performToolAction(can, 0, tile);
                         if (_config.EnableSoundEffects) Game1.playSound("wateringCan");
                         return true; // Done
                    }
                    return true; // Already watered or invalid
                }

                // Trees
                if (tf is Tree tree && _currentWorkTasks.Value.HasFlag(WorkType.Wood))
                {
                    return ProcessTree(farm, tile, tree);
                }
            }

            // 2. Objects
            if (farm.Objects.TryGetValue(tile, out StardewValley.Object obj))
            {
                return ProcessObject(farm, tile, obj);
            }

            return true;
        }

        private bool ProcessObject(Farm farm, Vector2 tile, StardewValley.Object obj)
        {
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
