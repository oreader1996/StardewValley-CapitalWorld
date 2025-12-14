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
            
            this.LoadNpcs();
            this.InitializeComponents();
        }

        private void LoadNpcs()
        {
             _availableNpcs = Utility.getAllCharacters()
                .Where(n => n.IsVillager && !n.IsMonster && n.Name != "FarmHelper")
                .OrderByDescending(n => Game1.player.getFriendshipLevelForNPC(n.Name))
                .ToList();
        }

        private void InitializeComponents()
        {
            // Close Button
            _closeButton = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + width - 48, yPositionOnScreen - 8, 48, 48),
                Game1.mouseCursors,
                new Rectangle(337, 494, 12, 12),
                4f);

            // Tasks (One Row)
            int taskY = yPositionOnScreen + 100;
            int startX = xPositionOnScreen + 50;
            int spacing = 180;

            _taskCheckboxes.Add(CreateCheckbox(_taskLabels[WorkType.Weeds], WorkType.Weeds, startX, taskY));
            _taskCheckboxes.Add(CreateCheckbox(_taskLabels[WorkType.Stone], WorkType.Stone, startX + spacing, taskY));
            _taskCheckboxes.Add(CreateCheckbox(_taskLabels[WorkType.Wood], WorkType.Wood, startX + spacing * 2, taskY));
            _taskCheckboxes.Add(CreateCheckbox(_taskLabels[WorkType.Watering], WorkType.Watering, startX + spacing * 3, taskY));

            // Scroll UI
            _listStartY = yPositionOnScreen + 180;
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

            int baseTaskCost = 0;
            if (_selectedTasks.HasFlag(WorkType.Weeds)) baseTaskCost += _config.CostPerTaskWeeds;
            if (_selectedTasks.HasFlag(WorkType.Stone)) baseTaskCost += _config.CostPerTaskStone;
            if (_selectedTasks.HasFlag(WorkType.Wood))  baseTaskCost += _config.CostPerTaskWood;
            if (_selectedTasks.HasFlag(WorkType.Watering)) baseTaskCost += _config.CostPerTaskWatering;

            int hiringCost = _config.BaseHiringCost + baseTaskCost;

            int hearts = Game1.player.getFriendshipHeartLevelForNPC(npc.Name);
            float discount = hearts * _config.FriendshipDiscountMultiplier; 
            if (discount > 0.8f) discount = 0.8f; 

            return (int)(hiringCost * (1.0f - discount));
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
                    if (_selectedTasks.HasFlag(type))
                        _selectedTasks &= ~type;
                    else
                        _selectedTasks |= type;
                    
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
                bool isHired = _workerService.IsHired(npc.Name);

                // 计算按钮位置
                int buttonStartX = xPositionOnScreen + width - 64 - BUTTON_WIDTH * 2 - 20; // 两个按钮，右边留空间给滚动条
                int hireButtonX = buttonStartX;
                int dismissButtonX = buttonStartX + BUTTON_WIDTH + 10;

                // Hire Button
                Rectangle hireBtnRect = new Rectangle(hireButtonX, rowY + 15, BUTTON_WIDTH, BUTTON_HEIGHT);
                if (hireBtnRect.Contains(x, y) && !isHired)
                {
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

                // Dismiss Button
                Rectangle dismissBtnRect = new Rectangle(dismissButtonX, rowY + 15, BUTTON_WIDTH, BUTTON_HEIGHT);
                if (dismissBtnRect.Contains(x, y) && isHired)
                {
                    _workerService.DismissWorker(npc.Name);
                    Game1.playSound("smallSelect");
                    return;
                }
            }
        }

        public override void leftClickHeld(int x, int y)
        {
            base.leftClickHeld(x, y);
            if (_scrolling)
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
                int rowY = _listStartY + (i * ITEM_HEIGHT) + (i * ROW_PADDING);
                bool isHired = _workerService.IsHired(npc.Name);
                int cost = CalculateCostForNpc(npc);
                bool canAfford = Game1.player.Money >= cost;
                bool canHire = cost > 0 && canAfford && !isHired && _selectedTasks != WorkType.None;

                // Row Background
                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 396, 15, 15), 
                    xPositionOnScreen + 50, rowY, width - 150, ITEM_HEIGHT, Color.White, 4f, false);

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

                // Cost Text - 第四列
                int costX = heartsX + 100;
                string costText = isHired ? _helper.Translation.Get("ui.dismiss") : $"{cost}g";
                Color costColor = isHired ? Color.Gray : (canAfford ? Color.DarkGoldenrod : Color.Red);
                b.DrawString(Game1.smallFont, costText, new Vector2(costX, rowY + 25), costColor);

                // Buttons - 第五列（最右边）
                int buttonStartX = xPositionOnScreen + width - 64 - BUTTON_WIDTH * 2 - 20;
                int hireButtonX = buttonStartX;
                int dismissButtonX = buttonStartX + BUTTON_WIDTH + 10;

                // Hire Button
                if (!isHired)
                {
                    Rectangle hireBtnRect = new Rectangle(hireButtonX, rowY + 15, BUTTON_WIDTH, BUTTON_HEIGHT);
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

                // Dismiss Button
                if (isHired)
                {
                    Rectangle dismissBtnRect = new Rectangle(dismissButtonX, rowY + 15, BUTTON_WIDTH, BUTTON_HEIGHT);
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
