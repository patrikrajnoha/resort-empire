using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Stable editor-only art identifiers, independent of screen/shared folder placement.</summary>
public static class ResortArtCatalog
{
    private static readonly Dictionary<string, string> Guids = new Dictionary<string, string>
    {
        { "AreaUnlock/check", "9bc65bea43d14bc4ea4027d7a364b4fb" },
        { "AreaUnlock/coins", "53dd110ea0f823d478100a1f0549de2f" },
        { "AreaUnlock/compass", "ce536d53cc27d89499570d3613725978" },
        { "AreaUnlock/relax", "90c2daaf84850ff4eb975b6f26abe120" },
        { "AreaUnlock/unlock", "83f44a5791dc1454897dfce23ae4807c" },
        { "AreaUnlock/worker", "09e3fb1ee22424d44a31d9b6a873c2d3" },
        { "UpgradeAvailable/drinks", "07629f9a7b4a5514491b98089a3e3618" },
        { "UpgradeAvailable/food", "7d13930c0b91081429e2f72a01f121ef" },
        { "UpgradeAvailable/ocean-view", "f0ccd138f46189a40ac9de8de534d653" },
        { "UpgradeAvailable/relax", "e4196ee58e0717a4985a336470e97cbf" },
        { "UpgradeAvailable/rooms", "259553fc032db1140afe42af67350ed9" },
        { "UpgradeAvailable/tropical-panel", "f42efefc1196c984f88e608756292dd9" },
        { "UpgradeAvailable/upgrade-emblem", "881ed6e6720f312439d2a52b5d34ab6b" },
        { "WaveComplete/coins", "2c3e383f7126bd74f86819daa1f4feb3" },
        { "WaveComplete/confetti", "7dec6542ec139f44aa2dc8851796128d" },
        { "WaveComplete/energy", "9ece4092b68281643ad1c8895ef476c5" },
        { "WaveComplete/rating-empty", "96f2da0e83997464d899f8a9d4f07080" },
        { "WaveComplete/rating-star", "48ea136af62dc8b4e967cdff592f1eb1" },
        { "WaveComplete/resort-emblem", "41e8a2a8ef0df4d408b06d9152bf16dd" },
    };

    public static Sprite LoadSprite(string set, string name)
    {
        string key = set + "/" + name;
        if (!Guids.TryGetValue(key, out var guid)) throw new ArgumentException("Unknown art: " + key);
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
        if (!sprite) throw new InvalidOperationException("Missing Sprite (2D and UI): " + key);
        return sprite;
    }
}
