using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace FigmaImporter.Editor
{
    [CreateAssetMenu(menuName = "FigmaImporter/FontLinks")]
    public class FontLinks : ScriptableObject
    {
        [SerializeField] private List<FontStringPair> _fonts = new List<FontStringPair>();

        public TMP_FontAsset Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var font = _fonts?.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x?.Name) &&
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            return font?.Font;
        }

        public TMP_FontAsset GetAny(IEnumerable<string> names, out string matchedName)
        {
            matchedName = null;
            if (names == null)
            {
                return null;
            }

            foreach (var name in names)
            {
                var font = Get(name);
                if (font != null)
                {
                    matchedName = name;
                    return font;
                }
            }

            return null;
        }

        public bool AddName(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
            {
                return false;
            }

            _fonts ??= new List<FontStringPair>();
            if (_fonts.Any(x =>
                    !string.IsNullOrWhiteSpace(x?.Name) &&
                    string.Equals(x.Name, fontName, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            _fonts.Add(new FontStringPair(fontName, null));
            return true;
        }

        public bool Set(string fontName, TMP_FontAsset font)
        {
            if (string.IsNullOrWhiteSpace(fontName))
            {
                return false;
            }

            _fonts ??= new List<FontStringPair>();
            var pair = _fonts.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x?.Name) &&
                string.Equals(x.Name, fontName, StringComparison.OrdinalIgnoreCase));

            if (pair == null)
            {
                _fonts.Add(new FontStringPair(fontName, font));
                return true;
            }

            if (pair.Font == font)
            {
                return false;
            }

            pair.Font = font;
            return true;
        }

        public bool ReplaceFontReference(TMP_FontAsset oldFont, TMP_FontAsset newFont)
        {
            if (oldFont == null || newFont == null || _fonts == null)
            {
                return false;
            }

            var changed = false;
            foreach (var pair in _fonts)
            {
                if (pair == null || pair.Font != oldFont)
                {
                    continue;
                }

                pair.Font = newFont;
                changed = true;
            }

            return changed;
        }
    }

    [Serializable]
    public class FontStringPair
    {
        public string Name;
        public TMP_FontAsset Font;

        public FontStringPair(string name, TMP_FontAsset font)
        {
            Name = name;
            Font = font;
        }
    }
}
