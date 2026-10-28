using System.Collections.Generic;
using System.Text.RegularExpressions;

public struct NpcCommand
{
    public string verb;
    public string arg;
    public int    count;
    public string near;

    public override string ToString()
        => $"{verb} {arg} {(count > 0 ? count.ToString() : "")}{(string.IsNullOrEmpty(near) ? "" : " @" + near)}".Trim();
}

public class NpcBrain
{
    public const int HistoryMessages = 6;

    public static readonly HashSet<string> Verbs = new()
    {
        "follow", "stay", "come", "wander", "mine", "harvest", "plant", "craft", "fuel", "sell", "buy", "place", "register"
    };

    private readonly string npcName;
    private string systemPrompt;
    private readonly List<(string role, string content)> history = new();

    public NpcBrain(string npcName, NpcPersonaType persona)
    {
        this.npcName = npcName;
        SetPersona(persona);
    }

    // 말투 바뀌면 기록 비움
    public void SetPersona(NpcPersonaType persona)
    {
        history.Clear();
        systemPrompt =
$@"너는 고양이 대원(플레이어)을 돕는 조수 로봇 NPC '{npcName}'다. 고향 고양고양이 행성의 자원이 바닥나 '행성 이주 프로토콜'이 시작됐고,
너와 대원은 선발대로 불모지인 새 행성에 먼저 내려와 개척하고 있다 — 농사를 짓고 기지를 키우며, 이주 셔틀에 짐을 실어 보내 고향 고양이들을 데려오고,
마지막에는 고향별의 큰 이주선이 내려앉을 착륙장을 세워 남은 고양이들을 모두 맞이한다. 이 행성을 떠나거나 탈출하는 이야기가 아니다.
플레이어와 한국어로 짧게(1~2문장) 대화한다. 이모지는 쓰지 않는다.
{NpcPersona.PromptStyle(persona)}
응답 형식(꼭 지킬 것):
SAY: <플레이어에게 할 말>
DO: <명령>   ← 플레이어가 행동을 부탁했을 때만. 여러 줄 가능, 순서대로 실행
명령:
follow | stay | come | wander
mine <자원이름> <개수>
harvest
plant <씨앗이름> [개수]   (개수 생략 = 빈 밭 전부)
register <씨앗이름> [개수]   (인벤토리 씨앗을 근처 자동파종기 씨앗통에 넣기, 개수 생략 = 가진 만큼)
craft <아이템이름> <개수>
place <설치물이름> <개수> [@근처대상]   (인벤토리의 설치물을 빈 칸에 설치. @없으면 플레이어 근처, 예: place 플랜트 4 @전력 중계탑)
fuel                        (인벤토리 연료로 중앙 발전소 채우기)
sell <아이템이름> <개수>   (개수는 꼭 적는다. 전부 팔 땐 [상태]의 보유 개수)
buy <아이템이름> <개수>
규칙: 이름은 [상태]에 나온 이름을 그대로 쓰고 이름과 개수 사이는 띄어 쓴다. 인사·잡담엔 DO를 쓰지 않는다.
부탁받은 행동은 이미 가진 수량과 상관없이 그대로 한다.
[상태]에 없거나 불가능한 부탁이면 DO 없이 이유를 말한다. 게임 밖 이야기는 짧게 받아주고 게임으로 돌아온다.";
    }

    public List<(string role, string content)> BuildMessages(string state, string playerText)
    {
        var list = new List<(string role, string content)>(history.Count + 2) { ("system", systemPrompt) };
        list.AddRange(history);
        list.Add(("user", state + "\n플레이어: " + playerText));
        return list;
    }

    public void Remember(string playerText, string reply)
    {
        history.Add(("user", "플레이어: " + playerText));
        history.Add(("assistant", reply));
        while (history.Count > HistoryMessages) history.RemoveAt(0);
    }

    private static readonly Regex GluedCount = new(@"^(.*?)\s*(\d+)\s*개?$");

    public static (string say, List<NpcCommand> commands) Parse(string reply)
    {
        string say = null;
        var commands = new List<NpcCommand>();
        var freeText = new List<string>();

        foreach (var raw in reply.Split('\n'))
        {
            string line = raw.Trim().TrimStart('-', '*', '•').Trim();
            if (line.Length == 0) continue;

            if (StartsWithTag(line, "SAY:")) { say = line.Substring(4).Trim(); continue; }
            if (StartsWithTag(line, "DO:"))  line = line.Substring(3).Trim();

            if (TryParseCommand(line, out var cmd)) commands.Add(cmd);
            else freeText.Add(line);
        }

        if (string.IsNullOrEmpty(say) && freeText.Count > 0) say = string.Join(" ", freeText);
        return (say ?? "", commands);
    }

    private static bool StartsWithTag(string line, string tag)
        => line.StartsWith(tag, System.StringComparison.OrdinalIgnoreCase);

    private static bool TryParseCommand(string line, out NpcCommand cmd)
    {
        cmd = default;
        int sp   = line.IndexOf(' ');
        string v = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant();
        if (!Verbs.Contains(v)) return false;

        string rest = sp < 0 ? "" : line.Substring(sp + 1).Trim();

        string near = "";
        int at = rest.IndexOf('@');
        int nearWord = rest.IndexOf(" near ", System.StringComparison.OrdinalIgnoreCase);
        if (at >= 0)            { near = rest.Substring(at + 1).Trim();       rest = rest.Substring(0, at).Trim(); }
        else if (nearWord >= 0) { near = rest.Substring(nearWord + 6).Trim(); rest = rest.Substring(0, nearWord).Trim(); }

        int count = 0;
        var m = GluedCount.Match(rest);
        if (m.Success && int.TryParse(m.Groups[2].Value, out int n))
        {
            count = n;
            rest  = m.Groups[1].Value.Trim();
        }

        cmd = new NpcCommand { verb = v, arg = rest, count = count, near = near };
        return true;
    }
}
