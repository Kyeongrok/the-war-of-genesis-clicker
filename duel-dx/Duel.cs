namespace DuelDx.Engine;

/// <summary>
/// 「일기토」 규칙을 담은 자체 셈 — 이 프로젝트만의 독립된 자산이다.
/// </summary>
/// <remarks>
/// 몸을 상·중·하 세 자리로 나누고 자리마다 체력을 따로 둔다. 한 자리라도 0이 되면
/// 진다. 자리를 골라 선제를 가리고(가위바위보), 이긴 쪽이 치고 진 쪽이 막는다 —
/// 상단은 웅크리면 막히고 뛰면 정통, 중단은 피하면 막히고 웅크리면 정통, 하단은
/// 뛰면 막히고 피하면 정통이다. 정통으로 맞히면 같은 쪽이 이어 친다.
/// </remarks>
public sealed class Duel
{
    public const int Zones = 3;
    public const int Full = 100;

    public static readonly string[] ZoneNames = ["상단", "중단", "하단"];
    public static readonly string[] GuardNames = ["뛴다", "피한다", "웅크린다"];

    public sealed class Fighter
    {
        public Fighter(string name, int body, int might, int sword, int luck,
                       int weapon, int armour)
        {
            Name = name;
            Might = might;
            Sword = sword;
            Luck = luck;
            Weapon = weapon;
            Armour = armour;

            int start = Math.Clamp(body, 1, Full);
            for (int zone = 0; zone < Zones; zone++) Health[zone] = start;
        }

        public string Name { get; }
        public int Might { get; }
        public int Sword { get; }
        public int Luck { get; }
        public int Weapon { get; }
        public int Armour { get; }

        public int[] Health { get; } = new int[Zones];

        public bool SpentBlow { get; internal set; }

        public bool Down => Health.Any(h => h <= 0);

        public int Thinnest
        {
            get
            {
                int at = 0;
                for (int zone = 1; zone < Zones; zone++)
                    if (Health[zone] < Health[at]) at = zone;
                return at;
            }
        }
    }

    public enum Step
    {
        First = 0,
        Strike = 1,
        Guard = 2,
    }

    private readonly Random _rng;

    public Duel(Fighter mine, Fighter theirs, Random rng)
    {
        Mine = mine;
        Theirs = theirs;
        _rng = rng;
    }

    public Fighter Mine { get; }
    public Fighter Theirs { get; }

    public Step Now { get; private set; } = Step.First;
    public int Moves { get; private set; }
    public bool? Over { get; private set; }
    public string Line { get; private set; } = "";
    public bool Telling { get; private set; }

    /// <summary>바로 앞 수에 지은 몸짓 — 0~2 가 치는 자리, 3~5 가 막는 몸짓이다.</summary>
    public int MyPose { get; private set; } = -1;
    public int TheirPose { get; private set; } = -1;

    private const int GuardPose = Zones;

    public bool CanBlow => Over == null && Now == Step.Strike && !Mine.SpentBlow;

    public void Play(int pick, bool blow = false)
    {
        if (Over != null) return;

        pick = Math.Clamp(pick, 0, Zones - 1);
        Telling = false;
        Moves++;

        if (blow && CanBlow) Mine.SpentBlow = true;
        else blow = false;

        switch (Now)
        {
            case Step.First: FirstBlood(pick); break;
            case Step.Strike: IStrike(pick, blow); break;
            default: TheyStrike(pick); break;
        }

        if (Mine.Down) Over = false;
        else if (Theirs.Down) Over = true;
    }

    private void FirstBlood(int mine)
    {
        int theirs = _rng.Next(Zones);
        bool won;

        if (mine == theirs)
        {
            int me, you;
            do
            {
                me = Mine.Might + Mine.Sword * 30 + _rng.Next(11);
                you = Theirs.Might + Theirs.Sword * 30 + _rng.Next(11);
            }
            while (me == you);
            won = me > you;
        }
        else
        {
            won = (mine + 1) % Zones == theirs;
        }

        MyPose = mine;
        TheirPose = theirs;
        Line = $"내 {ZoneNames[mine]}, 상대 {ZoneNames[theirs]} : " +
               (won ? "내가 앞섰다!" : "상대에게 앞을 내주었다.");
        Now = won ? Step.Strike : Step.Guard;
    }

    private void IStrike(int zone, bool blow)
    {
        int guard = GuardFor(Theirs.Thinnest);
        int grade = Grade(zone, guard);

        int hurt = grade == 2 ? 0 : Hurt(Mine, Theirs, blow, grade == 3);
        if (hurt > 0) Wound(Theirs, zone, hurt);

        MyPose = zone;
        TheirPose = GuardPose + guard;
        Line = $"{ZoneNames[zone]}{(blow ? "필살" : "공격")} vs {GuardNames[guard]} : " +
               Tale(grade, hurt, "상대");
        Pass(grade);
    }

    private void TheyStrike(int guard)
    {
        int zone = Mine.Thinnest;
        bool blow = false;

        if (_rng.Next(10) < 6)
        {
            if (!Theirs.SpentBlow && _rng.Next(2) == 1)
            {
                Theirs.SpentBlow = true;
                blow = true;
            }
        }
        else
        {
            zone = _rng.Next(Zones);
        }

        int grade = Grade(zone, guard);
        int hurt = grade == 2 ? 0 : Hurt(Theirs, Mine, blow, grade == 3);
        if (hurt > 0) Wound(Mine, zone, hurt);

        MyPose = GuardPose + guard;
        TheirPose = zone;
        Line = $"상대 {ZoneNames[zone]}{(blow ? "필살" : "공격")} vs {GuardNames[guard]} : " +
               Tale(grade, hurt, "나");
        Pass(grade);
    }

    private int GuardFor(int zone)
    {
        int roll = _rng.Next(10);
        int off = roll < 5 ? 0 : roll < 9 ? 1 : 2;

        var order = Enumerable.Range(0, Zones).OrderBy(guard => Grade(zone, guard)).ToArray();
        return order[off];
    }

    /// <summary>자리와 몸짓의 짝 — 4 정통, 3 스침, 2 막힘.</summary>
    public static int Grade(int zone, int guard) => zone switch
    {
        0 => 4 - guard,
        1 => guard == 0 ? 3 : guard * 2,
        _ => guard == 0 ? 2 : 5 - guard,
    };

    private int Hurt(Fighter hits, Fighter takes, bool blow, bool graze)
    {
        int hurt = (hits.Sword - takes.Sword) * 10
                   - takes.Might / 5
                   + hits.Might / 5
                   - takes.Armour
                   + hits.Weapon;

        hurt = hurt < 0
            ? _rng.Next(4) + _rng.Next(6) + 10
            : hurt + _rng.Next(6) + 10;

        if (blow)
        {
            hurt *= 2;
        }
        else if (hits.Sword + hits.Luck / 10 >= _rng.Next(1000))
        {
            Telling = true;
            hurt = hurt * 3 / 2;
        }

        if (graze) hurt /= 2;
        return hurt;
    }

    private static void Wound(Fighter who, int zone, int hurt) =>
        who.Health[zone] = Math.Max(0, who.Health[zone] - hurt);

    private void Pass(int grade)
    {
        if (grade == 4) return;
        Now = Now == Step.Strike ? Step.Guard : Step.Strike;
    }

    private string Tale(int grade, int hurt, string who) => grade switch
    {
        2 => "막혔다.",
        3 => $"스쳤다. {who} -{hurt}",
        _ => (Telling ? "회심의 일격!" : "정통으로 맞혔다!") + $" {who} -{hurt}",
    };
}
