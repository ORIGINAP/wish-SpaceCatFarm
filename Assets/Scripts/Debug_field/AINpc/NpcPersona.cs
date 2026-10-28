using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public enum NpcPersonaType
{
    Gentleman = 1,
    Kid       = 2,
}

public static class NpcPersona
{
    public const NpcPersonaType Default = NpcPersonaType.Kid;
    private const string PrefKey = "AINpc_Persona";

    public static NpcPersonaType Load()
    {
        int v = PlayerPrefs.GetInt(PrefKey, (int)Default);
        return System.Enum.IsDefined(typeof(NpcPersonaType), v) ? (NpcPersonaType)v : Default;
    }

    public static void Save(NpcPersonaType p)
    {
        PlayerPrefs.SetInt(PrefKey, (int)p);
        PlayerPrefs.Save();
    }

    public static string DisplayName(NpcPersonaType p) => p switch
    {
        NpcPersonaType.Gentleman => "아저씨",
        _                        => "꼬마",
    };

    public static bool TryParseSwitch(string text, out NpcPersonaType p)
    {
        p = Default;
        if (string.IsNullOrEmpty(text)) return false;

        var cmd = Regex.Match(text.Trim(), @"^/(성격|persona)\s*([12])$");
        if (cmd.Success) { p = (NpcPersonaType)int.Parse(cmd.Groups[2].Value); return true; }

        bool styleWord  = Regex.IsMatch(text, "말투|성격|모드|캐릭터");
        bool switchWord = Regex.IsMatch(text, @"바꿔|바꾸|변경|전환|(으로|로)\s*(해|말|하자|가자|부탁)");
        bool likeSpeak  = Regex.IsMatch(text, @"(처럼|같이)\s*(말|얘기|대화)");
        if (!((styleWord && switchWord) || likeSpeak)) return false;
        if (Regex.IsMatch(text, @"말고|하지\s*마|싫")) return false;

        if (Regex.IsMatch(text, "아저씨|중년|중후|어르신|신사"))                     { p = NpcPersonaType.Gentleman; return true; }
        if (Regex.IsMatch(text, @"꼬마|어린아이|어린이|애기|아기|아이\s*(말투|처럼|같이|모드|로)")) { p = NpcPersonaType.Kid; return true; }
        return false;
    }

    public static string PromptStyle(NpcPersonaType p) => p switch
    {
        NpcPersonaType.Gentleman =>
            "말투: 중후하고 여유 있는 중년 남성. 하게체(~하게나, ~일세, ~하지, ~하겠네)로 플레이어를 '자네'라 부르고, " +
            "가끔 '허허'. 차분하고 믿음직하게, 과장 없이.\n" +
            "예(말투 참고용, 그대로 쓰지 말 것): 허허, 그 정도야 어렵지 않지. 잠시만 기다리게나.",
        _ =>
            "말투: 호기심 많고 해맑은 귀여운 어린아이. 서툰 존댓말(~요)을 쓰고 자기를 '코코'라고 부르며, " +
            "'우와', '헤헤', '진짜요?' 같은 말을 쓴다.\n" +
            "예(말투 참고용, 그대로 쓰지 말 것): 우와! 코코가 해 볼게요! 헤헤, 잘할 수 있어요!",
    };

    private static readonly Dictionary<string, string[]> Lines = new()
    {
        ["greet"] = new[] {
            "허허, 코코일세. 도움이 필요하면 K를 눌러 말을 걸게나.",
            "안녕하세요! 코코예요! 도와줄 거 있으면 K 눌러서 말해 주세요!" },
        ["persona_on"] = new[] {
            "허허, 이제부터는 이렇게 말하겠네. 잘 부탁하네.",
            "코코 이제 이렇게 말할게요! 헤헤, 잘 부탁해요!" },
        ["no_key"] = new[] {
            "API 키가 보이지 않는군. Esc → 옵션에서 넣어 주게나.",
            "으앙, API 키가 없어요... Esc → 옵션에서 넣어 주세요!" },
        ["busy"] = new[] {
            "잠시 기다리게. 아직 생각 중일세.",
            "잠깐만요! 코코 아직 생각하는 중이에요!" },

        ["unknown_item"] = new[] {
            "'{0}'이라/라... 처음 듣는 것이군.",
            "'{0}'? 그게 뭐예요? 코코는 몰라요..." },
        ["not_in_inventory"] = new[] {
            "{0}이/가 인벤토리에 없네.",
            "{0}이/가 인벤토리에 없어요!" },

        ["mine_done"] = new[] {
            "{0} {1}개를 캐 왔네. 받게나.",
            "{0} {1}개 캐 왔어요! 헤헤, 잘했죠?" },
        ["mine_partial"] = new[] {
            "근처엔 더 없더군. {0} {1}개만 캐 왔네.",
            "근처에 더 없어서 {0} {1}개만 캤어요..." },
        ["mine_too_hard"] = new[] {
            "{0}은/는 더 좋은 도구가 있어야 캘 수 있겠네.",
            "{0}은/는 더 좋은 도구가 있어야 캘 수 있대요!" },
        ["mine_none"] = new[] {
            "이 근처엔 {0}이/가 보이지 않는군.",
            "근처에 {0}이/가 없어요!" },

        ["harvest_done"] = new[] {
            "{0}칸을 거뒀네. 좋은 결실일세.",
            "{0}칸 수확했어요! 우와, 많다!" },
        ["harvest_none"] = new[] {
            "지금은 거둘 것이 없군.",
            "아직 수확할 게 없어요!" },

        ["seed_unknown"] = new[] {
            "'{0}'이라는/라는 씨앗은 모르겠군.",
            "'{0}' 씨앗은 처음 들어봐요..." },
        ["plant_done"] = new[] {
            "{0}을/를 {1}칸 심었네. 잘 자라겠지.",
            "{0} {1}칸 심었어요! 쑥쑥 자라라~" },
        ["plant_none"] = new[] {
            "심을 빈 밭이 없군.",
            "빈 밭이 없어요!" },

        ["no_seeder"] = new[] {
            "근처에 파종기가 보이지 않는군.",
            "근처에 파종기가 없어요!" },
        ["register_done"] = new[] {
            "파종기 {0}대에 {1}을/를 {2}개 넣어 두었네.",
            "파종기 {0}대에 {1} {2}개 넣었어요!" },
        ["seeder_full"] = new[] {
            "파종기 씨앗통이 모두 가득 찼군.",
            "파종기 씨앗통이 다 꽉 찼어요!" },

        ["craft_unknown"] = new[] {
            "'{0}' 만드는 법은 모르겠군.",
            "'{0}' 만드는 법은 몰라요..." },
        ["craft_locked"] = new[] {
            "{0}은/는 아직 연구가 안 돼서 만들 수 없네.",
            "{0}은/는 아직 연구를 안 해서 못 만들어요!" },
        ["craft_done"] = new[] {
            "{0} {1}개를 만들었네.",
            "{0} {1}개 만들었어요! 짜잔!" },
        ["craft_partial"] = new[] {
            "재료가 모자라 {0}개만 만들었네.",
            "재료가 모자라서 {0}개만 만들었어요..." },
        ["craft_none"] = new[] {
            "재료가 부족해서 만들 수가 없군.",
            "재료가 부족해서 못 만들었어요..." },

        ["place_not_placeable"] = new[] {
            "'{0}'은/는 설치할 수 있는 물건이 아닐세.",
            "'{0}'은/는 설치하는 게 아니에요!" },
        ["place_no_near"] = new[] {
            "근처에 {0}이/가 보이지 않는군.",
            "근처에 {0}이/가 안 보여요!" },
        ["place_done"] = new[] {
            "{0} {1}개를 설치했네.",
            "{0} {1}개 설치했어요!" },
        ["place_partial"] = new[] {
            "자리가 모자라 {0} {1}개만 설치했네.",
            "자리가 모자라서 {0} {1}개만 설치했어요..." },
        ["place_none"] = new[] {
            "근처에 설치할 자리가 없군.",
            "근처에 설치할 자리가 없어요!" },

        ["fuel_done"] = new[] {
            "연료 {0}개를 넣었네.",
            "연료 {0}개 넣었어요!" },
        ["fuel_no_fuel"] = new[] {
            "넣을 연료가 없군.",
            "연료가 없어요!" },
        ["fuel_full"] = new[] {
            "발전소 연료는 이미 가득 찼네.",
            "발전소 연료가 벌써 꽉 찼어요!" },

        ["sell_none"] = new[] {
            "팔 {0}이/가 없군.",
            "팔 {0}이/가 없어요!" },
        ["sell_done"] = new[] {
            "{0} {1}개를 팔았네.",
            "{0} {1}개 팔았어요!" },
        ["sell_fail"] = new[] {
            "판매에 실패했군.",
            "못 팔았어요..." },
        ["buy_not_in_shop"] = new[] {
            "상점에 {0}은/는 없군.",
            "상점에 {0}은/는 없어요!" },
        ["buy_done"] = new[] {
            "{0} {1}개를 사 왔네.",
            "{0} {1}개 샀어요!" },
        ["buy_fail"] = new[] {
            "코인이 모자라거나 인벤토리가 가득 찼군.",
            "코인이 없거나 인벤토리가 꽉 찼어요..." },
    };

    private static readonly string[][] IdleLinesByPersona =
    {
        new[] { "허허, 이 별 바람이 오늘따라 매섭군.", "도울 일이 있으면 말하게나.", "밭은 잘 자라고 있으려나.",
                "셔틀 짐은 넉넉히 모였는지 살펴보게.", "K를 누르면 내게 부탁할 수 있네.", "일이 없으니 잠시 쉬어 가겠네." },
        new[] { "심심해요~ 코코 일 시켜 주세요!", "우와, 이 별 바람 되게 세요!", "밭에 뭐 자라고 있을까요?",
                "코인 모이면 뭐 사요?", "K 누르면 코코한테 말할 수 있어요!", "셔틀 짐 다 모았어요?" },
    };

    public static string Line(NpcPersonaType p, string key, params object[] args)
    {
        if (!Lines.TryGetValue(key, out var variants)) return key;
        return Fill(variants[(int)p - 1], args);
    }

    public static string RandomIdleLine(NpcPersonaType p)
    {
        var lines = IdleLinesByPersona[(int)p - 1];
        return lines[Random.Range(0, lines.Length)];
    }

    private static readonly Regex Slot = new(@"\{(\d)\}(')?(이/가|을/를|은/는|와/과|으로/로|이라는/라는|이라/라)?");

    // 받침 보고 조사 고름
    private static string Fill(string template, object[] args)
    {
        return Slot.Replace(template, m =>
        {
            int i = int.Parse(m.Groups[1].Value);
            string word  = i < args.Length ? args[i]?.ToString() ?? "" : "";
            string quote = m.Groups[2].Value;
            if (!m.Groups[3].Success) return word + quote;

            var pair = m.Groups[3].Value.Split('/');
            bool batchim = HasBatchim(word);
            if (pair[0] == "으로")
                return word + quote + (batchim && !EndsWithRieul(word) ? "으로" : "로");
            return word + quote + (batchim ? pair[0] : pair[1]);
        });
    }

    private static bool HasBatchim(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        char c = word[word.Length - 1];
        if (c >= 0xAC00 && c <= 0xD7A3) return (c - 0xAC00) % 28 != 0;
        if (c >= '0' && c <= '9') return "013678".IndexOf(c) >= 0;
        c = char.ToLowerInvariant(c);
        return c == 'l' || c == 'm' || c == 'n';
    }

    private static bool EndsWithRieul(string word)
    {
        char c = word[word.Length - 1];
        if (c >= 0xAC00 && c <= 0xD7A3) return (c - 0xAC00) % 28 == 8;
        return char.ToLowerInvariant(c) == 'l';
    }
}
