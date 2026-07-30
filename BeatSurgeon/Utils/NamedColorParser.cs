using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatSurgeon.Utils
{
    /// <summary>
    /// Parses chat color tokens as CSS/SVG named colors (plus common aliases) or hex.
    /// Unity's <see cref="ColorUtility.TryParseHtmlString"/> only recognizes a small
    /// name set; this covers the full CSS Level 3 / SVG named-color table (~147)
    /// and accepts spaced / hyphenated / underscored forms (e.g. "light blue", "hot-pink").
    /// </summary>
    internal static class NamedColorParser
    {
        private static readonly Dictionary<string, Color> NamedColors = BuildNamedColors();

        /// <summary>
        /// Tries to parse a color starting at <paramref name="startIndex"/> in
        /// <paramref name="tokens"/>. Prefers the longest matching named color
        /// (up to 3 words), then hex via ColorUtility.
        /// </summary>
        internal static bool TryParseFromTokens(
            string[] tokens,
            int startIndex,
            out Color color,
            out int tokensConsumed)
        {
            color = default;
            tokensConsumed = 0;
            if (tokens == null || startIndex < 0 || startIndex >= tokens.Length)
                return false;

            // Longest named match first so "medium sea green" beats "medium".
            for (int length = Math.Min(3, tokens.Length - startIndex); length >= 1; length--)
            {
                string joined = string.Join(" ", tokens, startIndex, length);
                if (TryParseNamed(joined, out color))
                {
                    tokensConsumed = length;
                    return true;
                }
            }

            string single = tokens[startIndex];
            if (TryParseHex(single, out color))
            {
                tokensConsumed = 1;
                return true;
            }

            return false;
        }

        internal static bool TryParse(string input, out Color color)
        {
            if (TryParseNamed(input, out color))
                return true;
            return TryParseHex(input, out color);
        }

        private static bool TryParseNamed(string input, out Color color)
        {
            color = default;
            string key = NormalizeName(input);
            if (string.IsNullOrEmpty(key))
                return false;
            return NamedColors.TryGetValue(key, out color);
        }

        private static bool TryParseHex(string input, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string trimmed = input.Trim();
            if (ColorUtility.TryParseHtmlString(trimmed, out color))
                return true;
            if (trimmed.Length > 0 && trimmed[0] != '#')
                return ColorUtility.TryParseHtmlString("#" + trimmed, out color);
            return false;
        }

        /// <summary>Lowercase and strip spaces, hyphens, underscores.</summary>
        private static string NormalizeName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var buffer = new char[input.Length];
            int n = 0;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == ' ' || c == '-' || c == '_' || c == '\t')
                    continue;
                if (c >= 'A' && c <= 'Z')
                    c = (char)(c + 32);
                buffer[n++] = c;
            }
            return n == 0 ? string.Empty : new string(buffer, 0, n);
        }

        private static Color Rgb(byte r, byte g, byte b)
            => new Color(r / 255f, g / 255f, b / 255f, 1f);

        private static Dictionary<string, Color> BuildNamedColors()
        {
            // CSS Level 3 / SVG named colors + a few chat-friendly aliases.
            // Keys must already be NormalizeName-compatible (lowercase, no separators).
            var map = new Dictionary<string, Color>(160)
            {
                ["aliceblue"] = Rgb(240, 248, 255),
                ["antiquewhite"] = Rgb(250, 235, 215),
                ["aqua"] = Rgb(0, 255, 255),
                ["aquamarine"] = Rgb(127, 255, 212),
                ["azure"] = Rgb(240, 255, 255),
                ["beige"] = Rgb(245, 245, 220),
                ["bisque"] = Rgb(255, 228, 196),
                ["black"] = Rgb(0, 0, 0),
                ["blanchedalmond"] = Rgb(255, 235, 205),
                ["blue"] = Rgb(0, 0, 255),
                ["blueviolet"] = Rgb(138, 43, 226),
                ["brown"] = Rgb(165, 42, 42),
                ["burlywood"] = Rgb(222, 184, 135),
                ["cadetblue"] = Rgb(95, 158, 160),
                ["chartreuse"] = Rgb(127, 255, 0),
                ["chocolate"] = Rgb(210, 105, 30),
                ["coral"] = Rgb(255, 127, 80),
                ["cornflowerblue"] = Rgb(100, 149, 237),
                ["cornsilk"] = Rgb(255, 248, 220),
                ["crimson"] = Rgb(220, 20, 60),
                ["cyan"] = Rgb(0, 255, 255),
                ["darkblue"] = Rgb(0, 0, 139),
                ["darkcyan"] = Rgb(0, 139, 139),
                ["darkgoldenrod"] = Rgb(184, 134, 11),
                ["darkgray"] = Rgb(169, 169, 169),
                ["darkgrey"] = Rgb(169, 169, 169),
                ["darkgreen"] = Rgb(0, 100, 0),
                ["darkkhaki"] = Rgb(189, 183, 107),
                ["darkmagenta"] = Rgb(139, 0, 139),
                ["darkolivegreen"] = Rgb(85, 107, 47),
                ["darkorange"] = Rgb(255, 140, 0),
                ["darkorchid"] = Rgb(153, 50, 204),
                ["darkred"] = Rgb(139, 0, 0),
                ["darksalmon"] = Rgb(233, 150, 122),
                ["darkseagreen"] = Rgb(143, 188, 143),
                ["darkslateblue"] = Rgb(72, 61, 139),
                ["darkslategray"] = Rgb(47, 79, 79),
                ["darkslategrey"] = Rgb(47, 79, 79),
                ["darkturquoise"] = Rgb(0, 206, 209),
                ["darkviolet"] = Rgb(148, 0, 211),
                ["deeppink"] = Rgb(255, 20, 147),
                ["deepskyblue"] = Rgb(0, 191, 255),
                ["dimgray"] = Rgb(105, 105, 105),
                ["dimgrey"] = Rgb(105, 105, 105),
                ["dodgerblue"] = Rgb(30, 144, 255),
                ["firebrick"] = Rgb(178, 34, 34),
                ["floralwhite"] = Rgb(255, 250, 240),
                ["forestgreen"] = Rgb(34, 139, 34),
                ["fuchsia"] = Rgb(255, 0, 255),
                ["gainsboro"] = Rgb(220, 220, 220),
                ["ghostwhite"] = Rgb(248, 248, 255),
                ["gold"] = Rgb(255, 215, 0),
                ["goldenrod"] = Rgb(218, 165, 32),
                ["gray"] = Rgb(128, 128, 128),
                ["grey"] = Rgb(128, 128, 128),
                ["green"] = Rgb(0, 128, 0),
                ["greenyellow"] = Rgb(173, 255, 47),
                ["honeydew"] = Rgb(240, 255, 240),
                ["hotpink"] = Rgb(255, 105, 180),
                ["indianred"] = Rgb(205, 92, 92),
                ["indigo"] = Rgb(75, 0, 130),
                ["ivory"] = Rgb(255, 255, 240),
                ["khaki"] = Rgb(240, 230, 140),
                ["lavender"] = Rgb(230, 230, 250),
                ["lavenderblush"] = Rgb(255, 240, 245),
                ["lawngreen"] = Rgb(124, 252, 0),
                ["lemonchiffon"] = Rgb(255, 250, 205),
                ["lightblue"] = Rgb(173, 216, 230),
                ["lightcoral"] = Rgb(240, 128, 128),
                ["lightcyan"] = Rgb(224, 255, 255),
                ["lightgoldenrodyellow"] = Rgb(250, 250, 210),
                ["lightgray"] = Rgb(211, 211, 211),
                ["lightgrey"] = Rgb(211, 211, 211),
                ["lightgreen"] = Rgb(144, 238, 144),
                ["lightpink"] = Rgb(255, 182, 193),
                ["lightsalmon"] = Rgb(255, 160, 122),
                ["lightseagreen"] = Rgb(32, 178, 170),
                ["lightskyblue"] = Rgb(135, 206, 250),
                ["lightslategray"] = Rgb(119, 136, 153),
                ["lightslategrey"] = Rgb(119, 136, 153),
                ["lightsteelblue"] = Rgb(176, 196, 222),
                ["lightyellow"] = Rgb(255, 255, 224),
                ["lime"] = Rgb(0, 255, 0),
                ["limegreen"] = Rgb(50, 205, 50),
                ["linen"] = Rgb(250, 240, 230),
                ["magenta"] = Rgb(255, 0, 255),
                ["maroon"] = Rgb(128, 0, 0),
                ["mediumaquamarine"] = Rgb(102, 205, 170),
                ["mediumblue"] = Rgb(0, 0, 205),
                ["mediumorchid"] = Rgb(186, 85, 211),
                ["mediumpurple"] = Rgb(147, 112, 219),
                ["mediumseagreen"] = Rgb(60, 179, 113),
                ["mediumslateblue"] = Rgb(123, 104, 238),
                ["mediumspringgreen"] = Rgb(0, 250, 154),
                ["mediumturquoise"] = Rgb(72, 209, 204),
                ["mediumvioletred"] = Rgb(199, 21, 133),
                ["midnightblue"] = Rgb(25, 25, 112),
                ["mintcream"] = Rgb(245, 255, 250),
                ["mistyrose"] = Rgb(255, 228, 225),
                ["moccasin"] = Rgb(255, 228, 181),
                ["navajowhite"] = Rgb(255, 222, 173),
                ["navy"] = Rgb(0, 0, 128),
                ["oldlace"] = Rgb(253, 245, 230),
                ["olive"] = Rgb(128, 128, 0),
                ["olivedrab"] = Rgb(107, 142, 35),
                ["orange"] = Rgb(255, 165, 0),
                ["orangered"] = Rgb(255, 69, 0),
                ["orchid"] = Rgb(218, 112, 214),
                ["palegoldenrod"] = Rgb(238, 232, 170),
                ["palegreen"] = Rgb(152, 251, 152),
                ["paleturquoise"] = Rgb(175, 238, 238),
                ["palevioletred"] = Rgb(219, 112, 147),
                ["papayawhip"] = Rgb(255, 239, 213),
                ["peachpuff"] = Rgb(255, 218, 185),
                ["peru"] = Rgb(205, 133, 63),
                ["pink"] = Rgb(255, 192, 203),
                ["plum"] = Rgb(221, 160, 221),
                ["powderblue"] = Rgb(176, 224, 230),
                ["purple"] = Rgb(128, 0, 128),
                ["rebeccapurple"] = Rgb(102, 51, 153),
                ["red"] = Rgb(255, 0, 0),
                ["rosybrown"] = Rgb(188, 143, 143),
                ["royalblue"] = Rgb(65, 105, 225),
                ["saddlebrown"] = Rgb(139, 69, 19),
                ["salmon"] = Rgb(250, 128, 114),
                ["sandybrown"] = Rgb(244, 164, 96),
                ["seagreen"] = Rgb(46, 139, 87),
                ["seashell"] = Rgb(255, 245, 238),
                ["sienna"] = Rgb(160, 82, 45),
                ["silver"] = Rgb(192, 192, 192),
                ["skyblue"] = Rgb(135, 206, 235),
                ["slateblue"] = Rgb(106, 90, 205),
                ["slategray"] = Rgb(112, 128, 144),
                ["slategrey"] = Rgb(112, 128, 144),
                ["snow"] = Rgb(255, 250, 250),
                ["springgreen"] = Rgb(0, 255, 127),
                ["steelblue"] = Rgb(70, 130, 180),
                ["tan"] = Rgb(210, 180, 140),
                ["teal"] = Rgb(0, 128, 128),
                ["thistle"] = Rgb(216, 191, 216),
                ["tomato"] = Rgb(255, 99, 71),
                ["turquoise"] = Rgb(64, 224, 208),
                ["violet"] = Rgb(238, 130, 238),
                ["wheat"] = Rgb(245, 222, 179),
                ["white"] = Rgb(255, 255, 255),
                ["whitesmoke"] = Rgb(245, 245, 245),
                ["yellow"] = Rgb(255, 255, 0),
                ["yellowgreen"] = Rgb(154, 205, 50),

                // Chat-friendly aliases (not in CSS, or common misspellings / short forms).
                ["navyblue"] = Rgb(0, 0, 128),
                ["sky"] = Rgb(135, 206, 235),
                ["blood"] = Rgb(138, 3, 3),
                ["bloodred"] = Rgb(138, 3, 3),
                ["neonpink"] = Rgb(255, 16, 240),
                ["neongreen"] = Rgb(57, 255, 20),
                ["neonblue"] = Rgb(31, 81, 255),
                ["neon"] = Rgb(57, 255, 20),
                ["mint"] = Rgb(152, 255, 152),
                ["peach"] = Rgb(255, 218, 185),
                ["rose"] = Rgb(255, 0, 127),
                ["lilac"] = Rgb(200, 162, 200),
                ["mauve"] = Rgb(224, 176, 255),
                ["burgundy"] = Rgb(128, 0, 32),
                ["wine"] = Rgb(114, 47, 55),
                ["champagne"] = Rgb(247, 231, 206),
                ["cream"] = Rgb(255, 253, 208),
                ["ivorywhite"] = Rgb(255, 255, 240),
                ["offwhite"] = Rgb(250, 250, 250),
                ["charcoal"] = Rgb(54, 69, 79),
                ["graphite"] = Rgb(71, 74, 81),
                ["amber"] = Rgb(255, 191, 0),
                ["azureblue"] = Rgb(0, 127, 255),
                ["cobalt"] = Rgb(0, 71, 171),
                ["sapphire"] = Rgb(15, 82, 186),
                ["ruby"] = Rgb(224, 17, 95),
                ["emerald"] = Rgb(80, 200, 120),
                ["jade"] = Rgb(0, 168, 107),
                ["turquoiseblue"] = Rgb(64, 224, 208),
                ["tealblue"] = Rgb(0, 128, 128),
                ["ultramarine"] = Rgb(63, 0, 255),
                ["periwinkle"] = Rgb(204, 204, 255),
                ["seafoam"] = Rgb(159, 226, 191),
                ["seafoamgreen"] = Rgb(159, 226, 191),
                ["sunset"] = Rgb(250, 114, 104),
                ["sunrise"] = Rgb(255, 170, 51),
                ["ice"] = Rgb(214, 240, 255),
                ["iceblue"] = Rgb(214, 240, 255),
                ["frost"] = Rgb(228, 240, 255),
                ["bubblegum"] = Rgb(255, 105, 180),
                ["cottoncandy"] = Rgb(255, 188, 217),
                ["lavenderpurple"] = Rgb(150, 123, 182),
            };

            return map;
        }
    }
}
