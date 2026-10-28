using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LabSleepVote : LabExperiment
{
    [SerializeField] private int nightStart = 20;
    [SerializeField] private int nightEnd   = 5;
    [SerializeField] private int wakeHour   = 6;

    public override string Id      => "sleep";
    public override string Title   => "잠자기 투표";
    public override string Tags    => "협동";
    public override Color  Accent  => CuteUIKit.Hex(0x7F8CE0);
    public override string Summary =>
        "밤(20시~5시)이 되면 화면 위에 투표 카드가 떠요. [N] 또는 [잠자기]를 누르고, 모두 누르면 잠들었다가 아침 6시에 깨어나요. " +
        "혼자면 바로 아침! (시험용 [지금 밤으로] 버튼도 있어요)";
    public override string Status
    {
        get
        {
            var clock = GameTimeManager.Instance;
            string now = clock != null ? clock.GetTimeString() : "--:--";
            return IsNight ? $"지금 {now} · 밤이에요 — 잠자기 {votes}/{total}" : $"지금 {now} · 밤 {nightStart}시부터 투표";
        }
    }

    [Serializable] private class VoteMsg  { public bool on; }
    [Serializable] private class CountMsg { public int votes, total; }

    private readonly HashSet<ulong> voters = new();
    private readonly List<Lab.Member> members = new();
    private int   votes, total = 1;
    private bool  voted, sleeping;
    private float nextCount;

    private RectTransform hud;
    private TextMeshProUGUI hudText;
    private TextMeshProUGUI hudButton;

    private bool IsNight
    {
        get
        {
            var clock = GameTimeManager.Instance;
            if (clock == null) return false;
            int h = clock.CurrentHour;
            return h >= nightStart || h < nightEnd;
        }
    }

    protected override void AuthorityTick()
    {
        if (sleeping || Time.time < nextCount) return;
        nextCount = Time.time + 0.5f;
        Lab.CollectMembers(members);
        int before = votes, beforeTotal = total;
        if (!IsNight) voters.Clear();
        voters.RemoveWhere(id => !members.Exists(m => m.id == id));
        votes = voters.Count;
        total = Mathf.Max(1, members.Count);
        if (votes != before || total != beforeTotal) Broadcast("count", new CountMsg { votes = votes, total = total });
        if (IsNight && votes > 0 && votes >= total) StartCoroutine(Sleep());
    }

    protected override void OnRequest(ulong from, string kind, string json)
    {
        switch (kind)
        {
            case "vote":
            {
                if (!IsNight || sleeping) return;
                var m = Read<VoteMsg>(json);
                if (m == null) return;
                if (m.on) voters.Add(from); else voters.Remove(from);
                nextCount = 0f;
                break;
            }
            case "night":
            {
                var clock = GameTimeManager.Instance;
                if (clock == null || IsNight) return;
                clock.RestoreTime(clock.CurrentDay, Mathf.Clamp(nightStart + 1, 0, 23), 0);
                EnergyManager.Instance?.RefreshEnergyNetwork();
                break;
            }
        }
    }

    private IEnumerator Sleep()
    {
        sleeping = true;
        Broadcast("sleep");
        LabFx.Run(SleepFx());
        yield return new WaitForSeconds(1.1f);
        var clock = GameTimeManager.Instance;
        if (clock != null)
        {
            int day = clock.CurrentHour >= nightStart ? clock.CurrentDay + 1 : clock.CurrentDay;
            clock.RestoreTime(day, wakeHour, 0);
            EnergyManager.Instance?.RefreshEnergyNetwork();
        }
        voters.Clear();
        votes = 0;
        Broadcast("count", new CountMsg { votes = 0, total = total });
        yield return new WaitForSeconds(1.2f);
        sleeping = false;
    }

    protected override void SendState(ulong peer) => SendTo(peer, "count", new CountMsg { votes = votes, total = total });

    protected override void OnEvent(string kind, string json)
    {
        if (kind == "count")
        {
            var m = Read<CountMsg>(json);
            if (m == null) return;
            votes = m.votes;
            total = Mathf.Max(1, m.total);
            if (votes == 0) voted = false;
        }
        else if (kind == "sleep") LabFx.Run(SleepFx());
    }

    private void Vote()
    {
        if (!IsOn || !IsNight) return;
        voted = !voted;
        Sfx.Play(voted ? SfxId.UiConfirm : SfxId.UiClose);
        Request("vote", new VoteMsg { on = voted });
    }

    protected override void LocalTick()
    {
        bool night = IsNight;
        if (!night) voted = false;
        if (night && !HotkeysBlocked && GameInput.GetKeyDown(KeyCode.N)) Vote();

        if (night && hud == null) BuildHud();
        if (hud == null) return;
        hud.gameObject.SetActive(night);
        if (!night) return;
        hudText.text   = $"<size=80%>밤이에요 — 모두 잠자면 아침이 와요</size>\n잠자기 {votes}/{total}";
        hudButton.text = voted ? "자는 중… (취소)" : "잠자기 (N)";
    }

    private static IEnumerator SleepFx()
    {
        var go = new GameObject("SleepFx", typeof(RectTransform));
        UIStyleKit.CreateOverlayCanvas(go, 60);
        var img = new GameObject("Night", typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(go.transform, false);
        img.rectTransform.anchorMin = Vector2.zero;
        img.rectTransform.anchorMax = Vector2.one;
        img.rectTransform.offsetMin = img.rectTransform.offsetMax = Vector2.zero;
        img.color = new Color(0.12f, 0.1f, 0.24f, 0f);
        var z = CuteUIKit.Text(go.transform, "z Z z", 40f, new Color(1f, 1f, 1f, 0f), FontStyles.Bold);
        z.rectTransform.sizeDelta = new Vector2(400f, 80f);
        Sfx.Play(SfxId.UiClose);

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
        {
            img.color = new Color(0.12f, 0.1f, 0.24f, t);
            z.color   = new Color(1f, 1f, 1f, t);
            z.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Sin(t * 6f) * 6f);
            yield return null;
        }
        img.color = new Color(0.12f, 0.1f, 0.24f, 1f);
        yield return new WaitForSecondsRealtime(1.1f);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
        {
            img.color = new Color(0.12f, 0.1f, 0.24f, 1f - t);
            z.color   = new Color(1f, 1f, 1f, 1f - t);
            yield return null;
        }
        UnityEngine.Object.Destroy(go);
        Lab.Notify("좋은 아침이에요! 푹 잤어요");
        Sfx.Play(SfxId.GameStart, 0.7f);
    }

    private void BuildHud()
    {
        hud = LabPanelUI.HudCard("SleepHud", 250f);
        if (hud == null) return;
        hudText = CuteUIKit.Text(hud, "", 14f, CuteUIKit.Cocoa, FontStyles.Bold);
        hudText.lineSpacing = -6f;
        var btn = CuteUIKit.Button(hud, "잠자기 (N)", CuteUIKit.LavenderTag, Vote, -1f, 26f, 12f);
        hudButton = btn.GetComponentInChildren<TextMeshProUGUI>();
    }

    protected override void Cleanup()
    {
        voters.Clear();
        votes = 0;
        voted = sleeping = false;
        if (hud != null) Destroy(hud.gameObject);
        hud = null;
    }

    public override void BuildControls(Transform row)
    {
        LabPanelUI.Btn(row, "잠자기 투표", CuteUIKit.LavenderTag, Vote);
        LabPanelUI.Btn(row, "지금 밤으로 (시험용)", CuteUIKit.ButtonLatte, () => Request("night"));
    }
}
