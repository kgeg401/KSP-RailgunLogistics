using System;
using System.Collections.Generic;
using UnityEngine;

namespace RailgunSandbox
{
    // Native IMGUI controls: cached glass textures, readable Segoe UI, no web overlay.
    public sealed class AeroMenu : IDisposable
    {
        readonly List<Texture2D> textures = new List<Texture2D>();
        readonly Font font;
        public readonly GUIStyle Window, Header, Card, SelectedCard, Title, Heading, Text, Small, Footer, White, Eyebrow, Badge, GoodBadge,
            Button, SelectedButton, Primary, Input, Quiet, Warning;
        public readonly Texture2D Orb;
        static readonly Color Ink = new Color(.08f, .22f, .32f);
        static readonly Color Muted = new Color(.29f, .43f, .51f);
        public AeroMenu()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 15);
            Window = Box(new Color(.72f, .91f, 1, .95f), new Color(.36f, .67f, .84f, .96f), new Color(.9f, 1, 1, .95f), 14);
            Window.padding = new RectOffset(14, 14, 12, 14);
            Header = Box(new Color(.15f, .74f, .95f), new Color(.03f, .32f, .61f), new Color(.66f, .95f, 1), 12, true);
            Header.padding = new RectOffset(14, 14, 10, 10);
            Card = Box(new Color(1, 1, 1, .98f), new Color(.9f, .97f, 1, .97f), new Color(.62f, .81f, .89f), 10);
            Card.padding = new RectOffset(14, 14, 10, 10);
            SelectedCard = Box(new Color(.94f, 1, .94f), new Color(.82f, .96f, .88f), new Color(.22f, .63f, .44f), 10);
            SelectedCard.padding = new RectOffset(12, 12, 9, 9);
            Title = Label(24, Color.white, FontStyle.Bold); Title.wordWrap = false;
            Heading = Label(17, Ink, FontStyle.Bold);
            Text = Label(14, Ink);
            Small = Label(12, Muted);
            Footer = Label(12, Ink);
            White = Label(12, new Color(.92f, .99f, 1));
            Eyebrow = Label(11, new Color(.08f, .43f, .59f), FontStyle.Bold);
            Badge = Box(new Color(.88f, .96f, 1), new Color(.73f, .89f, .96f), new Color(.52f, .75f, .85f), 8);
            Badge.font = font; Badge.fontSize = 11; Badge.alignment = TextAnchor.MiddleCenter; Badge.normal.textColor = Ink;
            Badge.padding = new RectOffset(9, 9, 3, 3); Badge.wordWrap = false;
            GoodBadge = new GUIStyle(Badge); GoodBadge.normal.background = Glass(new Color(.83f, .98f, .68f), new Color(.45f, .79f, .39f), new Color(.29f, .6f, .31f), 8, true);
            Button = Action(new Color(.98f, 1, 1), new Color(.68f, .88f, .97f), new Color(.36f, .65f, .79f), Ink);
            SelectedButton = Action(new Color(.67f, .94f, 1), new Color(.19f, .64f, .84f), new Color(.05f, .43f, .63f), new Color(.02f, .19f, .3f));
            Primary = Action(new Color(.68f, .94f, .37f), new Color(.15f, .57f, .26f), new Color(.08f, .4f, .18f), Color.white);
            Primary.fontSize = 17; Primary.fontStyle = FontStyle.Bold;
            Input = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 17, alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 5, 5), border = new RectOffset(9, 9, 9, 9) };
            Input.normal.background = Glass(Color.white, new Color(.95f, .99f, 1), new Color(.37f, .65f, .78f), 8);
            Input.normal.textColor = Ink; Input.focused.background = Glass(Color.white, new Color(.91f, .99f, 1), new Color(.05f, .55f, .79f), 8);
            Input.focused.textColor = Ink;
            Quiet = new GUIStyle(Button) { fontSize = 12, padding = new RectOffset(9, 9, 4, 4) };
            Warning = Label(12, new Color(.53f, .23f, .06f));
            Orb = Globe();
        }
        GUIStyle Label(int size, Color color, FontStyle weight = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = weight, wordWrap = true,
                padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), richText = false };
            style.normal.textColor = color; return style;
        }
        GUIStyle Box(Color top, Color bottom, Color rim, int radius, bool gloss = false) => new GUIStyle {
            normal = { background = Glass(top, bottom, rim, radius, gloss) }, border = new RectOffset(14, 14, 14, 14),
            margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(10, 10, 8, 8) };
        GUIStyle Action(Color top, Color bottom, Color rim, Color text)
        {
            var style = Box(top, bottom, rim, 10, true);
            style.font = font; style.fontSize = 13; style.alignment = TextAnchor.MiddleCenter; style.normal.textColor = text;
            style.hover.background = Glass(Color.Lerp(top, Color.white, .3f), Color.Lerp(bottom, Color.white, .15f), rim, 10, true);
            style.hover.textColor = text;
            style.active.background = Glass(bottom, top, rim, 10); style.active.textColor = text;
            style.onNormal.background = style.active.background; style.onNormal.textColor = text;
            style.focused.background = style.hover.background; style.focused.textColor = text;
            style.wordWrap = true; return style;
        }
        Texture2D Glass(Color top, Color bottom, Color rim, float radius, bool gloss = false)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
                float dx = Mathf.Max(radius - x - .5f, x + .5f - (size - radius));
                float dy = Mathf.Max(radius - y - .5f, y + .5f - (size - radius));
                float edge = radius - Mathf.Sqrt(Mathf.Max(0, dx) * Mathf.Max(0, dx) + Mathf.Max(0, dy) * Mathf.Max(0, dy));
                edge = Mathf.Min(edge, Mathf.Min(Mathf.Min(x + .5f, size - x - .5f), Mathf.Min(y + .5f, size - y - .5f)));
                var color = Color.Lerp(bottom, top, y / 63f);
                if (gloss && y > 33) color = Color.Lerp(color, Color.white, .15f + .2f * (y - 33) / 30f);
                color = Color.Lerp(rim, color, Mathf.Clamp01(edge - .5f));
                color.a *= Mathf.Clamp01(edge + .5f); pixels[y * size + x] = color;
            }
            texture.SetPixels(pixels); texture.Apply(false, true); textures.Add(texture); return texture;
        }
        Texture2D Globe()
        {
            const int size = 96; var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
                float u = (x - 47.5f) / 44, v = (y - 47.5f) / 44, r = Mathf.Sqrt(u * u + v * v);
                if (r > 1.02f) continue;
                var color = Color.Lerp(new Color(.02f, .27f, .58f), new Color(.17f, .85f, 1), (v + 1) / 2);
                if (v < -.17f + .21f * Mathf.Sin(u * 5) && v > -.65f + .13f * Mathf.Cos(u * 4)) color = new Color(.35f, .79f, .25f);
                float shine = Mathf.Exp(-((u + .3f) * (u + .3f) * 8 + (v - .52f) * (v - .52f) * 24));
                color = Color.Lerp(color, Color.white, shine * .83f);
                if (r > .94f) color = Color.Lerp(color, new Color(.85f, 1, 1), .7f);
                color.a = Mathf.Clamp01((1.02f - r) * 40); pixels[y * size + x] = color;
            }
            texture.SetPixels(pixels); texture.Apply(false, true); textures.Add(texture); return texture;
        }
        public void Step(string number, string title)
        {
            GUILayout.BeginHorizontal(); GUILayout.Label(number, Badge, GUILayout.Width(24), GUILayout.Height(22));
            GUILayout.Space(8); GUILayout.Label(title, Heading, GUILayout.Height(23)); GUILayout.EndHorizontal(); GUILayout.Space(7);
        }
        public void Dispose()
        {
            foreach (var texture in textures) UnityEngine.Object.Destroy(texture);
            if (font != null) UnityEngine.Object.Destroy(font);
        }
    }
}
