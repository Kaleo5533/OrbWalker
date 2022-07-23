namespace OrbWalker.modules
{
    class ActivePlayerData
    {
        public class ChampionStats
        {
            public static float GetAttackRange()
            {
                return ApiStuff.GetActivePlayerData()["championStats"]["attackRange"].ToObject<float>();
            }
        }
    }
}