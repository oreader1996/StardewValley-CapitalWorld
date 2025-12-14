using System;
using System.Collections.Generic;
using System.Linq;
using FarmHelper.Config;
using FarmHelper.Models;
using FarmHelper.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace FarmHelper.UI
{
    public class HireMenu : IClickableMenu
    {
        private readonly IModHelper _helper;
        private readonly ModConfig _config;
        private readonly WorkerService _workerService;

        // Data
        private List<NPC> _availableNpcs = new();
        private WorkType _selectedTasks = WorkType.None;
        private Dictionary<WorkType, string> _taskLabels = new();

        // UI Components
        private List<ClickableComponent> _taskCheckboxes = new();
        private ClickableTextureComponent? _closeButton;
        
        // Scrolling
        private ClickableTextureComponent? _upArrow;
        private ClickableTextureComponent? _downArrow;
        private ClickableTextureComponent? _scrollBar;
        private Rectangle _scrollBarRunner;
        private int _currentItemIndex = 0; // Topmost item index
        private const int ITEMS_PER_PAGE = 4;
        private const int ITEM_HEIGHT = 80;
        private bool _scrolling = false;

        // Layout Constants
        private const int ROW_PADDING = 10;
        private const int PORTRAIT_SIZE = 64;
        private const int BUTTON_WIDTH = 100;
        private const int BUTTON_HEIGHT = 50;
        private int _listStartY;

        public HireMenu(IModHelper helper, ModConfig config, WorkerService workerService)
            : base(Game1.viewport.Width / 2 - 400, Game1.viewport.Height / 2 - 300, 800, 600, true)
        {
            _helper = helper;
            _config = config;
            _workerService = workerService;
            
            // 初始化任务标签
            _taskLabels[WorkType.Weeds] = _helper.Translation.Get("task.weeds");
            _taskLabels[WorkType.Stone] = _helper.Translation.Get("task.stone");
            _taskLabels[WorkType.Wood] = _helper.Translation.Get("task.wood");
            _taskLabels[WorkType.Watering] = _helper.Translation.Get("task.watering");
            _taskLabels[WorkType.Collecting] = _helper.Translation.Get("task.collecting");
            _taskLabels[WorkType.All] = _helper.Translation.Get("task.all");
            
            this.LoadNpcs();
            this.InitializeComponents();
        }

        private void LoadNpcs()
        {
             // 获取所有NPC，按名称去重，确保每个NPC只出现一次
             var allNpcs = Utility.getAllCharacters()
                .Where(n => n != null && n.IsVillager && !n.IsMonster && !string.IsNullOrEmpty(n.Name) && n.Name != "FarmHelper")
                .GroupBy(n => n.Name!)
                .Select(g => 
                {
                    // 如果这个NPC已雇佣，优先使用农场中的实例
                    var hiredNpc = Game1.getFarm().characters.FirstOrDefault(c => c != null && c.Name == g.Key);
                    return hiredNpc ?? g.First(); // 如果已雇佣就用农场实例，否则用第一个
                })
                .Where(n => n != null)
                .Distinct() // 确保没有重复
                .OrderByDescending(n => Game1.player.getFriendshipLevelForNPC(n.Name ?? ""))
                .ThenBy(n => _workerService.IsHired(n.Name ?? "") ? 0 : 1) // 已雇佣的排在前面
                .ToList();
             
             _availableNpcs = allNpcs;
        }

        private void InitializeComponents()
        {
            // Close Button
            _closeButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + width - 48, yPositionOnScreen - 8, 48, 48),
                Game1.mouseCursors,
                new Rectangle(337, 494, 12, 12),
                4f);

            // Task Title
            int taskTitleY = yPositionOnScreen + 80;
            
            // Tasks (Two Rows)
            int taskY = yPositionOnScreen + 120;
            int startX = xPositionOnScreen + 50;
            int spacing = 150; // 减小间距以容纳更多任务

            // First Row
            _taskCheckboxes.Add(CreateCheckbox(_taskLabels[WorkType.Weeds], WorkType.Weeds, startX, taskY));
            _taskCheckboxes.Add(CreateCheckbox(_taskLabels[WorkType.Stone], WorkType.Stone, startX + spacing, taskY));
            _taskCheckboxes.Add(CreateCheckbox(_taskLabels[WorkType.Wood], WorkType.Wood, startX + spacing * 2, taskY));
            _taskCheckboxes.Add(CreateCheckbox(_taskLabels[WorkType.Watering], WorkType.Watering, startX + spacing * 3, taskY));
            
            // Second Row
            int taskY2 = taskY + 50;
            _taskCheckboxes.Add(CreateCheckbox(_taskLabels[WorkType.Collecting], WorkType.Collecting, startX, taskY2));
            _taskCheckboxes.Add(CreateCheckbox(_taskLabels[WorkType.All], WorkType.All, startX + spacing, taskY2));

            // Scroll UI
            _listStartY = yPositionOnScreen + 240;
            int listHeight = ITEMS_PER_PAGE * ITEM_HEIGHT + (ITEMS_PER_PAGE * ROW_PADDING);
            
            _upArrow = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + width - 64, _listStartY - 64, 44, 48),
                Game1.mouseCursors,
                new Rectangle(421, 459, 11, 12),
                4f);
            
            _downArrow = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + width - 64, _listStartY + listHeight + 16, 44, 48),
                Game1.mouseCursors,
                new Rectangle(421, 472, 11, 12),
                4f);

            _scrollBarRunner = new Rectangle(_upArrow.bounds.X + 12, _upArrow.bounds.Y + _upArrow.bounds.Height + 4, 24, listHeight);
            _scrollBar = new ClickableTextureComponent(
                new Rectangle(_scrollBarRunner.X, _scrollBarRunner.Y, 24, 40),
                Game1.mouseCursors,
                new Rectangle(435, 463, 6, 10),
                4f);
        }

        private ClickableComponent CreateCheckbox(string label, WorkType type, int x, int y)
        {
            return new ClickableComponent(new Rectangle(x, y, 160, 48), label)
            {
                myID = (int)type,
                name = label  // 存储翻译后的标签
            };
        }

        private int CalculateCostForNpc(NPC npc)
        {
            if (_selectedTasks == WorkType.None) return 0;
            
            // 如果选择了"全干"，计算所有任务类型的费用
            WorkType tasksToCalculate = _selectedTasks;
            if (_selectedTasks.HasFlag(WorkType.All))
            {
                tasksToCalculate = WorkType.Weeds | WorkType.Stone | WorkType.Wood | WorkType.Watering | WorkType.Collecting;
            }

            int baseTaskCost = 0;
            if (tasksToCalculate.HasFlag(WorkType.Weeds)) baseTaskCost += _config.CostPerTaskWeeds;
            if (tasksToCalculate.HasFlag(WorkType.Stone)) baseTaskCost += _config.CostPerTaskStone;
            if (tasksToCalculate.HasFlag(WorkType.Wood))  baseTaskCost += _config.CostPerTaskWood;
            if (tasksToCalculate.HasFlag(WorkType.Watering)) baseTaskCost += _config.CostPerTaskWatering;
            if (tasksToCalculate.HasFlag(WorkType.Collecting)) baseTaskCost += _config.CostPerTaskWeeds; // 收集使用和杂草相同的价格

            int hiringCost = _config.BaseHiringCost + baseTaskCost;

            int hearts = Game1.player.getFriendshipHeartLevelForNPC(npc.Name);
            float discount = hearts * _config.FriendshipDiscountMultiplier; 
            if (discount > 0.8f) discount = 0.8f; 

            return (int)(hiringCost * (1.0f - discount));
        }
        
        private bool IsNpcBusy(NPC npc)
        {
            if (npc == null || string.IsNullOrEmpty(npc.Name)) return false;
            
            // 检查NPC是否在任务中（比如罗宾在修房子）
            // 检查是否有正在进行的建筑任务
            if (Game1.player.team?.buildLock?.IsLocked() == true)
            {
                // 检查是否是建筑相关的NPC
                if (npc.Name == "Robin" || npc.Name == "Demetrius" || npc.Name == "Maru")
                {
                    // 检查是否有正在建造的建筑
                    var farm = Game1.getFarm();
                    if (farm?.buildings != null)
                    {
                        foreach (var building in farm.buildings)
                        {
                            if (building != null && building.daysOfConstructionLeft.Value > 0)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            
            // 检查是否有特殊订单涉及这个NPC
            if (Game1.player.team?.specialOrders != null)
            {
                foreach (var order in Game1.player.team.specialOrders)
                {
                    if (order != null && order.requester.Value == npc.Name)
                    {
                        // 检查订单是否未完成（不是完成状态）
                        // questState.Value 返回 SpecialOrderStatus 枚举类型
                        var questState = order.questState.Value;
                        // 将枚举转换为整数进行比较：0=NotStarted, 1=InProgress, 2=Complete
                        // 2 表示完成状态，如果不是完成状态，说明NPC正在执行任务
                        if ((int)questState != 2) // 2 = Complete
                        {
                            return true;
                        }
                    }
                }
            }
            
            return false;
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            if (_closeButton != null && _closeButton.containsPoint(x, y))
            {
                Game1.exitActiveMenu();
                return;
            }

            // Tasks
            foreach (var checkbox in _taskCheckboxes)
            {
                if (checkbox.containsPoint(x, y))
                {
                    WorkType type = (WorkType)checkbox.myID;
                    
                    // 如果选择了"全干"，清除其他所有任务
                    if (type == WorkType.All)
                    {
                        if (_selectedTasks.HasFlag(WorkType.All))
                            _selectedTasks = WorkType.None;
                        else
                            _selectedTasks = WorkType.All;
                    }
                    else
                    {
                        // 如果当前选择了"全干"，选择其他任务时清除"全干"
                        if (_selectedTasks.HasFlag(WorkType.All))
                            _selectedTasks = WorkType.None;
                        
                        if (_selectedTasks.HasFlag(type))
                            _selectedTasks &= ~type;
                        else
                            _selectedTasks |= type;
                    }
                    
                    Game1.playSound("drumkit6");
                    return; // Task changed, no need to check other clicks
                }
            }

            // Scroll
            if (_upArrow != null && _upArrow.containsPoint(x, y)) Scroll(-1);
            if (_downArrow != null && _downArrow.containsPoint(x, y)) Scroll(1);
            if (_scrollBarRunner.Contains(x, y)) _scrolling = true;

            // Worker List Clicks (Hire/Dismiss Buttons)
            for (int i = 0; i < ITEMS_PER_PAGE; i++)
            {
                int index = _currentItemIndex + i;
                if (index >= _availableNpcs.Count) break;

                int rowY = _listStartY + (i * ITEM_HEIGHT) + (i * ROW_PADDING);
                NPC npc = _availableNpcs[index];
                if (npc == null || string.IsNullOrEmpty(npc.Name)) continue;
                bool isHired = _workerService.IsHired(npc.Name);

                // 计算按钮位置（只有一个按钮位置）
                int buttonX = xPositionOnScreen + width - 64 - BUTTON_WIDTH - 10;

                // Hire Button
                if (!isHired)
                {
                    Rectangle hireBtnRect = new Rectangle(buttonX, rowY + 15, BUTTON_WIDTH, BUTTON_HEIGHT);
                    if (hireBtnRect.Contains(x, y))
                    {
                        // 检查NPC是否在任务中
                        if (IsNpcBusy(npc))
                        {
                            Game1.addHUDMessage(new HUDMessage(_helper.Translation.Get("message.npc_busy", new { name = npc.displayName }), 3));
                            Game1.playSound("cancel");
                            return;
                        }
                        
                        int cost = CalculateCostForNpc(npc);
                        if (cost > 0 && _selectedTasks != WorkType.None)
                        {
                            _workerService.HireWorker(npc, _selectedTasks, cost);
                            Game1.playSound("smallSelect");
                        }
                        else
                        {
                            Game1.playSound("cancel");
                        }
                        return;
                    }
                }

                // Dismiss Button
                if (isHired)
                {
                    Rectangle dismissBtnRect = new Rectangle(buttonX, rowY + 15, BUTTON_WIDTH, BUTTON_HEIGHT);
                    if (dismissBtnRect.Contains(x, y))
                    {
                        if (!string.IsNullOrEmpty(npc.Name))
                        {
                            _workerService.DismissWorker(npc.Name);
                            Game1.playSound("smallSelect");
                        }
                        return;
                    }
                }
            }
        }

        public override void leftClickHeld(int x, int y)
        {
            base.leftClickHeld(x, y);
            if (_scrolling && _scrollBar != null)
            {
                int totalHeight = _scrollBarRunner.Height - _scrollBar.bounds.Height;
                if (totalHeight <= 0) return;

                int relativeY = y - _scrollBarRunner.Y;
                float percentage = MathHelper.Clamp((float)relativeY / totalHeight, 0f, 1f);
                
                int totalItems = _availableNpcs.Count - ITEMS_PER_PAGE;
                if (totalItems > 0)
                {
                    _currentItemIndex = (int)(percentage * totalItems);
                    UpdateScrollBar();
                }
            }
        }

        public override void releaseLeftClick(int x, int y)
        {
            base.releaseLeftClick(x, y);
            _scrolling = false;
        }

        public override void receiveScrollWheelAction(int direction)
        {
            base.receiveScrollWheelAction(direction);
            Scroll(direction > 0 ? -1 : 1);
        }

        private void Scroll(int direction)
        {
            _currentItemIndex += direction;
            if (_currentItemIndex < 0) _currentItemIndex = 0;
            int max = Math.Max(0, _availableNpcs.Count - ITEMS_PER_PAGE);
            if (_currentItemIndex > max) _currentItemIndex = max;
            
            Game1.playSound("shwip");
            UpdateScrollBar();
        }

        private void UpdateScrollBar()
        {
            if (_scrollBar == null) return;
            
            int totalItems = Math.Max(1, _availableNpcs.Count - ITEMS_PER_PAGE);
            float percentage = (float)_currentItemIndex / totalItems;
            
            int totalHeight = _scrollBarRunner.Height - _scrollBar.bounds.Height;
            _scrollBar.bounds.Y = _scrollBarRunner.Y + (int)(percentage * totalHeight);
        }

        public override void draw(SpriteBatch b)
        {
            // Dim background
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

            // Main box
            Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, width, height, false, true);

            // Title
            string title = _helper.Translation.Get("ui.title");
            Utility.drawTextWithShadow(b, title, Game1.dialogueFont, 
                new Vector2(xPositionOnScreen + width / 2 - Game1.dialogueFont.MeasureString(title).X / 2, yPositionOnScreen + 30), 
                Game1.textColor);
            
            // Task Title
            string taskTitle = _helper.Translation.Get("ui.task_title");
            Utility.drawTextWithShadow(b, taskTitle, Game1.smallFont, 
                new Vector2(xPositionOnScreen + 50, yPositionOnScreen + 85), 
                Game1.textColor);

            // Tasks - 使用翻译后的标签
            foreach (var checkbox in _taskCheckboxes)
            {
                WorkType type = (WorkType)checkbox.myID;
                bool isChecked = _selectedTasks.HasFlag(type);
                
                // Draw box
                b.Draw(Game1.mouseCursors, new Vector2(checkbox.bounds.X, checkbox.bounds.Y), 
                    isChecked ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked, 
                    Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.4f);

                // Draw Text - 使用存储的翻译标签
                b.DrawString(Game1.smallFont, checkbox.name, new Vector2(checkbox.bounds.X + 50, checkbox.bounds.Y + 8), Game1.textColor);
            }
            
            // Separator
            b.Draw(Game1.staminaRect, new Rectangle(xPositionOnScreen + 40, _listStartY - 20, width - 80, 4), Color.SaddleBrown * 0.5f);

            // Scroll UI
            _upArrow?.draw(b);
            _downArrow?.draw(b);
            _scrollBar?.draw(b);

            // Worker List
            for (int i = 0; i < ITEMS_PER_PAGE; i++)
            {
                int index = _currentItemIndex + i;
                if (index >= _availableNpcs.Count) break;
                
                NPC npc = _availableNpcs[index];
                if (npc == null || string.IsNullOrEmpty(npc.Name)) continue;
                int rowY = _listStartY + (i * ITEM_HEIGHT) + (i * ROW_PADDING);
                bool isHired = _workerService.IsHired(npc.Name);
                int cost = CalculateCostForNpc(npc);
                bool canAfford = Game1.player.Money >= cost;
                bool canHire = cost > 0 && canAfford && !isHired && _selectedTasks != WorkType.None;

                // Row Background - 修复边框宽度，确保铺满（减去按钮和滚动条宽度）
                int rowWidth = width - 100 - 64; // 减去左右边距和滚动条宽度
                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 396, 15, 15), 
                    xPositionOnScreen + 50, rowY, rowWidth, ITEM_HEIGHT, Color.White, 4f, false);

                // Portrait (头像) - 第一列
                int portraitX = xPositionOnScreen + 60;
                int portraitY = rowY + 8;
                if (npc.Portrait != null)
                {
                    b.Draw(npc.Portrait, new Rectangle(portraitX, portraitY, PORTRAIT_SIZE, PORTRAIT_SIZE), 
                        new Rectangle(0, 0, 64, 64), Color.White);
                }
                else
                {
                    // 如果没有头像，绘制一个占位符
                    b.Draw(Game1.staminaRect, new Rectangle(portraitX, portraitY, PORTRAIT_SIZE, PORTRAIT_SIZE), Color.Gray);
                }

                // Name - 第二列（头像后面）
                int nameX = portraitX + PORTRAIT_SIZE + 15;
                Color nameColor = isHired ? Color.Green : Game1.textColor;
                b.DrawString(Game1.dialogueFont, npc.displayName, new Vector2(nameX, rowY + 20), nameColor);

                // Hearts - 第三列
                int heartsX = nameX + 200;
                int hearts = Game1.player.getFriendshipHeartLevelForNPC(npc.Name);
                if (hearts > 0) 
                {
                     // Draw a heart icon
                     b.Draw(Game1.mouseCursors, new Vector2(heartsX, rowY + 25), new Rectangle(211, 428, 7, 6), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.8f);
                     b.DrawString(Game1.smallFont, $"{hearts}", new Vector2(heartsX + 30, rowY + 25), Color.Red);
                }

                // Cost/Stamina Text - 第四列
                int costX = heartsX + 100;
                if (isHired)
                {
                    // 显示体力值
                    var workerInfo = _workerService.GetWorkerInfo(npc.Name);
                    if (workerInfo != null && workerInfo.Npc != null)
                    {
                        float currentStamina = workerInfo.Stamina;
                        float maxStamina = workerInfo.MaxStamina;
                        string staminaText = $"{(int)currentStamina}/{(int)maxStamina}";
                        Color staminaColor = currentStamina < maxStamina * 0.3f ? Color.Red : 
                                           (currentStamina < maxStamina * 0.6f ? Color.Orange : Color.Green);
                        b.DrawString(Game1.smallFont, staminaText, new Vector2(costX, rowY + 25), staminaColor);
                    }
                }
                else
                {
                    // 显示价格
                    string costText = $"{cost}g";
                    Color costColor = canAfford ? Color.DarkGoldenrod : Color.Red;
                    b.DrawString(Game1.smallFont, costText, new Vector2(costX, rowY + 25), costColor);
                }

                // Buttons - 第五列（最右边，只有一个按钮）
                int buttonX = xPositionOnScreen + width - 64 - BUTTON_WIDTH - 10;

                // Hire Button（未雇佣时显示）
                if (!isHired)
                {
                    Rectangle hireBtnRect = new Rectangle(buttonX, rowY + 15, BUTTON_WIDTH, BUTTON_HEIGHT);
                    IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(448, 262, 18, 16), 
                        hireBtnRect.X, hireBtnRect.Y, hireBtnRect.Width, hireBtnRect.Height, 
                        canHire ? Color.White : Color.Gray, 4f, false);
                    
                    string hireLabel = _helper.Translation.Get("ui.hire");
                    Vector2 hireLabelSize = Game1.smallFont.MeasureString(hireLabel);
                    Utility.drawTextWithShadow(b, hireLabel, Game1.smallFont, 
                        new Vector2(hireBtnRect.X + (hireBtnRect.Width - hireLabelSize.X) / 2, 
                            hireBtnRect.Y + (hireBtnRect.Height - hireLabelSize.Y) / 2), 
                        canHire ? Game1.textColor : Color.DimGray);
                }

                // Dismiss Button（已雇佣时显示）
                if (isHired)
                {
                    Rectangle dismissBtnRect = new Rectangle(buttonX, rowY + 15, BUTTON_WIDTH, BUTTON_HEIGHT);
                    IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(448, 262, 18, 16), 
                        dismissBtnRect.X, dismissBtnRect.Y, dismissBtnRect.Width, dismissBtnRect.Height, 
                        Color.White, 4f, false);
                    
                    string dismissLabel = _helper.Translation.Get("ui.dismiss");
                    Vector2 dismissLabelSize = Game1.smallFont.MeasureString(dismissLabel);
                    Utility.drawTextWithShadow(b, dismissLabel, Game1.smallFont, 
                        new Vector2(dismissBtnRect.X + (dismissBtnRect.Width - dismissLabelSize.X) / 2, 
                            dismissBtnRect.Y + (dismissBtnRect.Height - dismissLabelSize.Y) / 2), 
                        Game1.textColor);
                }
            }

            // Close Button
            _closeButton?.draw(b);
            drawMouse(b);
        }
    }
}
