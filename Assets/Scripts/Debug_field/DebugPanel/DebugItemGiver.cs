using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class DebugItemGiver : MonoBehaviour
{
    [System.Serializable]
    public class GiveEntry
    {
        public FarmingItemData item;
        public int             amount = 5;
    }

    [Header("지급할 아이템 목록")]
    [SerializeField] private List<GiveEntry> entries;

    [Header("UI")]
    [SerializeField] private GameObject buttonPrefab;
    [SerializeField] private Transform  buttonContainer;

    [Header("디버그 기능")]
    [SerializeField] private GameObject tutorialPanel;

    private void Start()
    {
        BuildButtons();
        AddDebugButtons();
    }

    private void BuildButtons()
    {
        foreach (var entry in entries)
        {
            if (entry.item == null) continue;
            CreateDebugButton($"{entry.item.itemName} +{entry.amount}", () => GiveItem(entry));
        }
    }

    private void AddDebugButtons()
    {
        if (tutorialPanel != null)
        {
            CreateDebugButton("Toggle Tutorial", () => {
                bool nextState = !tutorialPanel.activeSelf;
                tutorialPanel.SetActive(nextState);
            });
        }
    }

    private void CreateDebugButton(string labelText, UnityEngine.Events.UnityAction onClick)
    {
        if (buttonPrefab == null || buttonContainer == null) return;

        GameObject btn = Instantiate(buttonPrefab, buttonContainer);
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = labelText;

        var btnComp = btn.GetComponent<Button>();
        if (btnComp != null) btnComp.onClick.AddListener(onClick);
    }

    private void GiveItem(GiveEntry entry)
    {
        Inventory.Instance.AddItem(entry.item, entry.amount);
    }
    }