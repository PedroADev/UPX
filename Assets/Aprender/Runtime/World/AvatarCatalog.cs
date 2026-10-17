using System;
using UnityEngine;

namespace Aprender
{
    [Serializable]
    public sealed class AvatarDefinition
    {
        public string id;
        public string name;
        public Color color;

        public AvatarDefinition(string id, string name, Color color)
        {
            this.id = id;
            this.name = name;
            this.color = color;
        }
    }

    /// <summary>The same stable IDs are used by the UI, save data and presence server.</summary>
    public static class AvatarCatalog
    {
        public static readonly AvatarDefinition[] Items =
        {
            new AvatarDefinition("dog", "Cachorrinho", new Color32(237, 175, 102, 255)),
            new AvatarDefinition("cat", "Gatinho", new Color32(161, 158, 224, 255)),
            new AvatarDefinition("bunny", "Coelhinho", new Color32(238, 163, 191, 255)),
            new AvatarDefinition("lion", "Leãozinho", new Color32(241, 190, 67, 255)),
            new AvatarDefinition("chick", "Pintinho", new Color32(247, 212, 100, 255)),
            new AvatarDefinition("pig", "Porquinho", new Color32(239, 168, 182, 255))
        };

        public static AvatarDefinition Find(string id)
        {
            foreach (var item in Items)
                if (item.id == id) return item;
            return Items[0];
        }
    }
}
