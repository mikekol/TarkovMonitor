namespace TarkovMonitor;

public class Map
{
    public string id { get; set; }
    public string name { get; set; }
    public string nameId { get; set; }
    public string normalizedName { get; set; }
    public string scenePath { get; set; }
    public List<BossSpawn> bosses { get; set; } = new();
    public bool HasGoons()
    {
        List<string> goons = new() { "bossKnight", "followerBigPipe", "followerBirdEye" };
        return bosses.Any(spawn => goons.Contains(spawn.mob) || spawn.escorts.Any(e => goons.Contains(e.mob)));
    }
}

public class BossSpawn
{
    public string mob { get; set; }
    public List<BossEscort> escorts { get; set; } = new();
}

public class BossEscort
{
    public string mob { get; set; }
}
