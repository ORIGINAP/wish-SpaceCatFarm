using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 퀘스트 체인 에셋 코드로 생성
public static class QuestChainBuilder
{
    private const string QuestFolder = "Assets/Scripts/Debug_field/Quest/Chain";
    private const string ChainPath   = "Assets/Resources/QuestChain.asset";
    private const string StagesPath  = "Assets/Resources/CoreStages.asset";

    private struct Obj
    {
        public QuestObjectiveType type;
        public string item;
        public string tech;
        public int amount;
    }

    private struct Rew
    {
        public string item;
        public int amount;
    }

    private class Q
    {
        public string id, name, desc, talk, hint, padHint, tip;
        public Obj[] objs;
        public Rew[] rewards = new Rew[0];
        public int coins;
    }

    private class Chapter
    {
        public string title, goal, intro;
        public List<Q> quests = new();
    }

    private static Obj Plant(string seed, int n)    => new() { type = QuestObjectiveType.PlantSeed, item = seed, amount = n };
    private static Obj Harvest(string crop, int n)  => new() { type = QuestObjectiveType.HarvestPlant, item = crop, amount = n };
    private static Obj Place(string item, int n)    => new() { type = QuestObjectiveType.PlaceTile, item = item, amount = n };
    private static Obj Craft(string item, int n)    => new() { type = QuestObjectiveType.CraftItem, item = item, amount = n };
    private static Obj Collect(string item, int n)  => new() { type = QuestObjectiveType.CollectItem, item = item, amount = n };
    private static Obj Residents(int n)             => new() { type = QuestObjectiveType.Residents, amount = n };
    private static Obj Level(int n)                 => new() { type = QuestObjectiveType.CoreLevel, amount = n };
    private static Obj Varieties(int n)             => new() { type = QuestObjectiveType.CropVarieties, amount = n };
    private static Obj AutoHarvest(int n)           => new() { type = QuestObjectiveType.AutoHarvest, item = "AutoHarvester", amount = n };
    private static Rew R(string item, int n)        => new() { item = item, amount = n };

    private struct Req
    {
        public CoreRequirementType type;
        public string item;
        public int amount;
        public bool power;
    }

    private static Req Facility(string item, int n, bool power) => new() { type = CoreRequirementType.Facility, item = item, amount = n, power = power };
    private static Req ReqVarieties(int n) => new() { type = CoreRequirementType.CropVarieties, amount = n };
    private static Req ReqResidents(int n) => new() { type = CoreRequirementType.Residents, amount = n };
    private static Req ReqCoins(int n)     => new() { type = CoreRequirementType.Coins, amount = n };

    // 문명 Lv.2부터 승급 조건
    private static readonly Req[][] Levels =
    {
        new[] { Facility("Item_BasicLab", 1, true), Facility("PlantTile", 10, false), ReqResidents(3), ReqCoins(300) },
        new[] { Facility("Item_Greenhouse", 1, true), Facility("Item_WaterTower", 1, true), Facility("Item_CatKitchen", 1, false),
                Facility("PlantTile", 20, true), ReqVarieties(4), ReqResidents(12), ReqCoins(2000) },
        new[] { Facility("Item_Factory", 1, true), Facility("AutoSeeder", 1, false), Facility("AutoHarvester", 1, false),
                Facility("PlantTile", 36, true), ReqVarieties(7), ReqResidents(30), ReqCoins(8000) },
        new[] { Facility("Item_Observatory", 1, true), Facility("Item_CommTower", 1, true), Facility("PlantTile", 60, true),
                ReqVarieties(10), ReqResidents(50), ReqCoins(30000) },
        new[] { Facility("Item_ControlTower", 1, true), Facility("Item_Habitat", 2, true), Facility("PlantTile", 80, true),
                ReqVarieties(12), ReqResidents(100), ReqCoins(120000) },
        new[] { Facility("Item_LaunchPad", 1, true), Facility("PlantTile", 100, true), ReqVarieties(14), ReqResidents(150), ReqCoins(300000) },
    };

    private static List<Chapter> Define()
    {
        var chapters = new List<Chapter>();

        var c1 = new Chapter { title = "착륙 기지", goal = "첫 농장을 일구고, 첫 셔틀로 고양이들을 맞아요!", intro = "선발대 무사히 착륙! 이주 프로토콜 첫 단계 — 이 불모지에 고양이들이 먹고 살 밭부터 일궈요." };
        c1.quests.Add(new Q
        {
            id = "C1_01_Plant", name = "첫 씨앗 심기",
            talk = "고양이들을 맞으려면 먹을 것부터! 우리 첫 밭에 블루스타를 심어 볼까요?",
            desc = "고양이들이 건너오려면 먹을 것이 있어야 해요. 착륙 기지 옆 밭에 블루스타 씨앗을 심어요.",
            hint = "1번 칸 씨앗을 고르고 빈 밭을 클릭 (누른 채 쓸면 여러 칸)",
            padHint = "LB/RB로 씨앗을 고르고 밭 앞에서 RT",
            tip = "블루스타는 20초면 자라요. 밭에 마우스를 올리면 남은 시간이 보여요.\n밭은 가운데 센터코어의 전기 범위 안에서 자라요.",
            objs = new[] { Plant("Item_Seed_BlueStar", 6) },
            rewards = new[] { R("Item_Seed_BlueStar", 4) },
        });
        c1.quests.Add(new Q
        {
            id = "C1_02_Expand", name = "밭 넓히기",
            talk = "밭이 넓을수록 많이 거둬요. 가져온 플랜트를 깔아서 밭을 넓혀 봐요!",
            desc = "밭이 많을수록 더 많이 거둬요. 가방에 있는 플랜트(밭)를 깔고 씨앗을 심어요.",
            hint = "3번 칸 플랜트를 고르고 빈 땅 클릭 → 씨앗 심기",
            padHint = "LB/RB로 플랜트를 고르고 RT로 설치 → 씨앗 심기",
            tip = "설치물은 커서를 따라다니는 초록 그림자 자리에 놓여요 (빨강 = 못 놓는 자리).\n잘못 놓았으면 우클릭을 꾹 누르면 해체돼 돌려받아요.",
            objs = new[] { Place("PlantTile", 10), Plant(null, 4) },
            coins = 50,
        });
        c1.quests.Add(new Q
        {
            id = "C1_03_Harvest", name = "첫 수확",
            talk = "와, 반짝반짝 다 자랐어요! 얼른 거둬들여요.",
            desc = "다 자란 작물은 반짝반짝! 거둬들여요.",
            hint = "다 자란 작물을 클릭 (무엇을 들고 있어도 돼요)",
            padHint = "다 자란 밭 앞에서 RT",
            tip = "수확하면 작물과 씨앗(가끔 2개)이 나와요. 빈 밭엔 바로 다시 심을 수 있어요.",
            objs = new[] { Harvest(null, 10) },
            rewards = new[] { R("Item_Seed_RedStar", 4), R("Item_Seed_YellowStar", 4) },
        });
        c1.quests.Add(new Q
        {
            id = "C1_04_Order", name = "첫 셔틀",
            talk = "착륙장의 셔틀에 블루스타를 실어 보내면, 고향별 고양이들을 태워 와요!",
            desc = "셔틀에 블루스타 5개를 실으면 고향별에 다녀오며 첫 고양이 가족을 태워 와요.",
            hint = "착륙장의 셔틀을 클릭하면 짐이 실려요 · 다 실으면 출발!",
            padHint = "셔틀 앞에서 A로 짐 싣기 · 다 실으면 출발!",
            tip = "셔틀 머리 위 말풍선이 실을 짐이에요. 다 실으면 짐값을 받고, 잠시 뒤 셔틀이 고양이들을 태우고 돌아와요.\n[셔틀](J) 창에서 짐과 태워 올 가족을 볼 수 있어요.",
            objs = new[] { Residents(3) },
            coins = 100,
        });
        c1.quests.Add(new Q
        {
            id = "C1_05_Stars", name = "별 삼총사",
            talk = "별 작물은 서로 붙어 있으면 더 많이 열린대요. 레드스타랑 옐로스타도 심어 봐요!",
            desc = "블루 · 레드 · 옐로스타를 서로 붙여 심으면 [별자리] 성향 덕분에 작물이 더 열려요!",
            hint = "레드스타 · 옐로스타 씨앗을 블루스타 옆에 심어요",
            padHint = "LB/RB로 레드 · 옐로스타 씨앗을 골라 블루스타 옆에 RT",
            tip = "작물마다 성향이 있어요. 밭에 마우스를 올리면 지금 받는 보너스가 보여요.",
            objs = new[] { Plant("Item_Seed_RedStar", 3), Plant("Item_Seed_YellowStar", 3) },
            coins = 50,
        });
        c1.quests.Add(new Q
        {
            id = "C1_06_Mine", name = "광석 캐기",
            talk = "건물을 지으려면 금속이 필요해요. 드릴로 구리랑 철 광석을 캐 와요!",
            desc = "건물을 지으려면 금속이 필요해요. 드릴로 구리와 철 광석을 캐요.",
            hint = "2번 칸 드릴을 들고 광석을 꾹 클릭 (가까이 가야 해요)",
            padHint = "LB/RB로 드릴을 고르고 광석 앞에서 RT를 꾹",
            tip = "구리는 주황빛, 철은 회색 바위예요. 화살표가 가까운 광석을 가리켜요.\n광석은 다시 생기지 않으니 필요한 만큼만 캐요.",
            objs = new[] { Collect("Item_Copper", 20), Collect("Item_iron", 20) },
        });
        c1.quests.Add(new Q
        {
            id = "C1_07_Smelter", name = "제련소 세우기",
            talk = "광석은 그대로는 못 써요. 제련소를 세워서 녹여 봐요!",
            desc = "광석은 그대로 못 써요. 제련소에서 녹여 주괴로 만들어요.",
            hint = "[제작](C) → 설비 탭 → 제련소 만들기 → 센터코어 곁에 설치",
            padHint = "십자키 ↑ 제작 → 설비 탭 → 제련소 → RT로 설치",
            tip = "제련소는 전기가 있어야 돌아가요 (센터코어 둘레 25칸). 설치물을 들면 전기 범위가 테두리로 보여요.",
            objs = new[] { Place("Item_Smelter", 1) },
        });
        c1.quests.Add(new Q
        {
            id = "C1_08_Ingot", name = "주괴 녹이기",
            talk = "제련소에 광석을 넣으면 반짝이는 주괴가 돼요!",
            desc = "제련소에 광석을 넣으면 3초마다 광석 2개가 주괴 1개로 녹아요.",
            hint = "광석을 든 채 제련소를 클릭하면 들어가요 · 다 녹으면 다시 클릭해 받기",
            padHint = "광석을 든 채 제련소 앞에서 A · 다 녹으면 A로 받기",
            tip = "한 번에 한 종류만 녹여요. 구리를 다 녹인 뒤 철을 넣어요.\n그냥 클릭하면 제련소 창이 열려 넣을 광석을 고를 수 있어요.",
            objs = new[] { Collect("Item_CopperIngot", 7), Collect("Item_IronIngot", 7) },
            coins = 50,
        });
        c1.quests.Add(new Q
        {
            id = "C1_09_Lab", name = "기초 연구소",
            talk = "연구소가 서면 우리 착륙 기지가 개척 캠프로 발전할 수 있어요!",
            desc = "연구소를 세우면 착륙 기지가 개척 캠프로 발전할 수 있어요.",
            hint = "[제작](C) → 재료 탭 → 합금, 문명 탭 → 기초 연구소 → 설치",
            padHint = "십자키 ↑ 제작 → 재료 탭 합금 · 문명 탭 기초 연구소 → RT로 설치",
            tip = "합금은 제련소 곁(5칸)에서 만들어요. 문명 시설은 전기가 들어와야 문명 조건에 세어져요.",
            objs = new[] { Craft("item_Alloy", 1), Place("Item_BasicLab", 1) },
        });
        c1.quests.Add(new Q
        {
            id = "C1_10_Civ", name = "개척 캠프로!",
            talk = "조건만 채우면 개척 캠프가 돼요. 위쪽 문명 알림판을 봐요!",
            desc = "센터코어의 조건을 모두 채우면 문명이 한 단계 발전해요.",
            hint = "위쪽 문명 알림판의 조건을 채워요 (밭 10칸 · 주민 3마리 · 300코인)",
            tip = "문명이 발전하면 새 씨앗 · 새 건물 · 새 요리가 열려요.",
            objs = new[] { Level(2) },
        });
        chapters.Add(c1);

        var c2 = new Chapter { title = "개척 캠프", goal = "요리와 새 작물로 주민을 늘려 농업 마을로!", intro = "첫 주민들이 왔어요! 요리와 새 작물로 더 많은 고양이를 맞을 준비를 해요." };
        c2.quests.Add(new Q
        {
            id = "C2_01_Kitchen", name = "고양이 주방",
            talk = "작물을 요리하면 훨씬 비싸게 팔려요. 우리도 주방을 지어 봐요!",
            desc = "작물로 요리하면 훨씬 비싸게 팔려요. 고양이 주방을 지어요.",
            hint = "[제작](C) → 농사 탭 → 고양이 주방 → 전기 범위 안에 설치",
            padHint = "십자키 ↑ 제작 → 농사 탭 → 고양이 주방 → RT로 설치",
            tip = "주방(2×2)엔 철 주괴 4 · 구리 주괴 3 · 합금 1이 들어가요.",
            objs = new[] { Place("Item_CatKitchen", 1) },
            rewards = new[] { R("Item_Seed_Catnip", 4) },
        });
        c2.quests.Add(new Q
        {
            id = "C2_02_Cook", name = "첫 요리: 별빛 잼",
            talk = "별 작물 세 가지면 별빛 잼을 만들 수 있어요. 셔틀 짐으로 보내면 고향 고양이들이 엄청 좋아할 거예요!",
            desc = "블루 · 레드 · 옐로스타를 하나씩 넣으면 별빛 잼이 돼요.",
            hint = "주방 클릭 → 별빛 잼 → [재료 넣기]",
            padHint = "주방 앞에서 A → 별빛 잼 → [재료 넣기]",
            tip = "요리는 잠시 걸려요. 다 되면 주방을 클릭해 받아요! 주방을 창고와 연결하면 재료를 알아서 가져가요.",
            objs = new[] { Craft("Item_StarJam", 1) },
            coins = 100,
        });
        c2.quests.Add(new Q
        {
            id = "C2_03_Radish", name = "달빛 무",
            talk = "새 씨앗이 도착했어요! 달빛 무는 밤에 쑥쑥 자란대요.",
            desc = "개척 캠프가 되면서 새 씨앗을 받았어요. 달빛 무는 밤에 두 배로 빨리 자라요.",
            hint = "가방(Tab)의 달빛 무 씨앗을 핫바로 옮겨 심고 거둬요",
            padHint = "Y로 가방을 열어 달빛 무 씨앗을 핫바로 → 심고 거두기",
            tip = "씨앗이 모자라면 [상점](B)에서 사요. 온실을 지을 때 달빛 무 3개가 필요해요.",
            objs = new[] { Plant("Item_Seed_MoonRadish", 3), Harvest("MoonRadish", 3) },
            rewards = new[] { R("Item_Seed_MoonRadish", 3) },
        });
        c2.quests.Add(new Q
        {
            id = "C2_04_Water", name = "급수탑",
            talk = "물을 주면 작물이 더 빨리 자라요. 밭 옆에 급수탑을 세워요!",
            desc = "급수탑 둘레 5칸의 밭은 물을 받아 1.25배 빨리 자라요 (츄르 옥수수는 2배!).",
            hint = "[제작](C) → 문명 탭 → 급수탑 → 밭 곁에 설치",
            padHint = "십자키 ↑ 제작 → 문명 탭 → 급수탑 → RT로 설치",
            tip = "물을 받은 밭은 흙이 파랗게 보여요.",
            objs = new[] { Place("Item_WaterTower", 1) },
            coins = 100,
        });
        c2.quests.Add(new Q
        {
            id = "C2_05_Greenhouse", name = "온실",
            talk = "온실은 작물을 빨리 키우고 폭풍에서도 지켜 줘요.",
            desc = "온실은 곁의 작물을 1.2배 빨리 키우고, 폭풍에서도 지켜 줘요.",
            hint = "[제작](C) → 문명 탭 → 온실 (달빛 무 3개 필요) → 설치",
            padHint = "십자키 ↑ 제작 → 문명 탭 → 온실 → RT로 설치",
            tip = "온실 재료: 플랜트 4 · 합금 2 · 구리 주괴 4 · 달빛 무 3.",
            objs = new[] { Place("Item_Greenhouse", 1) },
            coins = 150,
        });
        c2.quests.Add(new Q
        {
            id = "C2_05b_Fields", name = "밭 20칸",
            talk = "고양이가 늘수록 셔틀 짐도 커져요. 밭을 20칸까지 넓혀 둬요!",
            desc = "농업 마을이 되려면 전기가 들어오는 밭이 20칸 있어야 해요. 플랜트를 더 만들어 센터코어 둘레에 깔아요.",
            hint = "[제작](C) → 농사 탭 → 플랜트 → 누른 채 끌면 여러 칸이 깔려요",
            padHint = "십자키 ↑ 제작 → 농사 탭 → 플랜트 → RT를 누른 채 움직이면 여러 칸",
            tip = "플랜트 하나 = 구리 주괴 1개. 밭을 한곳에 모아 깔면 다음 장에서 자동파종기 · 자동수확기 한 대가 한꺼번에 돌봐요.",
            objs = new[] { Place("PlantTile", 20) },
            coins = 150,
        });
        c2.quests.Add(new Q
        {
            id = "C2_06_Orders", name = "이웃 늘리기",
            talk = "주민이 늘수록 마을이 커져요. 셔틀로 고양이들을 더 데려와요!",
            desc = "셔틀이 원하는 짐을 실어 보낼 때마다 고양이 가족이 건너와요. 주민 12마리를 모아요.",
            hint = "셔틀 말풍선의 짐을 모아 셔틀 클릭 — 어려운 짐은 [셔틀](J)에서 [다른 짐 받기]",
            padHint = "셔틀 앞에서 A로 짐 싣기 — 어려운 짐은 Y → LB 셔틀 창에서 [다른 짐 받기]",
            tip = "요리를 실어 보내면 짐값이 커요. 별빛 잼 · 캣닢 쿠키를 만들어 두면 좋아요.",
            objs = new[] { Residents(12) },
            coins = 200,
        });
        c2.quests.Add(new Q
        {
            id = "C2_07_Civ", name = "농업 마을로!",
            talk = "농업 마을까지 한 걸음이에요! 알림판 조건을 채워요.",
            desc = "온실 · 급수탑 · 주방이 선 개척 캠프는 이제 농업 마을이 될 수 있어요.",
            hint = "문명 알림판 조건: 밭 20칸 · 작물 4종 · 주민 12마리 · 2,000코인",
            tip = "작물 종류는 서로 다른 작물을 한 번씩 수확하면 늘어요. 코인은 셔틀 짐값 · 판매로 모아요.",
            objs = new[] { Level(3) },
        });
        chapters.Add(c2);

        var c3 = new Chapter { title = "농업 마을", goal = "도구를 강화하고 농사를 자동으로! 산업 도시로!", intro = "어엿한 농업 마을이에요! 더 많은 고양이를 먹이려면 기계에게 농사를 맡겨 봐요." };
        c3.quests.Add(new Q
        {
            id = "C3_01_Compost", name = "퇴비 만들기",
            talk = "남는 씨앗은 버리지 말고 퇴비로! 퇴비를 준 밭은 더 많이 열려요.",
            desc = "남는 씨앗을 퇴비통에 넣으면 퇴비가 돼요. 퇴비를 준 밭은 작물이 더 열려요.",
            hint = "[제작](C) → 농사 탭 → 퇴비통 설치 → 클릭해 씨앗 넣기",
            padHint = "십자키 ↑ 제작 → 농사 탭 → 퇴비통 → A로 열어 씨앗 넣기",
            tip = "퇴비를 들고 밭을 클릭하면 수확 3번 동안 작물이 하나씩 더 열리고, 황금 작물 확률도 두 배!",
            objs = new[] { Place("Item_Composter", 1), Craft("Item_Compost", 2) },
            coins = 150,
        });
        c3.quests.Add(new Q
        {
            id = "C3_02_Drill2", name = "티어2 드릴",
            talk = "더 단단한 광석을 캐려면 드릴을 강화해야 해요.",
            desc = "리튬 · 알루미늄은 단단해서 티어2 드릴이 있어야 캐요.",
            hint = "[제작](C) → 도구 탭 → 티어2 드릴 (티어1 드릴을 강화)",
            padHint = "십자키 ↑ 제작 → 도구 탭 → 티어2 드릴",
            tip = "티어2 드릴은 초당 채굴량이 6배예요.",
            objs = new[] { Craft("Item_Tier2_Drill", 1) },
        });
        c3.quests.Add(new Q
        {
            id = "C3_03_Mine2", name = "리튬 · 알루미늄",
            talk = "가벼운 금속이 있어야 기계를 만들 수 있어요. 캐러 가요!",
            desc = "자동화 설비와 공장엔 가벼운 금속이 필요해요.",
            hint = "티어2 드릴로 알루미늄 · 리튬 광석을 꾹 클릭",
            padHint = "티어2 드릴을 들고 광석 앞에서 RT를 꾹",
            tip = "화살표가 가까운 광석을 가리켜요. 미니맵(M)으로 크게 볼 수도 있어요.",
            objs = new[] { Collect("Item_Aluminum", 20), Collect("Item_Lithium", 10) },
            coins = 150,
        });
        c3.quests.Add(new Q
        {
            id = "C3_03b_Storage", name = "창고 짓기",
            talk = "가방이 점점 꽉 차요! 창고를 하나 지어 두면 든든해요.",
            desc = "창고에는 무엇이든 넣어 둘 수 있어요. 창고를 클릭하면 창이 열려요 — 가방의 물건을 눌러 옮겨요.",
            hint = "[제작](C) → 창고 탭 → 일반창고 → 기지 근처에 설치",
            padHint = "십자키 ↑ 제작 → 창고 탭 → 일반창고 → RT로 설치",
            tip = "수확기 · 채굴기를 창고와 연결하면(설비를 클릭해 [연결하기]) 거둔 것이 창고로 들어가요.\n주방 · 제련소도 연결된 창고에서 재료를 꺼내 써요.",
            objs = new[] { Place("NormalStorage", 1) },
            coins = 150,
        });
        c3.quests.Add(new Q
        {
            id = "C3_04_Auto", name = "자동 농사",
            talk = "심고 거두는 건 기계에게 맡겨 볼까요? 그동안 우리는 다른 일을 해요!",
            desc = "자동파종기는 곁의 빈 밭에 씨앗을 심고, 자동수확기는 다 자란 작물을 거둬요.",
            hint = "[제작](C) → 농사 탭 → 자동파종기 · 자동수확기 → 밭 곁에 설치",
            padHint = "십자키 ↑ 제작 → 농사 탭 → 자동파종기 · 자동수확기 → RT로 설치",
            tip = "씨앗을 든 채 파종기를 클릭하면 씨앗통에 들어가요. 수확기를 창고와 연결하면 수확물이 창고로 들어가요.",
            objs = new[] { Place("AutoSeeder", 1), Place("AutoHarvester", 1) },
            coins = 300,
        });
        c3.quests.Add(new Q
        {
            id = "C3_04b_MachineFarm", name = "기계 농장",
            talk = "밭이 넓어지면 손으로 다 거두기 벅차요. 밭을 36칸으로 넓히고 자동수확기가 거두는 걸 지켜봐요!",
            desc = "자동수확기는 둘레의 다 자란 작물을 알아서 거둬요. 밭을 36칸으로 넓히고, 수확기 범위 안 밭에 심어 자동수확기로 30번 거둬요.",
            hint = "수확기에 마우스를 올리면 범위가 보여요 — 그 안에 밭을 깔고 심어 두기",
            padHint = "수확기 곁에 밭을 깔고 심어 두기 — 다 자라면 수확기가 거둬요",
            tip = "수확기 · 파종기를 같은 창고와 연결하면(설비를 클릭해 [연결하기]) 거둔 씨앗을 파종기가 다시 꺼내 심어요 — 밭이 혼자 돌아가요!",
            objs = new[] { Place("PlantTile", 36), AutoHarvest(30) },
            coins = 300,
        });
        c3.quests.Add(new Q
        {
            id = "C3_05_Churu", name = "츄르 요리",
            talk = "츄르는 고양이들이 제일 좋아하는 간식이에요! 셔틀 짐으로도 자주 나와요.",
            desc = "츄르는 고양이들이 제일 좋아하는 간식! 셔틀 짐으로 자주 나와요.",
            hint = "주방에서 츄르 (츄르 옥수수 2 + 우유 버섯 1)",
            padHint = "주방 앞에서 A → 츄르",
            tip = "우유 버섯은 [그늘 작물]이라 전기 없이도 자라요. 츄르 옥수수는 급수탑 곁에서 두 배로 자라요.",
            objs = new[] { Craft("Item_Churu", 2) },
            coins = 300,
        });
        c3.quests.Add(new Q
        {
            id = "C3_05b_Neighbors", name = "이웃 마을",
            talk = "마을이 북적일수록 좋아요! 셔틀로 고양이 가족을 더 데려와요.",
            desc = "셔틀이 원하는 짐을 실어 보내 주민을 22마리까지 늘려요. 요리를 실으면 짐값이 커요.",
            hint = "셔틀 말풍선의 짐을 모아 셔틀 클릭 — 어려운 짐은 [셔틀](J)에서 [다른 짐 받기]",
            padHint = "셔틀 앞에서 A로 짐 싣기 — 어려운 짐은 Y → LB 셔틀 창에서 [다른 짐 받기]",
            tip = "셔틀은 짐을 다 실으면 바로 떠나요. 다녀오는 동안 다음 짐을 미리 모아 두면 빨라요.",
            objs = new[] { Residents(22) },
            coins = 300,
        });
        c3.quests.Add(new Q
        {
            id = "C3_06_Factory", name = "조립 공장",
            talk = "공장이 있으면 제련소랑 주방이 더 빨리 일해요!",
            desc = "조립 공장 곁(8칸)의 제련소 · 주방 · 퇴비통은 1.3배 빨리 일해요.",
            hint = "[제작](C) → 문명 탭 → 조립 공장 (회로 기판 · 알루미늄 주괴) → 설치",
            padHint = "십자키 ↑ 제작 → 문명 탭 → 조립 공장 → RT로 설치",
            tip = "회로 기판 = 구리 주괴 + 리튬 주괴 (재료 탭). 알루미늄 주괴는 제련소에서.",
            objs = new[] { Place("Item_Factory", 1) },
        });
        c3.quests.Add(new Q
        {
            id = "C3_07_Civ", name = "산업 도시로!",
            talk = "산업 도시가 코앞이에요! 조건을 마저 채워요.",
            desc = "공장이 돌아가면 마을은 산업 도시가 돼요.",
            hint = "문명 알림판 조건: 밭 36칸 · 작물 7종 · 주민 30마리 · 8,000코인",
            tip = "Lv.4가 되면 고급 제작(티어3 드릴 · 고급 설비 · 침입방어막)이 열려요.",
            objs = new[] { Level(4) },
        });
        chapters.Add(c3);

        var c4 = new Chapter { title = "산업 도시", goal = "하늘을 연구할 통신탑 · 천문대를 세워 과학 도시로!", intro = "공장 귀 환기구에서 김이 폴폴 나요! 이제 하늘을 연구할 차례예요." };
        c4.quests.Add(new Q
        {
            id = "C4_01_Drill3", name = "티어3 드릴",
            talk = "티타늄이랑 다이아몬드는 아주 단단해요. 드릴을 한 번 더 강화해요!",
            desc = "티타늄 · 다이아몬드는 티어3 드릴로만 캘 수 있어요.",
            hint = "[제작](C) → 도구 탭 → 티어3 드릴 (티어2 드릴을 강화)",
            padHint = "십자키 ↑ 제작 → 도구 탭 → 티어3 드릴",
            objs = new[] { Craft("Item_Tier3_Drill", 1) },
        });
        c4.quests.Add(new Q
        {
            id = "C4_02_Mine3", name = "희귀 광석",
            talk = "천문대엔 다이아몬드 렌즈가 필요해요. 희귀 광석을 찾아봐요!",
            desc = "천문대 렌즈엔 다이아몬드, 튼튼한 탑엔 티타늄이 들어가요.",
            hint = "티어3 드릴로 티타늄 · 다이아몬드 광석을 캐요",
            padHint = "티어3 드릴을 들고 광석 앞에서 RT를 꾹",
            tip = "희귀 광석이 바닥나면 밤의 유성우가 곁에 새 광석을 떨어뜨려요.",
            objs = new[] { Collect("Item_Titanium", 12), Collect("Item_Diamond", 2) },
            coins = 500,
        });
        c4.quests.Add(new Q
        {
            id = "C4_03_Shield", name = "폭풍 대비",
            talk = "가끔 큰 폭풍이 와요. 방어막으로 소중한 밭을 지켜요!",
            desc = "폭풍은 지켜 주는 것이 없는 작물을 날려 버려요. 침입방어막(반경 10)을 세워요.",
            hint = "[제작](C) → 농사 탭 → 침입방어막 → 밭 한가운데 설치",
            padHint = "십자키 ↑ 제작 → 농사 탭 → 침입방어막 → RT로 설치",
            tip = "시계 아래 날씨 예보에 폭풍이 보이면 미리 대비해요. 혜성 고추는 폭풍에도 끄떡없어요.",
            objs = new[] { Place("StormShield", 1) },
            coins = 500,
        });
        c4.quests.Add(new Q
        {
            id = "C4_04_Comm", name = "통신탑",
            talk = "통신탑이 서면 고향별과 연락이 잘 닿아요. 셔틀이 한 척 더 올 수 있어요!",
            desc = "통신탑을 세우면 셔틀이 한 척 더 와요. 셔틀이 내려앉을 착륙장도 하나 더 지어요.",
            hint = "[제작](C) → 문명 탭 → 통신탑 · 셔틀 착륙장 → 설치",
            padHint = "십자키 ↑ 제작 → 문명 탭 → 통신탑 · 셔틀 착륙장 → RT로 설치",
            tip = "셔틀은 착륙장마다 한 척, 통신탑 하나마다 한 척 더 (최대 3척) 다녀요.",
            objs = new[] { Place("Item_CommTower", 1), Place("Item_ShuttlePad", 2) },
            coins = 500,
        });
        c4.quests.Add(new Q
        {
            id = "C4_04b_TwoShuttles", name = "셔틀 두 척",
            talk = "셔틀이 두 척이 됐어요! 두 배로 고양이들을 데려올 수 있어요.",
            desc = "두 셔틀에 짐을 실어 보내 주민을 40마리까지 늘려요.",
            hint = "착륙장마다 셔틀이 한 척 — 말풍선의 짐을 모아 셔틀 클릭",
            padHint = "셔틀 앞에서 A로 짐 싣기",
            tip = "착륙장을 창고와 이으면(셔틀 창 [창고 연결]) 창고에 있는 짐을 알아서 실어요.",
            objs = new[] { Residents(40) },
            coins = 800,
        });
        c4.quests.Add(new Q
        {
            id = "C4_04c_BigFarm", name = "큰 농장",
            talk = "셔틀이 두 척이라 짐도 두 배예요! 밭을 60칸으로 넓히고 고급 자동수확기를 들여요.",
            desc = "고급 자동수확기는 더 넓은 범위에서 한 번에 여러 칸을 거둬요. 밭을 60칸으로 넓히고 고급 자동수확기를 세워요.",
            hint = "[제작](C) → 농사 탭 → 플랜트 · 고급 자동수확기 → 밭 곁에 설치",
            padHint = "십자키 ↑ 제작 → 농사 탭 → 플랜트 · 고급 자동수확기 → RT로 설치",
            tip = "고급 자동수확기 = 자동수확기 + 티타늄 합금 2 + 합금 2. 착륙장을 창고와 이으면(셔틀 창 [창고 연결]) 창고의 수확물이 셔틀에 알아서 실려요.",
            objs = new[] { Place("PlantTile", 60), Place("AutoHarvester_T2", 1) },
            coins = 800,
        });
        c4.quests.Add(new Q
        {
            id = "C4_05_Tart", name = "크리스탈 베리 타르트",
            talk = "고급 요리는 짐값이 아주 커요. 타르트에 도전해 봐요!",
            desc = "고급 요리는 셔틀 짐값이 아주 커요. 크리스탈 베리는 밤에 잘 자라요.",
            hint = "주방에서 크리스탈 베리 타르트 (베리 2 · 버섯 우유 · 별빛 잼)",
            padHint = "주방 앞에서 A → 크리스탈 베리 타르트",
            objs = new[] { Craft("Item_BerryTart", 1) },
            coins = 800,
        });
        c4.quests.Add(new Q
        {
            id = "C4_06_Obs", name = "천문대",
            talk = "천문대로 고향별까지 가는 가장 안전한 하늘길을 찾아요!",
            desc = "천문대가 큰 이주선이 다닐 하늘길을 찾아요. 날씨 예보도 한 칸 더 보여요.",
            hint = "[제작](C) → 문명 탭 → 천문대 → 설치",
            padHint = "십자키 ↑ 제작 → 문명 탭 → 천문대 → RT로 설치",
            tip = "천문대 재료: 티타늄 합금 2 · 회로 기판 2 · 다이아몬드 2 · 크리스탈 베리 2.",
            objs = new[] { Place("Item_Observatory", 1) },
        });
        c4.quests.Add(new Q
        {
            id = "C4_07_Civ", name = "과학 도시로!",
            talk = "과학 도시까지 조금만 더 힘내요!",
            desc = "하늘을 연구하는 과학 도시가 눈앞이에요.",
            hint = "문명 알림판 조건: 밭 60칸 · 작물 10종 · 주민 50마리 · 30,000코인",
            objs = new[] { Level(5) },
        });
        chapters.Add(c4);

        var c5 = new Chapter { title = "과학 도시", goal = "주민이 살 거주 모듈과 우주 관제탑으로 우주 항구를!", intro = "천문대가 큰 이주선의 하늘길을 찾았어요! 더 많은 고양이를 맞을 준비를 해요." };
        c5.quests.Add(new Q
        {
            id = "C5_01_Habitat", name = "거주 모듈",
            talk = "주민이 많아져 집이 모자라요! 거주 모듈을 지어 맞이해요.",
            desc = "거주 모듈마다 셔틀이 고양이를 한 마리 더 태워 와요 (최대 4채).",
            hint = "[제작](C) → 문명 탭 → 거주 모듈 2채 → 설치",
            padHint = "십자키 ↑ 제작 → 문명 탭 → 거주 모듈 → RT로 설치",
            objs = new[] { Place("Item_Habitat", 2) },
            coins = 2000,
        });
        c5.quests.Add(new Q
        {
            id = "C5_01b_BigFamilies", name = "대가족 맞이",
            talk = "거주 모듈 덕분에 셔틀마다 고양이가 더 타요! 대가족을 맞아요.",
            desc = "셔틀로 주민을 75마리까지 늘려요. 거주 모듈마다 한 번에 한 마리씩 더 타요.",
            hint = "셔틀 말풍선의 짐을 모아 셔틀 클릭",
            padHint = "셔틀 앞에서 A로 짐 싣기",
            objs = new[] { Residents(75) },
            coins = 3000,
        });
        c5.quests.Add(new Q
        {
            id = "C5_02_Booster", name = "성장 촉진기",
            talk = "주민이 늘면 먹을 것도 더 필요해요. 작물을 더 빨리 키워 봐요!",
            desc = "성장 촉진기 둘레 3칸의 작물은 1.5배 빨리 자라요.",
            hint = "[제작](C) → 농사 탭 → 성장 촉진기 → 밭 사이에 설치",
            padHint = "십자키 ↑ 제작 → 농사 탭 → 성장 촉진기 → RT로 설치",
            objs = new[] { Place("Item_GrowthBooster", 1) },
            coins = 2000,
        });
        c5.quests.Add(new Q
        {
            id = "C5_02b_Fields80", name = "넓은 들판",
            talk = "주민이 백 마리를 바라봐요! 밭을 80칸으로 넓히고 기계들에게 맡겨요.",
            desc = "우주 항구가 되려면 전기가 들어오는 밭 80칸이 있어야 해요. 넓은 밭은 밭 무리마다 자동파종기 · 자동수확기로 돌봐요.",
            hint = "플랜트를 더 깔고, 밭 무리마다 파종기 · 수확기를 하나씩",
            padHint = "플랜트를 더 깔고, 밭 무리마다 파종기 · 수확기를 하나씩",
            tip = "밭이 전기 범위를 벗어나면 전력 중계탑으로 범위를 넓혀요. 급수탑 · 온실 · 성장 촉진기 곁에 모아 깔면 더 빨리 자라요.",
            objs = new[] { Place("PlantTile", 80), AutoHarvest(150) },
            coins = 3000,
        });
        c5.quests.Add(new Q
        {
            id = "C5_03_Melon", name = "은하 수박",
            talk = "은하 수박은 크고 달콤해서 비싸게 팔려요!",
            desc = "은하 수박은 크고 비싸요. 퇴비를 주면 두 개씩 더 열려요!",
            hint = "은하 수박 씨앗을 심고 거둬요 (상점(B)에서도 팔아요)",
            padHint = "은하 수박 씨앗을 심고 거둬요 (상점: 십자키 →)",
            objs = new[] { Harvest("GalaxyMelon", 3) },
            coins = 3000,
        });
        c5.quests.Add(new Q
        {
            id = "C5_04_Control", name = "우주 관제탑",
            talk = "관제탑이 서면 셔틀이 더 빨리 오가요. 큰 이주선도 관제탑이 있어야 안전하게 내려앉아요!",
            desc = "관제탑이 서면 셔틀 왕복이 빨라지고 짐값도 20% 늘어요.",
            hint = "[제작](C) → 문명 탭 → 우주 관제탑 → 설치",
            padHint = "십자키 ↑ 제작 → 문명 탭 → 우주 관제탑 → RT로 설치",
            objs = new[] { Place("Item_ControlTower", 1) },
        });
        c5.quests.Add(new Q
        {
            id = "C5_05_Civ", name = "우주 항구로!",
            talk = "우주 항구가 되면 이제 큰 이주선을 맞을 일만 남아요!",
            desc = "우주 항구가 되면 이제 큰 이주선을 맞을 일만 남아요.",
            hint = "문명 알림판 조건: 밭 80칸 · 작물 12종 · 주민 100마리 · 120,000코인",
            objs = new[] { Level(6) },
        });
        chapters.Add(c5);

        var c6 = new Chapter { title = "우주 항구", goal = "큰 이주선이 내려앉을 착륙장을 세워 남은 고양이들을 모두 맞이해요!", intro = "마지막 장이에요! 셔틀로는 다 못 태워요 — 고향별의 큰 이주선이 내려앉을 자리를 만들어 남은 고양이들을 모두 맞이해요!" };
        c6.quests.Add(new Q
        {
            id = "C6_01_StarAlloy", name = "별빛 합금",
            talk = "큰 이주선을 맞으려면 별의 힘이 깃든 합금이 있어야 해요.",
            desc = "최종 이주 계획(행성 이주 프로토콜)에 별의 힘이 깃든 합금이 들어가요.",
            hint = "[제작](C) → 재료 탭 → 별빛 합금 (티타늄 합금 + 별 작물 3종) ×5",
            padHint = "십자키 ↑ 제작 → 재료 탭 → 별빛 합금",
            objs = new[] { Craft("Item_StarAlloy", 5) },
            coins = 5000,
        });
        c6.quests.Add(new Q
        {
            id = "C6_01b_LastShuttles", name = "마지막 셔틀들",
            talk = "큰 이주선이 오기 전에, 셔틀로 한 가족이라도 더 데려와요!",
            desc = "셔틀로 주민을 125마리까지 늘려요.",
            hint = "셔틀 말풍선의 짐을 모아 셔틀 클릭",
            padHint = "셔틀 앞에서 A로 짐 싣기",
            objs = new[] { Residents(125) },
            coins = 8000,
        });
        c6.quests.Add(new Q
        {
            id = "C6_01c_HomeFields", name = "새 고향의 들판",
            talk = "큰 이주선에서 내릴 고양이들이 먹을 들판이에요. 밭 100칸을 채워요!",
            desc = "새 고향이 되려면 전기가 들어오는 밭 100칸이 있어야 해요. 큰 이주선에서 내릴 고양이들을 먹일 들판이에요.",
            hint = "플랜트를 더 깔아 밭 100칸 — 파종기 · 수확기로 돌봐요",
            padHint = "플랜트를 더 깔아 밭 100칸 — 파종기 · 수확기로 돌봐요",
            objs = new[] { Place("PlantTile", 100) },
            coins = 8000,
        });
        c6.quests.Add(new Q
        {
            id = "C6_02_Protocol", name = "행성 이주 프로토콜",
            talk = "프로토콜의 마지막 단계예요. 남은 고양이들을 한 번에 옮길 계획을 세워요!",
            desc = "남은 고양이들을 한 번에 안전하게 옮길 최종 이주 계획을 완성해요.",
            hint = "[제작](C) → 재료 탭 → 행성 이주 프로토콜",
            padHint = "십자키 ↑ 제작 → 재료 탭 → 행성 이주 프로토콜",
            tip = "재료: 별빛 합금 3 · 자동수확기 · 자동파종기 · 우라늄 주괴 3 · 다이아몬드 3 · 에너지 셀 4.",
            objs = new[] { Craft("Item_MigrationProtocol", 1) },
        });
        c6.quests.Add(new Q
        {
            id = "C6_03_Feast", name = "이주 만찬",
            talk = "새 행성에 도착할 고양이들을 위한 잔치를 준비해요!",
            desc = "새 행성에 도착할 고양이들을 위한 잔치를 준비해요.",
            hint = "주방에서 고양이 만찬 (츄르 2 · 타르트 · 수박 주스 · 무지개 샐러드)",
            padHint = "주방 앞에서 A → 고양이 만찬",
            objs = new[] { Craft("Item_CatFeast", 1) },
            coins = 10000,
        });
        c6.quests.Add(new Q
        {
            id = "C6_04_LaunchPad", name = "이주선 착륙장",
            talk = "드디어 큰 이주선이 내려앉을 착륙장을 세울 차례예요!",
            desc = "고향별의 큰 이주선이 내려앉을 커다란 착륙장이에요!",
            hint = "[제작](C) → 문명 탭 → 이주선 착륙장 (3×3) → 설치",
            padHint = "십자키 ↑ 제작 → 문명 탭 → 이주선 착륙장 → RT로 설치",
            objs = new[] { Place("Item_LaunchPad", 1) },
        });
        c6.quests.Add(new Q
        {
            id = "C6_05_Civ", name = "새 고향!",
            talk = "마지막 조건만 채우면 큰 이주선이 고향별을 출발해 이리로 와요. 대원님, 정말 최고예요!",
            desc = "센터코어가 마지막 단계에 오르면 큰 이주선이 고향별에 남은 고양이들을 모두 태우고 착륙장에 내려앉아요!",
            hint = "문명 알림판 조건: 밭 100칸 · 작물 14종 · 주민 150마리 · 300,000코인",
            objs = new[] { Level(7) },
        });
        chapters.Add(c6);

        return chapters;
    }

    [MenuItem("Tools/WISH/진행 구조 다시 만들기")]
    public static void Build()
    {
        var items = new Dictionary<string, FarmingItemData>();
        foreach (var guid in AssetDatabase.FindAssets("t:FarmingItemData"))
        {
            var a = AssetDatabase.LoadAssetAtPath<FarmingItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null && !items.ContainsKey(a.name)) items[a.name] = a;
        }

        FarmingItemData Item(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (items.TryGetValue(name, out var it)) return it;
            Debug.LogError($"[QuestChainBuilder] 아이템을 찾을 수 없어요: {name}");
            return null;
        }

        if (!AssetDatabase.IsValidFolder(QuestFolder))
            AssetDatabase.CreateFolder("Assets/Scripts/Debug_field/Quest", "Chain");

        var chain = AssetDatabase.LoadAssetAtPath<QuestChain>(ChainPath);
        if (chain == null)
        {
            chain = ScriptableObject.CreateInstance<QuestChain>();
            AssetDatabase.CreateAsset(chain, ChainPath);
        }
        chain.chapters.Clear();

        int questCount = 0;
        foreach (var ch in Define())
        {
            var chapter = new QuestChain.Chapter { title = ch.title, goal = ch.goal, intro = ch.intro ?? "" };
            foreach (var q in ch.quests)
            {
                string path = $"{QuestFolder}/{q.id}.asset";
                // 기존 에셋 재사용해서 GUID 유지
                var def = AssetDatabase.LoadAssetAtPath<QuestDefinition>(path);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<QuestDefinition>();
                    AssetDatabase.CreateAsset(def, path);
                }
                def.questName   = q.name;
                def.description = q.desc ?? "";
                def.talk        = q.talk ?? "";
                def.hint        = q.hint ?? "";
                def.padHint     = q.padHint ?? "";
                def.tutorialTip = q.tip ?? "";
                def.rewardCoins = q.coins;
                def.objectives  = new List<QuestObjective>();
                foreach (var o in q.objs)
                    def.objectives.Add(new QuestObjective
                    {
                        type             = o.type,
                        targetItem       = Item(o.item),
                        targetTechNodeId = o.tech ?? "",
                        targetAmount     = Mathf.Max(1, o.amount),
                    });
                def.rewards = new List<QuestReward>();
                foreach (var r in q.rewards)
                    def.rewards.Add(new QuestReward { item = Item(r.item), amount = r.amount });
                EditorUtility.SetDirty(def);
                chapter.quests.Add(def);
                questCount++;
            }
            chain.chapters.Add(chapter);
        }
        EditorUtility.SetDirty(chain);

        var stages = AssetDatabase.LoadAssetAtPath<CoreStageTable>(StagesPath);
        if (stages != null)
        {
            // stages[0]은 시작 단계
            for (int i = 0; i < Levels.Length && i + 1 < stages.stages.Count; i++)
            {
                var reqs = new List<CoreRequirement>();
                foreach (var r in Levels[i])
                    reqs.Add(new CoreRequirement
                    {
                        type       = r.type,
                        item       = Item(r.item),
                        amount     = Mathf.Max(1, r.amount),
                        needsPower = r.power,
                        techNodeId = "",
                    });
                stages.stages[i + 1].requirements = reqs;
            }
            EditorUtility.SetDirty(stages);
        }
        else Debug.LogError($"[QuestChainBuilder] {StagesPath}이 없어요.");

        AssetDatabase.SaveAssets();
        Debug.Log($"[QuestChainBuilder] 퀘스트 {questCount}개 · 장 {chain.chapters.Count}개 · 문명 조건 {Levels.Length}단계를 만들었어요.");
    }
}
