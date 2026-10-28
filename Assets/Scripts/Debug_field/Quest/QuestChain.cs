using System.Collections.Generic;
using UnityEngine;

// 문명 단계별 챕터 진행표
[CreateAssetMenu(fileName = "QuestChain", menuName = "Quest/Quest Chain")]
public class QuestChain : ScriptableObject
{
    public const string ResourcePath = "QuestChain";

    [System.Serializable]
    public class Chapter
    {
        [Tooltip("장 이름 (예: 착륙 기지)")]
        public string title;
        [Tooltip("이 장의 목표 한 줄")]
        public string goal;
        [Tooltip("장이 시작될 때 코코가 하는 말 (장 제목 카드)")]
        [TextArea(2, 3)]
        public string intro;
        public List<QuestDefinition> quests = new();
    }

    public List<Chapter> chapters = new();

    private static QuestChain loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => loaded = null;

    public static QuestChain Load() => loaded != null ? loaded : (loaded = Resources.Load<QuestChain>(ResourcePath));

    public int ChapterOf(QuestDefinition quest, out int indexInChapter)
    {
        indexInChapter = -1;
        if (quest == null) return -1;
        for (int c = 0; c < chapters.Count; c++)
        {
            if (chapters[c] == null) continue;
            int i = 0;
            foreach (var q in chapters[c].quests)
            {
                if (q == null) continue;
                if (q == quest) { indexInChapter = i; return c; }
                i++;
            }
        }
        return -1;
    }

    public List<QuestDefinition> Flatten()
    {
        var list = new List<QuestDefinition>();
        foreach (var c in chapters)
            if (c != null)
                foreach (var q in c.quests)
                    if (q != null) list.Add(q);
        return list;
    }
}
