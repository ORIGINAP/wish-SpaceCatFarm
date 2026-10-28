using UnityEngine;

public class PowerLink : MonoBehaviour
{
    public enum Kind { Relay, Consumer }

    private static readonly Color OnColor  = new(0.45f, 0.9f, 1f, 0.6f);
    private static readonly Color OffColor = new(0.72f, 0.72f, 0.78f, 0.35f);
    private const float Width        = 0.035f;
    private const float RelayWidth   = 0.07f;
    private const int   SortingOrder = WorldConnectionLine.UnderSpritesOrder;

    private TileItem     owner;
    private Kind         kind;
    private LineRenderer line;

    public TileItem Source    { get; private set; }
    public bool     IsPowered { get; private set; }

    public event System.Action OnPowerChanged;

    public static PowerLink Attach(TileItem owner, Kind kind)
    {
        if (owner == null || owner.IsGhost) return null;
        var link = owner.gameObject.AddComponent<PowerLink>();
        link.owner = owner;
        link.kind  = kind;
        if (EnergyManager.Instance != null) EnergyManager.Instance.OnNetworkRefreshed += link.Refresh;
        if (GridManager.Instance   != null) GridManager.Instance.OnTilesChanged      += link.Refresh;
        PowerRangeOverlay.OnShowingChanged += link.ApplyVisibility;
        link.Refresh();
        return link;
    }

    private void OnDestroy()
    {
        if (EnergyManager.Instance != null) EnergyManager.Instance.OnNetworkRefreshed -= Refresh;
        if (GridManager.Instance   != null) GridManager.Instance.OnTilesChanged      -= Refresh;
        PowerRangeOverlay.OnShowingChanged -= ApplyVisibility;
    }

    private void ApplyVisibility()
    {
        if (line != null) line.enabled = Source != null && PowerRangeOverlay.Showing;
    }

    public void Refresh()
    {
        if (owner == null) return;

        bool powered = false;
        TileItem source = null;
        if (kind == Kind.Relay && owner is PowerRelay relay)
        {
            source  = relay.Upstream;
            powered = relay.IsEnergized;
        }
        else if (EnergyManager.Instance != null)
        {
            source = EnergyManager.Instance.FindPowerSource(owner.FootprintMin, owner.FootprintMax, out powered);
        }

        bool changed = powered != IsPowered;
        Source    = source;
        IsPowered = powered;
        Draw();
        if (changed) OnPowerChanged?.Invoke();
    }

    private void Draw()
    {
        if (Source == null)
        {
            if (line != null) line.enabled = false;
            return;
        }

        Vector3 from  = WorldConnectionLine.UnderSprites(owner.transform.position);
        Vector3 to    = WorldConnectionLine.UnderSprites(Source.transform.position);
        Color   color = IsPowered ? OnColor : OffColor;
        if (line == null)
            line = WorldConnectionLine.Create(from, to, color, kind == Kind.Relay ? RelayWidth : Width, SortingOrder, owner.transform);
        else
        {
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startColor = line.endColor = color;
        }
        ApplyVisibility();
    }
}
