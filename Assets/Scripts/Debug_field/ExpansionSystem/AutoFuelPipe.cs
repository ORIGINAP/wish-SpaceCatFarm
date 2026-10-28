using UnityEngine;

public class AutoFuelPipe : TileItem
{
    public override string AbilityText => "발전소 연료가 떨어지면 이어 준 창고의 연료를 보내요 (파이프끼리 이으면 멀리까지)";

    private void Start()
    {
        if (IsGhost) return;
        LinkRules.HintOnPlace(this);
    }
}
