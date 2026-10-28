using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Text.RegularExpressions;

public class HelpUI : MonoBehaviour
{
    [Header("설정")]
    [SerializeField] private float   panelWidth   = 380f;
    [SerializeField] private float   animDuration = 0.22f;

    [Header("폰트")]
    [SerializeField] private TMP_FontAsset fontAsset;

    private const string Intro =
        "반가워요, 대원! 고향 고양고양이 행성의 자원이 바닥나 <b>행성 이주 프로토콜</b>이 시작됐어요. 선발대로 먼저 내려온 이 불모지를 개척해 고양이들을 데려와 주세요. " +
        "궁금한 항목을 누르면 펼쳐져요.";

    private static readonly (string title, string body)[] Entries =
    {
        ("처음 시작하기",
            "• 목표: <b>농사를 지어</b> 이주 셔틀로 고향별 고양이들을 데려오고, 맵 한가운데 <b>센터코어</b>를 문명 <b>Lv.7 새 고향</b>까지 발전시키면 큰 이주선이 남은 고양이들을 모두 태우고 내려와요 (엔딩)! 그 뒤에도 계속 플레이할 수 있어요.\n" +
            "• 게임은 <b>장(챕터)</b>으로 나뉘어요. 한 장 = 문명 한 단계. 왼쪽 위 <b>퀘스트 패널</b>이 지금 할 일과 노란 <b>한 줄 안내</b>를 보여 주고, 화면의 <b>분홍 화살표</b>가 어디로 가서 무엇을 누를지 짚어 줘요.\n" +
            "• 안내 줄 아래 초록 버튼(예: [제작창 열기])을 누르면 필요한 창이 바로 열려요.\n" +
            "• 화면 위 가운데 <b>문명 알림판</b>이 다음 단계 조건을 보여 줘요 (누르면 센터코어 창).\n" +
            "• [[L]] <b>여정</b> 창(퀘스트 패널 머리를 눌러도): 문명 7단계 · 장마다 할 일 · 다음 단계에 열리는 것을 한눈에 봐요.\n" +
            "• 오른쪽 아래 버튼으로 [[가방]] · [[제작]] · [[상점]] · [[셔틀]] · [[연구]] · [[여정]] 창을 열 수 있어요 (단축키 Tab · C · B · J · T · L).\n" +
            "• 막히면 AI 조수 <b>코코</b>에게 물어보세요 ([[K]])."),

        ("기본 조작",
            "• [[WASD]] / 방향키: 이동\n" +
            "• [[좌클릭]]: 설치 · 씨앗 심기 · 채굴 · 수확 (누른 채 끌면 지나간 칸마다 차례로 — 빨리 끌어도 빠짐없이)\n" +
            "• [[우클릭]] 누르고 있기: 해체 — 진행 막대가 다 차면 아이템이 돌아와요\n" +
            "• [[F]]: 상호작용 (파종기에 씨앗 넣기 · 수확 · 커서 아래 주민 고양이와 이야기)\n" +
            "• 주민 고양이 [[좌클릭]]: 이야기 · 작물이나 요리를 든 채 누르면 선물 (설치물 · 씨앗을 들었으면 [[F]])\n" +
            "• 창고 · 자동화 설비 [[좌클릭]]: 그 설비의 창이 바로 열려요 (맨 아래 [연결하기] · [끊기]). 다 된 것은 누르면 받고, 광석 · 씨앗 · 재료를 든 채 누르면 바로 넣어요\n" +
            "• 인벤토리 아래 <b>휴지통</b>에 끌어다 놓으면 버려요 ([[Ctrl]]+클릭으로 바로). 마지막으로 버린 건 다시 꺼낼 수 있어요.\n" +
            "• 인벤토리 칸을 창 밖으로 끌어다 놓으면 땅에 떨어뜨려요. 3초가 지나면 가까이 있을 때 다시 주워져요.\n" +
            "• 아이템 칸 바탕색은 <b>티어</b>예요: <color=#3F8A66>초록 2티어</color> · <color=#6E57B5>보라 3티어</color> · <color=#A8741A>금색 4티어</color> (칸에 마우스를 올리면 보여요).\n" +
            "• [[1~7]] · [[Q]]/[[E]] · 휠: 핫바 고르기   • [[Esc]]: 선택 해제\n" +
            "• [[Ctrl]] + 휠: 확대 · 축소   • [[G]]: 격자 표시"),

        ("창 단축키",
            "• [[Tab]] 인벤토리 (장비칸 포함)   • [[C]] 제작\n" +
            "• [[B]] 상점   • [[T]] 연구\n" +
            "• [[J]] 이주 셔틀 (착륙장을 눌러도)   • [[L]] 여정 (장 · 문명 단계)\n" +
            "• [[Esc]] / [[P]] 메뉴 — 저장 · 옵션 · 나가기\n" +
            "• [[Enter]] 채팅   • [[K]] 코코와 대화   • 오른쪽 위 [[?]] 도움말\n" +
            "• 위 가운데 문명 알림판: 센터코어 창\n" +
            "• 오른쪽 위 <b>미니맵</b>: 가 본 곳만 보여요 (안 가 본 곳은 어둡게). [[M]] 또는 미니맵을 누르면 크게 보기\n" +
            "• Esc 한 번은 한 가지만 해요: 열린 창 닫기 → 핫바 선택 해제 → 메뉴 열기 순서예요."),

        ("게임패드 (Xbox 배치)",
            "• 왼쪽 스틱 이동 · 오른쪽 스틱 조준. 조준하지 않으면 든 아이템에 맞춰 알아서 골라요 (광석 · 다 자란 밭 · 바로 앞 칸)\n" +
            "• [[RT]] 사용: 채굴 · 설치 · 심기 · 수확 · 퇴비 주기 (누르고 있으면 반복)   • [[LT]] 누르고 있기: 해체\n" +
            "• [[A]] 가까운 설비 열기 · 든 것 넣기 · 수확 · 주민 고양이와 이야기 · 선물 (말풍선이 뜬 대상)   • [[B]] 빈손 · 창 닫기\n" +
            "• [[Y]] 인벤토리   • [[LB]]/[[RB]] 핫바\n" +
            "• 십자키 [[↑]] 제작 · [[→]] 상점 · [[←]] 연구 · [[↓]] 퀘스트 상세   • 셔틀 창: [[Y]]로 가방을 연 뒤 [[LB]] (착륙장 앞 [[A]] = 짐 싣기)\n" +
            "• [[START]] 메뉴 (도움말도 여기서)   • [[BACK]] 미니맵 켜기 · 끄기   • [[L3]] 격자   • [[R3]] 확대 · 축소\n" +
            "• 창에서는 십자키 · 왼쪽 스틱으로 고르고 [[A]]로 눌러요. 인벤토리 칸은 [[A]]로 집어 옮긴 뒤 [[A]]로 놓고, [[X]]는 휴지통이에요.\n" +
            "• 창에서 [[LB]]/[[RB]] 창 바꾸기 (가방 · 제작 · 상점 · 연구 · 여정 · 이주 셔틀)   • [[LT]]/[[RT]] 탭 · 가게 / 가방 · 연구 분야   • 오른쪽 스틱 스크롤\n" +
            "• 오른쪽 아래 막대에 지금 누를 수 있는 버튼이 나와요. 진동 세기는 옵션에서 바꿀 수 있어요.\n" +
            "• 마우스를 움직이면 바로 마우스 조작으로 돌아가요."),

        ("채굴과 드릴",
            "• 핫바에서 드릴을 고르고 자원 블록을 [[좌클릭]]한 채로 있으면 캐져요 (가까이 가야 해요).\n" +
            "• 도구를 들면 커서가 그 도구 모양으로 바뀌어요. 드릴은 <b>날 끝</b>이 클릭 지점이에요.\n" +
            "• <b>티어1</b> 구리 · 철 광석 / <b>티어2</b> + 리튬 · 알루미늄 광석 / <b>티어3</b> + 티타늄 · 우라늄 광석 · 다이아몬드\n" +
            "• 드릴은 한 단계 아래 드릴을 재료로 <b>강화</b>해요: 티어1 → 티어2(문명 Lv.2) → 티어3(Lv.4) → <b>플라즈마 드릴</b>(Lv.5, 금색 4티어). 단계마다 초당 채굴량이 4~6배!\n" +
            "• 드릴 등급이 모자라면 \"더 좋은 도구가 필요합니다\"라고 알려 줘요.\n" +
            "• 떨어진 아이템은 가까이 가면 쏙 빨려 들어와요. 가방이 꽉 차면 바닥에 남아 있으니 비우고 다시 주우세요.\n" +
            "• 자원 블록은 저절로 다시 생기지 않아요. 대신 밤에 <b>유성우</b>가 오면 곁에 운석이 떨어져 리튬 · 티타늄 · 우라늄 · 다이아몬드 덩어리가 새로 생겨요.\n" +
            "• 캔 광석은 코인이 되지 않아요 — 재료예요. 돈은 농사 · 요리 · 셔틀 짐값으로 벌어요 (셔틀이 주괴 · 합금을 짐으로 실어 가기도 해요)."),

        ("제련 (주괴 · 합금)",
            "• 캔 광석은 그대로 쓰지 않고 <b>주괴</b>로 바꿔서 써요. 제작창([[C]])에서 광석으로 <b>제련소</b>를 만들어 설치하세요 (발전소 곁이나 전력 중계탑 범위 안이면 전기가 들어와요).\n" +
            "• 광석을 <b>든 채</b> 제련소를 클릭하면 그 광석이 들어가고 <b>3초</b>마다 광석 2개가 주괴 1개로 녹아요 (화로처럼). 다 녹으면 다시 클릭해 받아요! 그냥 클릭하면 제련소 창이 열려 넣을 광석을 고를 수 있어요.\n" +
            "• <b>고급 제련소</b>(2×2): 문명 Lv.4가 되면 제련소 + 티타늄 합금 2 + 합금 2로 강화해요. <b>1초</b>마다 녹이고(3배) 광석 · 주괴를 300개까지 담아요.\n" +
            "• 녹이는 동안은 전기를 더 써요. 전기가 끊기면 멈췄다가, 다시 들어오면 이어서 녹여요.\n" +
            "• 제련소를 창고와 <b>연결</b>하면 창고의 광석을 알아서 가져와 녹이고, 주괴를 창고로 돌려놔요 (채굴기 → 창고 → 제련소).\n" +
            "• <b>합금</b>은 전기가 들어오는 제련소 곁에서 제작창 <b>재료</b> 탭의 <b>합금</b> 칸으로 만들어요. 한 번에 [1개] · [5개] · [전부]를 골라 [만들기].\n" +
            "• 합금: <b>합금</b>(구리+철) · <b>경량 합금</b>(알루미늄+리튬) · <b>티타늄 합금</b>(티타늄+알루미늄) · <b>강화 합금</b>(합금+티타늄) · <b>별빛 합금</b>(티타늄 합금+별 작물 3종)"),

        ("문명 발전 (센터코어)",
            "• 다음 단계 조건(문명 시설 · 밭 · 작물 종류 · 주민 수 · 발전 자금)을 모두 채우면 센터코어가 저절로 한 단계 올라가요. 퀘스트가 조건을 차례로 안내해요.\n" +
            "• Lv.1 착륙 기지 → Lv.2 개척 캠프 → Lv.3 농업 마을 → Lv.4 산업 도시 → Lv.5 과학 도시 → Lv.6 우주 항구 → <b>Lv.7 새 고향</b> (엔딩)\n" +
            "• 문명 시설: 기초 연구소 → 급수탑 · 온실 → 조립 공장 → 통신탑 · 천문대 → 거주 모듈 · 우주 관제탑 → 이주선 착륙장. 제작창([[C]])의 <b>문명</b> 탭에서 만들어요.\n" +
            "• 문명 시설은 <b>전기가 들어와야</b> 조건에 세어져요 (발전소 곁 · 전력 중계탑 범위 안). 창에 불이 켜지면 전기가 들어온 거예요.\n" +
            "• 조건: Lv.2 기초 연구소 · 밭 10칸 · 주민 3마리 / Lv.3 온실 · 급수탑 · 고양이 주방 · 밭 20칸 · 작물 4종 · 주민 12마리 / Lv.4 조립 공장 · 자동파종기 · 자동수확기 · 밭 36칸 · 작물 7종 · 주민 30마리\n" +
            "   Lv.5 천문대 · 통신탑 · 밭 60칸 · 작물 10종 · 주민 50마리 / Lv.6 우주 관제탑 · 거주 모듈 2 · 밭 80칸 · 작물 12종 · 주민 100마리 / Lv.7 이주선 착륙장 · 밭 100칸 · 작물 14종 · 주민 150마리\n" +
            "• Lv.3부터 밭은 <b>전기가 들어오는 칸</b>만 세어요. 마을이 커질수록 셔틀 짐도 커지니, 밭 무리마다 자동파종기 · 자동수확기를 두고 창고와 이어 두면 편해요.\n" +
            "• 문명 시설은 전기가 들어오는 동안 <b>효과</b>도 내요: 기초 연구소 연구비 10% 할인 · 급수탑 곁 5칸 밭에 물 · 온실 곁 4칸 작물 1.2배 + 폭풍 보호 · 조립 공장 곁 8칸 제련소 · 주방 · 퇴비통 1.3배 · 통신탑 셔틀 한 척 더 · 천문대 날씨 예보 한 칸 더 + 오로라 · 유성우 2배 · 거주 모듈 셔틀마다 고양이 한 마리 더 · 우주 관제탑 셔틀 왕복 40% 빨리 + 짐값 +20%.\n" +
            "• 단계마다 <b>발전 자금</b>(코인)도 모아야 해요: Lv.2 300 · Lv.3 2,000 · Lv.4 8,000 · Lv.5 3만 · Lv.6 12만 · Lv.7 30만. 다른 조건을 다 채운 채 코인이 모이면 단계가 오르면서 그만큼 내요.\n" +
            "• 단계가 오르면 축하 창과 함께 <b>새로 만들 수 있는 것</b> · 새 씨앗 · 새 연구가 열려요 (새 씨앗 5개씩 선물). <b>Lv.2에 중급 제작</b>(티어2 드릴 · 자동화 · 창고), <b>Lv.4에 고급 제작</b>(티어3 드릴 · 고급 설비 · 침입방어막 · 장비 II)이 저절로 열려요.\n" +
            "• 센터코어를 클릭하거나 화면 위 <b>문명 알림판</b>을 누르면 다음 단계 조건 · 해금 예정을 볼 수 있어요."),

        ("전기 (중앙 발전소 · 전력 중계탑)",
            "• 전기는 맵 한가운데의 <b>센터코어(중앙 발전소)</b>에서 나와요. 연료 없이 늘 돌아가고, <b>25칸</b> 안의 밭 · 제련소 · 주방 · 성장 촉진기 · 문명 시설에 바로 전기가 들어가요.\n" +
            "• 더 먼 곳은 <b>전력 중계탑</b>(3×3 거대한 탑)으로 넓혀요: 발전소 범위 안에 세우면 전기가 이어지고, 중계탑은 <b>30칸</b> 안에 전기를 줘요. 중계탑 범위 안에 또 세우면 사슬처럼 맵 끝까지 이어져요.\n" +
            "• 밭 · 제련소 · 문명 시설 · 중계탑을 들고 있으면 전기 범위가 테두리로 보여요 (하늘색 = 전기 있음, 회색 = 꺼짐). 중계탑을 놓을 땐 새로 닿을 범위가 흰 테두리로 보여요.\n" +
            "• 전기 범위가 보이는 동안 전기를 쓰는 설비와 중계탑이 어느 전원에서 전기를 받는지 <b>하늘색 전력선</b>으로 보여요 (회색 선 = 중계탑 사슬이 끊겼어요). 평소엔 직접 이은 노란 연결선만 보여요.\n" +
            "• <b>태양광 패널</b>: 연료 없이 낮(06:00~18:00)에만 발전해 <b>6칸</b> 안의 설비에 전기를 줘요. 곁의 중계탑도 낮 동안 이어 줄 수 있어요."),

        ("농사",
            "• <b>플랜트(밭)</b>를 전기가 닿는 곳(발전소 곁 · 전력 중계탑 범위)에 설치하고, 씨앗을 골라 [[좌클릭]]으로 심어요.\n" +
            "• 전기가 들어오면 작물마다 <b>12~60초</b> 만에 다 자라요. 다 자란 밭을 [[좌클릭]] (또는 [[F]])하면 수확!\n" +
            "• 수확하면 작물 1개 + 씨앗 1개가 나오고, 30% 확률로 씨앗이 1개 더 나와요. 작물은 상점에 팔거나, 요리하거나, 셔틀에 실어 고향별로 보내요.\n" +
            "• 씨앗은 조금씩만 늘어나요. 밭을 빨리 늘리려면 상점에서 씨앗을 사세요. 남는 씨앗은 <b>퇴비통</b>에 넣어 퇴비로!\n" +
            "• 밭에 마우스를 올리면 <b>밭 정보</b>가 떠요: 자란 정도 · 남은 시간 · 성장 배율과 그 이유 · 퇴비 · 황금 작물 확률.\n" +
            "• <b>작물 성향</b> — 작물마다 잘 자라는 조건이 달라요 (씨앗 툴팁에 나와요):\n" +
            "   별 3종 [별자리] 다른 색 별을 곁에 심으면 더 열림 · 캣닢 [고양이 사랑] 고양이가 곁에 있으면 2.5배 · 달빛 무 [밤 작물] 밤에 2배\n" +
            "   솜구름 목화 [비 좋아함] 별비 때 2.5배 · 츄르 옥수수 [물 좋아함] 물을 받으면 2배 · 혜성 고추 [폭풍 작물] 폭풍에 끄떡없고 폭풍 때 3배\n" +
            "   우유 버섯 [그늘 작물] 전기 없이도 자람 · 털실 꽃 [군락] 같은 꽃끼리 모으면 더 열림 · 무지개 당근 [섞어심기] 곁의 작물 종류가 많을수록 더 열림\n" +
            "   크리스탈 베리 [별빛] 밤에 빨리 + 오로라 때 황금 · 태양 해바라기 [해 바라기] 낮에 1.6배 · 은하 수박 [대식가] 퇴비를 주면 2개 더\n" +
            "• <b>물</b>: 급수탑(전기 연결) 곁 5칸이나 별비가 오면 밭이 촉촉해져 1.25배 빨리 자라요.\n" +
            "• <b>퇴비</b>: 들고 밭을 클릭하면 그 밭의 수확 3번 동안 작물이 1개씩 더 열리고 황금 작물 확률이 2배예요 (흙이 짙은 갈색).\n" +
            "• <b>황금 작물</b>: 아주 드물게(1.5%) 함께 열리는 금빛 작물. 판매가가 10배이고, 퇴비 · 오로라 때 더 잘 나와요.\n" +
            "• 처음엔 블루스타 씨앗과 밭 6칸으로 시작해요. 레드스타 · 옐로스타는 퀘스트 보상, <b>캣닢</b>은 상점에서 사요.\n" +
            "• 문명이 발전하면 새 작물이 열려요 (모두 14종): Lv.2 달빛 무 · 솜구름 목화 · 츄르 옥수수 / Lv.3 털실 꽃 · 우유 버섯 · 혜성 고추 / Lv.4 크리스탈 베리 · 무지개 당근 / Lv.5 은하 수박 · 태양 해바라기.\n" +
            "• 작물도 <b>티어</b>로 값이 크게 달라요: 1티어 5~12코인(별 3종 · 캣닢 · 달빛 무) · 2티어 25~40(솜구름 목화 · 츄르 옥수수 · 우유 버섯 · 털실 꽃 · 혜성 고추) · 3티어 100~110(무지개 당근 · 크리스탈 베리) · 4티어 300~360(태양 해바라기 · 은하 수박).\n" +
            "• <b>수확 낫</b>을 들고 [[좌클릭]]하면 주변 3×3 칸의 다 자란 작물을 한 번에 수확해요.\n" +
            "• <b>성장 촉진기</b>: 전기가 들어오면 3칸 안의 작물이 1.5배 빨리 자라요."),

        ("자동화 설비",
            "• <b>자동채굴기</b>: 자원 블록 옆에 설치하면 알아서 캐요. 클릭해서 <b>드릴</b>을 끼우면 더 빠르고 더 좋은 자원까지!\n" +
            "• <b>자동파종기</b>: 씨앗을 든 채 클릭하면 씨앗통에 들어가요 (그냥 클릭하면 씨앗통 창). 넣은 순서대로 주변 빈 밭에 심고, 씨앗통이 비면 연결한 창고의 씨앗을 꺼내 써요.\n" +
            "• <b>자동수확기</b>: 주변의 다 자란 밭을 자동으로 수확해요.\n" +
            "• <b>연결하기</b>: 설비나 창고를 클릭해 창 맨 아래 [연결하기] → 이을 설비를 클릭하면 노란 연결선이 생겨요 (최대 15칸). 이어진 설비를 다시 클릭하면 해제, [[Shift]]+클릭으로 여러 개를 연달아 이어요. [끊기]는 그 설비의 연결을 모두 끊어요.\n" +
            "• <b>창고</b> (일반 · 자원 · 씨앗): 설비와 연결하면 결과물이 창고로 들어가요. 연결한 창고가 없거나 차면 인벤토리로 와요.\n" +
            "• <b>작물 판매기</b>: 연결한 창고의 작물 · 요리를 5초마다 팔아 코인으로 바꿔요. 씨앗 · 자원은 팔지 않아요.\n" +
            "• <b>대형 창고</b>(2×2): 무엇이든 1000개까지 담아요.\n" +
            "• 고급(2×2) 설비는 더 빠르고 넓게 일해요. 설비에 마우스를 올리면 작업 범위가 보여요.\n" +
            "• 자동파종기와 이은 창고에 <b>퇴비</b>가 있으면, 퇴비가 다 떨어진 밭에 씨앗을 심으면서 퇴비도 함께 뿌려요.\n" +
            "• 레이저 색: 채굴 하늘색 · 파종 초록 · 수확 주황. 자동화 설비는 전기가 필요 없어요 (제련소 · 고양이 주방 · 문명 시설만 전기를 써요)."),

        ("제작 · 연구 · 상점",
            "• [[C]] 제작: 왼쪽에서 만들 물건 칸을 고르면 오른쪽에 하는 일 · 필요한 재료(가진 수/필요)가 크게 보여요. [만들기]나 고른 칸을 한 번 더 누르면 완성! 민트 칸 = 지금 만들 수 있음, \"퀘스트\" 딱지 = 퀘스트에 필요한 것. 모자란 재료 줄엔 어디서 얻는지가 붙고, 제작으로 만드는 재료는 그 줄을 누르면 레시피로 가요.\n" +
            "• [[T]] 연구: 왼쪽 목록(농사 · 자동화 · 셔틀 · 대원)에서 연구를 고르면 오른쪽에 지금 효과 → 연구하면 효과, 단계표, 비용이 보여요. [연구하기]로 한 단계씩 올려요.\n" +
            "• 연구마다 3~5단계 — 단계마다 문명 단계 조건 · 코인 · 재료(그 시기의 작물 · 요리 · 주괴)가 들어요. 기초 연구소에 전기가 들어오면 코인 10% 할인. (중급 · 고급 제작은 연구가 아니라 문명 Lv.2 · Lv.4에 저절로 열려요)\n" +
            "• 농사: <b>작물 성장</b> · <b>풍성한 수확</b>(하나 더 열릴 확률) / 자동화: <b>기계 속도</b>(파종기 · 수확기 · 채굴기) · <b>가공 속도</b>(제련소 · 주방 · 퇴비통) / 셔틀: <b>짐값 협상</b> · <b>빠른 셔틀</b> / 대원: 이동 · 설치 · 채굴 · 해체 속도 · 자석 범위.\n" +
            "• 이전 단계보다 얼마나 좋은지도 보여요. 흐린 칸은 다음 문명 단계에 열려요. 3 · 4티어를 만들면 반짝!\n" +
            "• [[B]] 상점: 왼쪽 씨앗 가게에서 씨앗을 누르면 사고, 오른쪽 내 가방에서 작물 · 요리 · 씨앗을 누르면 팔아요. 한 번에 몇 개씩 사고팔지는 위쪽 [1개] · [10개] · [전부] 알약으로 골라요. 잘못 팔았으면 [되찾기]! 새 씨앗은 문명 단계가 올라야 열려요 (\"처음!\" = 아직 안 길러 본 작물).\n" +
            "• 중간 재료: <b>회로 기판</b>(구리 주괴 + 리튬 주괴) · <b>에너지 셀</b>(리튬 주괴 + 우라늄 주괴, 고급 연료)\n" +
            "• <b>에너지 풍족기간</b>에는 작물 판매가가 2배예요!"),

        ("고양이 주방 (요리)",
            "• 제작창([[C]]) <b>농사</b> 탭의 <b>고양이 주방</b>(2×2)을 전기 범위 안에 세우고, 클릭 → 메뉴판에서 요리를 골라 [재료 넣기]! 다 된 요리는 주방을 클릭하면 바로 받아요.\n" +
            "• 요리는 재료로 판 값보다 비싸게 팔리고, 셔틀 짐으로 인기 만점이에요. 문명이 발전할수록 메뉴가 늘어요 (모두 15가지):\n" +
            "   Lv.1 별빛 잼 · 캣닢 쿠키 / Lv.2 달빛 무 피클 · 츄르 팝콘 · 구름 솜사탕 / Lv.3 츄르 · 버섯 우유 · 털실 뭉치 · 혜성 칠리 소스\n" +
            "   Lv.4 무지개 샐러드 · 크리스탈 베리 타르트 · 구름 방석 / Lv.5 해바라기씨 간식 · 은하 수박 주스 / Lv.6 고양이 만찬\n" +
            "• 주방을 창고와 <b>연결</b>하면 재료를 알아서 가져와 요리하고, 다 된 요리를 창고로 돌려놔요 (수확기 → 창고 → 주방 → 창고 → 작물 판매기).\n" +
            "• 메뉴는 넣어 둔 재료가 없을 때만 바꿀 수 있어요. 조립 공장 곁이면 1.3배 빨리 요리해요.\n" +
            "• <b>퇴비통</b>: 남는 씨앗 · 작물을 넣으면 퇴비가 돼요 (전기 필요 없음). 씨앗을 든 채 퇴비통을 클릭해도 들어가고, 다 된 퇴비는 클릭하면 받아요. 창고와 이으면 종류마다 20개 넘는 씨앗만 가져가요."),

        ("이주 셔틀 · 주민",
            "• 센터코어 곁 <b>셔틀 착륙장</b>에 고향별을 오가는 셔틀이 내려와요. 셔틀 머리 위 말풍선이 고향별로 실어 보낼 짐이에요 (실은 수/필요 수).\n" +
            "• 착륙장의 셔틀을 [[좌클릭]]하면 가방의 짐이 실려요 (조금씩 실어도 돼요). 다 실으면 <b>짐값</b>(판매가의 2배 코인)과 가끔 덤(퇴비 · 씨앗 · 에너지 셀)을 받고 셔틀이 떠나요.\n" +
            "• 셔틀은 고향별에 다녀오며(40초쯤) <b>고양이 가족</b>을 태워 와요. 내린 고양이는 <b>주민</b>이 되어 마을에 살아요 — 주민 수가 문명 단계 조건이에요.\n" +
            "• [[J]] 셔틀 창: 짐 · 태워 올 가족 · 남은 시간을 봐요. 어려운 짐은 [다른 짐 받기] (실어 둔 짐은 돌려받아요).\n" +
            "• 문명 단계가 오를수록 짐이 커지고, 그 단계에서 기를 수 있는 작물 · 만들 수 있는 요리 · 재료만 실어 가요. 아주 가끔 <b>황금 작물</b>도!\n" +
            "• 통신탑마다 셔틀이 한 척 더 와요 (착륙장도 하나 더, 최대 3척). 거주 모듈마다 고양이가 한 마리 더 타고, 우주 관제탑은 왕복을 빠르게 해요.\n" +
            "• 착륙장을 창고와 <b>연결</b>하면(셔틀 창의 [창고 연결]) 창고에 있는 짐을 알아서 실어요.\n" +
            "• <b>주민과 어울리기</b>: 주민 고양이를 [[좌클릭]]하면 이야기, 작물 · 황금 작물 · 요리를 든 채 누르면 선물이에요. 마우스를 올리면 이름과 <b>하트</b>(친밀도)가 보여요.\n" +
            "   이야기는 게임 1시간에 한 번, 선물은 2시간에 한 번 친밀도가 올라요. 고양이마다 <b>좋아하는 요리 · 작물</b>이 있어 그걸 주면 많이 올라요 (이야기하다 보면 알려 줘요).\n" +
            "   하트가 하나 오를 때마다 답례 선물(퇴비 · 씨앗 · 코인 · 황금 작물)을 받고, 하트 5개면 단짝! 친해진 고양이는 새 가족이 와도 마을에 남아요."),

        ("※  우주 날씨 · 낮과 밤",
            "• 시계 아래에 지금 날씨와 <b>다음 날씨 예보</b>가 보여요 (게임 6시간 = 실제 12분마다 바뀜, 천문대가 있으면 두 칸 앞까지).\n" +
            "• <b>별비</b>: 모든 밭에 물 — 작물 1.25배, 솜구름 목화 2.5배\n" +
            "• <b>오로라</b> (밤): 황금 작물이 2배 잘 나와요 (크리스탈 베리는 5배)\n" +
            "• <b>유성우</b> (밤, 문명 Lv.2부터): 곁에 운석이 떨어져 희귀 광석 덩어리가 생겨요\n" +
            "• <b>모래폭풍 · 눈보라</b> (문명 Lv.3부터): 경고 20초 뒤 몰아쳐, 침입방어막(반경 10) · 온실이 지키지 않는 작물을 날려요. <b>밭과 설비는 안전</b>해요. 예보를 보고 미리 거두세요!\n" +
            "• 18시가 넘으면 밤이 와서 화면이 어두워져요. 밤 작물(달빛 무 · 크리스탈 베리)은 밤에, 해바라기는 낮에 잘 자라요.\n" +
            "• <b>에너지 풍족기간</b>: 알림이 뜨는 동안 작물 · 요리를 2배 값에 팔 수 있어요."),

        ("장비",
            "• 인벤토리([[Tab]]) 위쪽 <b>장비칸</b>(5칸)에 장비를 끌어다 놓으면 장착, 장비칸을 누르면 해제돼요.\n" +
            "• <b>말랑 젤리 부츠</b> 이동 +30%  • <b>광부 헬멧</b> 채굴 속도 +30%  • <b>파워 장갑</b> 해체 속도 +30%\n" +
            "• <b>자석 방울 목걸이</b> 아이템 끌어오는 범위 +60%  • <b>건축가 공구벨트</b> 설치 속도 +30%\n" +
            "• 장비는 한 단계 아래 장비를 재료로 <b>II · III</b>로 강화해요: +30% → +60% → +100% (자석 목걸이 +60% → +120% → +200%). 부츠의 마지막 단계는 <b>로켓 부스터</b>!\n" +
            "• 장비는 5칸까지 낄 수 있지만 <b>같은 능력은 가장 좋은 것 하나만</b> 적용돼요 — 같은 장비를 여러 개 끼기보다 강화하세요."),

        ("꾸미기",
            "• <b>고양이 조각상</b> · <b>꽃 화분</b> · <b>별빛 가로등</b>: 기능은 없지만 농장을 예쁘게 꾸며요.\n" +
            "• 초급 제작으로 만들 수 있고, 다른 설치물처럼 [[우클릭]]으로 다시 회수할 수 있어요."),

        ("AI 조수 코코",
            "• [[K]]로 대화창 열기 · [[Enter]] 보내기 · [[Esc]] 닫기\n" +
            "• 말로 부탁하면 직접 일해요: \"철 광석 10개 캐줘\", \"밭에 씨앗 심어줘\", \"블루스타 5개 팔아줘\", \"따라와\", \"여기 있어\"\n" +
            "• 말투 바꾸기: \"아저씨 말투로 바꿔줘\" 또는 /성격 1~2\n" +
            "• 대화는 외부 AI 서버를 거쳐요. 옵션에서 코코를 끌 수 있어요."),

        ("저장 · 옵션",
            "• <b>60초마다 자동 저장</b>돼요. 메뉴([[Esc]] / [[P]])에서 [저장하기] · [저장하고 나가기]도 할 수 있어요.\n" +
            "• 월드는 3개까지! 메인메뉴 [시작]에서 고르거나 새로 만들어요.\n" +
            "• 옵션: 전체 · 배경음 · 효과음 볼륨, 해상도 · 화면 모드, <b>UI 크기</b>(PC, 70~120%), AI 코코 켜기/끄기"),
    };

    private static readonly (string title, string body) TouchEntry =
        ("터치 조작 (휴대폰)",
            "• 왼쪽 아래를 누른 채 끌면 [[조이스틱]] — 끄는 쪽으로 걸어요 (조금만 끌면 천천히)\n" +
            "• 땅 · 광석 · 밭을 [[누르면]] 클릭과 같아요: 설치 · 씨앗 심기 · 채굴 · 수확 (누르고 있으면 반복, 끌면 줄지어 설치)\n" +
            "• 설비를 누르면 그 설비의 창이 바로 열려요 (맨 아래 [연결하기])\n" +
            "• 주민 고양이를 누르면 이야기, 작물 · 요리를 든 채 누르면 선물 (설치물 · 씨앗을 들었으면 빈손으로 바꾼 뒤)\n" +
            "• [[해체]] 버튼을 켜면 설치물을 꾹 눌러 해체해요. 다시 누르면 꺼져요.\n" +
            "• 핫바 칸을 눌러 고르고, 고른 칸을 한 번 더 누르면 빈손\n" +
            "• 두 손가락을 벌리면 확대, 오므리면 축소\n" +
            "• 오른쪽 아래 [[가방]] · [[제작]] · [[상점]] · [[연구]] · [[메뉴]]로 창을 열어요. 휴대폰의 [[뒤로 가기]]는 Esc와 같아요 (창 닫기 → 빈손 → 메뉴)\n" +
            "• [[코코]] 버튼으로 AI 코코와 대화해요. 미니맵을 누르면 크게 보여요.");

    private static (string title, string body)[] ShownEntries()
    {
        if (!MobileMode.Active) return Entries;
        var list = new System.Collections.Generic.List<(string title, string body)>(Entries);
        list.Insert(1, TouchEntry);
        return list.ToArray();
    }

    private const string KeyColor = "#D9587A";
    private static readonly Regex KeyMarkup = new(@"\[\[(.+?)\]\]");

    private static string Format(string body) => KeyMarkup.Replace(body, $"<b><color={KeyColor}>$1</color></b>");

    private RectTransform panelRT;
    private CanvasGroup   panelCG;
    private RectTransform contentRT;
    private bool isPanelOpen = false;
    private Coroutine animCo;
    private Transform toggleButton;

    private GameObject[]      bodies;
    private Image[]           headerFills;
    private TextMeshProUGUI[] arrows;

    // 예전 씬 값 보정
    private float Width   => Mathf.Max(panelWidth, 460f);
    private float HiddenX => Width + 80f;
    private const float ShownX = -20f;

    private static HelpUI current;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => current = null;

    public static bool AnyOpen => current != null && current.isPanelOpen;

    public static Transform PanelRoot => current != null ? current.panelRT : null;

    public static Transform ToggleButton => current != null ? current.toggleButton : null;

    private void Awake()
    {
        current = this;

        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        var selfRT = GetComponent<RectTransform>();
        if (selfRT == null) selfRT = gameObject.AddComponent<RectTransform>();

        selfRT.anchorMin = Vector2.zero;
        selfRT.anchorMax = Vector2.one;
        selfRT.offsetMin = selfRT.offsetMax = Vector2.zero;
        selfRT.pivot = new Vector2(0.5f, 0.5f);

        BuildUI();

        // 화면 밖에선 투명하게
        panelRT.anchoredPosition = new Vector2(HiddenX, panelRT.anchoredPosition.y);
        panelCG.alpha = 0f;
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
    }

    private void Update()
    {
        if (isPanelOpen && EscapeRouter.TryConsume(EscapeRouter.CancelPressed))
            TogglePanel();
    }

    private void BuildUI()
    {
        BuildToggleButton();

        var panelGO = new GameObject("HelpPanel", typeof(RectTransform));
        panelGO.transform.SetParent(transform, false);
        panelRT = (RectTransform)panelGO.transform;
        panelRT.anchorMin = new Vector2(1f, 0f);
        panelRT.anchorMax = new Vector2(1f, 1f);
        panelRT.pivot     = new Vector2(1f, 0.5f);
        panelRT.sizeDelta = new Vector2(Width, 0f);
        panelRT.offsetMin = new Vector2(panelRT.offsetMin.x, 24f);
        panelRT.offsetMax = new Vector2(panelRT.offsetMax.x, DevPanelUI.Enabled ? DevPanelUI.PanelTop : -104f);
        panelGO.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT, catEars: true, earInset: 92f);
        panelCG = panelGO.AddComponent<CanvasGroup>();
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var ribbon = CuteUIKit.TitleRibbon(panelRT, "도움말");
        ribbon.anchorMin = ribbon.anchorMax = ribbon.pivot = new Vector2(0.5f, 1f);
        ribbon.anchoredPosition = new Vector2(0f, -16f);
        var fit = ribbon.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var close = (RectTransform)CuteUIKit.CloseButton(panelRT, TogglePanel, 28f).transform;
        close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 1f);
        close.anchoredPosition = new Vector2(-14f, -16f);
        close.sizeDelta = new Vector2(28f, 28f);

        contentRT = BuildScrollView(panelRT, new Vector2(14f, 16f), new Vector2(-10f, -60f));
        BuildItems();

        if (DevPanelUI.Enabled) DevPanelUI.Create(transform, fontAsset);
    }

    private void BuildToggleButton()
        => toggleButton = CornerButton(transform, "HelpToggleButton", "?", new Vector2(-25f, -25f), 55f, CuteUIKit.Strawberry, 30f,
                                       fontAsset, TogglePanel).transform;

    internal static Button CornerButton(Transform parent, string name, string label, Vector2 position, float size,
                                        Color color, float fontSize, TMP_FontAsset font, UnityAction onClick)
    {
        var btnGO = new GameObject(name, typeof(RectTransform));
        btnGO.transform.SetParent(parent, false);
        var btnRT = (RectTransform)btnGO.transform;
        btnRT.anchorMin = btnRT.anchorMax = btnRT.pivot = new Vector2(1f, 1f);
        btnRT.anchoredPosition = position;
        btnRT.sizeDelta = new Vector2(size, size);

        int radius = Mathf.FloorToInt(size * 0.5f);
        btnGO.AddComponent<Image>().color = Color.clear;
        var lip = CuteUIKit.Layer(btnGO.transform, "Lip", CuteUIKit.Rounded(radius), CuteUIKit.Darker(color, 0.8f));
        lip.rectTransform.offsetMin = lip.rectTransform.offsetMax = new Vector2(0f, -3f);
        var face = CuteUIKit.Layer(btnGO.transform, "Face", CuteUIKit.Rounded(radius), Color.white);

        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = face;
        CuteUIKit.ApplyButtonColors(btn, color);
        btn.onClick.AddListener(onClick);
        btnGO.AddComponent<UiButtonSfx>().hover = true;
        btnGO.AddComponent<CutePop>();

        var txt = CuteUIKit.Text(btnGO.transform, label, fontSize, Color.white, FontStyles.Bold);
        txt.rectTransform.anchorMin = Vector2.zero;
        txt.rectTransform.anchorMax = Vector2.one;
        txt.rectTransform.offsetMin = txt.rectTransform.offsetMax = Vector2.zero;
        if (font != null) txt.font = font;
        return btn;
    }

    internal static RectTransform BuildScrollView(RectTransform parent, Vector2 offsetMin, Vector2 offsetMax)
    {
        var scrollGO = new GameObject("ScrollView", typeof(RectTransform));
        scrollGO.transform.SetParent(parent, false);
        var srt = (RectTransform)scrollGO.transform;
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.offsetMin = offsetMin;
        srt.offsetMax = offsetMax;

        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal        = false;
        scroll.vertical          = true;
        scroll.movementType      = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        var viewportGO = new GameObject("Viewport", typeof(RectTransform));
        viewportGO.transform.SetParent(srt, false);
        var viewRT = (RectTransform)viewportGO.transform;
        viewRT.anchorMin = Vector2.zero;
        viewRT.anchorMax = Vector2.one;
        viewRT.offsetMin = Vector2.zero;
        viewRT.offsetMax = new Vector2(-14f, 0f);
        viewportGO.AddComponent<RectMask2D>();
        viewportGO.AddComponent<Image>().color = Color.clear;

        var contentGO = new GameObject("Content", typeof(RectTransform));
        contentGO.transform.SetParent(viewRT, false);
        var contentRT = (RectTransform)contentGO.transform;
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot     = new Vector2(0.5f, 1f);
        contentRT.sizeDelta = Vector2.zero;

        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(2, 2, 2, 6);
        vlg.spacing                = 8f;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewRT;
        scroll.content  = contentRT;

        var barGO = new GameObject("Scrollbar", typeof(RectTransform));
        barGO.transform.SetParent(srt, false);
        var barRT = (RectTransform)barGO.transform;
        barRT.anchorMin = new Vector2(1f, 0f);
        barRT.anchorMax = new Vector2(1f, 1f);
        barRT.pivot     = new Vector2(1f, 0.5f);
        barRT.sizeDelta = new Vector2(8f, 0f);
        barGO.AddComponent<Image>();
        var area = new GameObject("SlidingArea", typeof(RectTransform));
        area.transform.SetParent(barRT, false);
        var areaRT = (RectTransform)area.transform;
        areaRT.anchorMin = Vector2.zero;
        areaRT.anchorMax = Vector2.one;
        areaRT.offsetMin = areaRT.offsetMax = Vector2.zero;
        var handle = new GameObject("Handle", typeof(RectTransform));
        handle.transform.SetParent(areaRT, false);
        var handleRT = (RectTransform)handle.transform;
        handleRT.offsetMin = handleRT.offsetMax = Vector2.zero;
        handle.AddComponent<Image>();

        var scrollbar = barGO.AddComponent<Scrollbar>();
        scrollbar.handleRect = handleRT;
        scrollbar.direction  = Scrollbar.Direction.BottomToTop;
        CuteUIKit.StyleScrollbar(scrollbar);
        scroll.verticalScrollbar           = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return contentRT;
    }

    private void BuildItems()
    {
        var intro = CuteUIKit.Card(contentRT, CuteUIKit.ButterSoft, 12, "Intro");
        intro.raycastTarget = false;
        AddPadding(intro.gameObject, new RectOffset(12, 12, 10, 10));
        Body(intro.transform, Intro, CuteUIKit.Cocoa);

        var entries = ShownEntries();
        bodies      = new GameObject[entries.Length];
        headerFills = new Image[entries.Length];
        arrows      = new TextMeshProUGUI[entries.Length];

        for (int i = 0; i < entries.Length; i++)
        {
            int index = i;
            var item = new GameObject($"Item_{i}", typeof(RectTransform));
            item.transform.SetParent(contentRT, false);
            var ivlg = item.AddComponent<VerticalLayoutGroup>();
            ivlg.spacing                = 4f;
            ivlg.childForceExpandWidth  = true;
            ivlg.childForceExpandHeight = false;
            ivlg.childControlWidth      = true;
            ivlg.childControlHeight     = true;

            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(item.transform, false);
            var headerImg = CuteUIKit.SetRounded(header.AddComponent<Image>(), 12, CuteUIKit.LatteLight);
            header.AddComponent<LayoutElement>().preferredHeight = 38f;
            var hlg = header.AddComponent<HorizontalLayoutGroup>();
            hlg.padding                = new RectOffset(12, 12, 0, 0);
            hlg.childAlignment         = TextAnchor.MiddleLeft;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;

            var title = CuteUIKit.Text(header.transform, entries[i].title, 15f, CuteUIKit.Cocoa, FontStyles.Bold,
                                       TextAlignmentOptions.MidlineLeft);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            if (fontAsset != null) title.font = fontAsset;
            arrows[i] = CuteUIKit.Text(header.transform, "+", 11f, CuteUIKit.CocoaSoft, align: TextAlignmentOptions.MidlineRight);

            var headerBtn = header.AddComponent<Button>();
            headerBtn.targetGraphic = headerImg;
            headerBtn.transition    = Selectable.Transition.None;
            headerBtn.onClick.AddListener(() => ToggleEntry(index));
            header.AddComponent<UiButtonSfx>().hover = true;

            var body = CuteUIKit.Card(item.transform, Color.white, 12, "Body");
            body.raycastTarget = false;
            AddPadding(body.gameObject, new RectOffset(14, 12, 10, 12));
            Body(body.transform, Format(entries[i].body), CuteUIKit.Cocoa);

            bodies[i]      = body.gameObject;
            headerFills[i] = headerImg;
            SetEntryOpen(i, i == 0);
        }
    }

    private static void AddPadding(GameObject go, RectOffset padding)
    {
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = padding;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
    }

    private TextMeshProUGUI Body(Transform parent, string text, Color color)
    {
        var t = CuteUIKit.Text(parent, text, 14f, color, align: TextAlignmentOptions.TopLeft);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.lineSpacing      = 10f;
        if (fontAsset != null) t.font = fontAsset;
        return t;
    }

    private void ToggleEntry(int index)
    {
        SetEntryOpen(index, !bodies[index].activeSelf);
        Sfx.Play(bodies[index].activeSelf ? SfxId.UiOpen : SfxId.UiClose, 0.6f);
        LayoutRebuilder.MarkLayoutForRebuild(contentRT);
    }

    private void SetEntryOpen(int index, bool open)
    {
        bodies[index].SetActive(open);
        arrows[index].text     = open ? "-" : "+";
        headerFills[index].color = open ? CuteUIKit.PinkSoft : CuteUIKit.LatteLight;
    }

    [ContextMenu("Rebuild UI")]
    public void RebuildUI()
    {
        if (animCo != null) { StopCoroutine(animCo); animCo = null; }
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        BuildUI();
        panelRT.anchoredPosition = new Vector2(isPanelOpen ? ShownX : HiddenX, panelRT.anchoredPosition.y);
        panelCG.blocksRaycasts = panelCG.interactable = isPanelOpen;
        panelCG.alpha = isPanelOpen ? 1f : 0f;
    }

    public void TogglePanel()
    {
        isPanelOpen = !isPanelOpen;
        if (isPanelOpen) DevPanelUI.CloseIfOpen();
        Sfx.Play(isPanelOpen ? SfxId.UiOpen : SfxId.UiClose);
        panelCG.blocksRaycasts = isPanelOpen;
        panelCG.interactable   = isPanelOpen;
        if (isPanelOpen) CuteUIKit.WiggleEars(this, panelRT);
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(isPanelOpen));
    }

    public static void CloseIfOpen()
    {
        if (AnyOpen) current.TogglePanel();
    }

    public static void Open()
    {
        if (current != null && !current.isPanelOpen) current.TogglePanel();
    }

    private IEnumerator Animate(bool open)
    {
        float from = panelRT.anchoredPosition.x;
        float to   = open ? ShownX : HiddenX;
        float y    = panelRT.anchoredPosition.y;
        float duration = open ? animDuration * 1.5f : animDuration;
        if (open) panelCG.alpha = 1f;

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float t = elapsed / duration;
            float k = open ? CuteUIKit.EaseOutBack(t, 1.4f) : t * t;
            panelRT.anchoredPosition = new Vector2(Mathf.LerpUnclamped(from, to, k), y);
            yield return null;
        }
        panelRT.anchoredPosition = new Vector2(to, y);
        if (!open) panelCG.alpha = 0f;
        animCo = null;
    }
}
