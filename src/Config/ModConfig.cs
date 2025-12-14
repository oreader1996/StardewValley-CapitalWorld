namespace FarmHelper.Config
{
    public class ModConfig
    {
        public bool EnableSoundEffects { get; set; } = true;
        
        // Base costs per task type
        public int CostPerTaskWeeds { get; set; } = 30;
        public int CostPerTaskStone { get; set; } = 50;
        public int CostPerTaskWood { get; set; } = 100;
        public int CostPerTaskWatering { get; set; } = 150;
        
        public int BaseHiringCost { get; set; } = 100;
        public float FriendshipDiscountMultiplier { get; set; } = 0.05f; // 5% off per heart
    }
}
