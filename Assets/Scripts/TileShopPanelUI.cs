using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TileShopPanelUI : MonoBehaviour
{
    public enum ShopItemType
    {
        TilePlacement,
        DirectPurchase
    }

    [Serializable]
    public class ShopSlot
    {
        public Button button;
        public ShopItemType itemType = ShopItemType.TilePlacement;
        public TilePieceDefinition tilePrefab;
        public string itemName = "Item";
        [Min(0)]
        public int itemPrice = 1;
        public UnityEvent onPurchased;
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI priceText;
    }

    [Header("References")]
    public TimeCounterUI currencySource;
    public TilePlacementManager placementManager;
    public List<ShopSlot> slots = new List<ShopSlot>();

    void Start()
    {
        BindButtons();
        Refresh();

        if (currencySource != null)
            currencySource.ValueChanged += HandleCurrencyChanged;
    }

    void OnDestroy()
    {
        if (currencySource != null)
            currencySource.ValueChanged -= HandleCurrencyChanged;
    }

    public void Refresh()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            ShopSlot slot = slots[i];
            if (slot == null || slot.button == null)
                continue;

            if (slot.nameText != null)
                slot.nameText.text = GetDisplayName(slot);

            if (slot.priceText != null)
                slot.priceText.text = GetPrice(slot).ToString();

            slot.button.interactable = currencySource != null && currencySource.CanAfford(GetPrice(slot));
        }
    }

    private void BindButtons()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            ShopSlot slot = slots[i];
            if (slot == null || slot.button == null)
                continue;

            slot.button.onClick.RemoveAllListeners();
            ShopSlot capturedSlot = slot;
            slot.button.onClick.AddListener(() => HandleBuyClicked(capturedSlot));
        }
    }

    private void HandleBuyClicked(ShopSlot slot)
    {
        if (slot == null || currencySource == null)
            return;

        int price = GetPrice(slot);
        if (!currencySource.CanAfford(price))
            return;

        switch (slot.itemType)
        {
            case ShopItemType.TilePlacement:
                if (slot.tilePrefab == null || placementManager == null)
                    return;

                if (placementManager.TryBeginPlacement(slot.tilePrefab))
                    Refresh();
                break;

            case ShopItemType.DirectPurchase:
                if (!currencySource.TrySpend(price))
                    return;

                slot.onPurchased?.Invoke();
                Refresh();
                break;
        }
    }

    private void HandleCurrencyChanged(float _)
    {
        Refresh();
    }

    private string GetDisplayName(ShopSlot slot)
    {
        if (slot == null)
            return string.Empty;

        if (slot.itemType == ShopItemType.TilePlacement && slot.tilePrefab != null)
            return slot.tilePrefab.shopData.displayName;

        return slot.itemName;
    }

    private int GetPrice(ShopSlot slot)
    {
        if (slot == null)
            return 0;

        if (slot.itemType == ShopItemType.TilePlacement && slot.tilePrefab != null)
            return slot.tilePrefab.shopData.price;

        return Mathf.Max(0, slot.itemPrice);
    }
}
