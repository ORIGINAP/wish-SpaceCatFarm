using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AutoSeller : TileItem
{
    [Header("판매기 설정")]
    [SerializeField] private float sellInterval = 5f;
    [SerializeField, Min(1)] private int maxPerCycle = 10;

    [Header("실시간 상태")]
    [SerializeField] private long totalEarned;

    private static readonly Color CoinBeamColor = new Color(1f, 0.82f, 0.30f, 1f);

    private readonly List<FarmingItemData> buffer = new();
    private FlashBeam flashBeam;

    public override string AbilityText => $"{sellInterval:0.#}초마다 이어 준 창고의 작물을 {maxPerCycle}개까지 팔아요";
    public override float  WorkRate    => sellInterval > 0f ? maxPerCycle / sellInterval : 0f;

    private void Start()
    {
        if (IsGhost) return;
        if (!NetSession.IsReplica) StartCoroutine(SellLoop());
        flashBeam = FlashBeam.Attach(transform, FlashBeam.Kind.Generic);
        LinkRules.HintOnPlace(this);
    }

    private IEnumerator SellLoop()
    {
        var wait = new WaitForSeconds(sellInterval);
        while (true)
        {
            yield return wait;
            SellOnce();
        }
    }

    private void SellOnce()
    {
        var shop     = ShopManager.Instance;
        var currency = CurrencyManager.Instance;
        if (shop == null || currency == null) return;

        int  budget = maxPerCycle;
        long earned = 0;
        for (int i = 0; i < Links.Count && budget > 0; i++)
        {
            if (Links[i] is not ItemStorage storage) continue;

            int soldHere = 0;
            buffer.Clear();
            buffer.AddRange(storage.StockedItems);
            foreach (var item in buffer)
            {
                if (budget <= 0) break;
                if (item == null || !item.isCropProduct || !ShopManager.IsSellable(item)) continue;
                int removed = storage.RemoveItem(item, budget);
                if (removed <= 0) continue;
                earned   += (long)shop.SellPrice(item) * removed;
                budget   -= removed;
                soldHere += removed;
            }
            if (soldHere > 0)
                flashBeam?.Flash(storage.transform.position, transform.position, CoinBeamColor, 0.05f, 0.4f);
        }

        if (earned <= 0) return;
        currency.Add(earned);
        totalEarned += earned;
        Sfx.PlayAt(SfxId.Sell, transform.position, 0.7f);
    }
}
