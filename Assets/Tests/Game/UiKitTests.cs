using NUnit.Framework;
using UnityEngine;

namespace ParcelFactory.Game.Tests
{
    /// <summary>UI 부품 라이브러리(스펙 12.2): SDF 9-slice 스프라이트, 다섯 글꼴, 흰색 아이콘 19개.</summary>
    public class UiKitTests
    {
        public static readonly string[] IconNames =
        {
            "tool-conveyor", "tool-fast", "tool-bridge", "tool-pallet", "tool-table", "tool-splitter", "tool-scanner",
            "ui-pause", "ui-play", "ui-shop", "ui-status", "ui-help", "ui-save", "ui-load", "ui-new", "ui-exit", "ui-close", "ui-check", "ui-parcel",
        };

        [Test]
        public void RoundedSprites_HaveSliceBorders_MatchingRadius()
        {
            foreach (int r in new[] { UiKit.RKey, UiKit.RTile, UiKit.RButton, UiKit.RPanel, UiKit.RCard, 14 })
            {
                var s = UiSprites.Rounded(r);
                Assert.IsNotNull(s);
                float k = s.pixelsPerUnit / 100f; // 캔버스 기준 px로 환산
                for (int i = 0; i < 4; i++) Assert.AreEqual(r, s.border[i] / k, 1e-3f, "fill border " + i + " radius " + r);
                Assert.Greater(s.rect.width, s.border.x + s.border.z, "fill must keep a stretchable centre");
                Assert.AreSame(s, UiSprites.Rounded(r), "sprites must be cached");
                var ring = UiSprites.Ring(r, 1f);
                for (int i = 0; i < 4; i++) Assert.AreEqual(r, ring.border[i] / k, 1e-3f, "ring border " + i);
                // 모서리 바깥 픽셀은 투명, 가운데는 불투명(채움) / 투명(테두리)
                Assert.Less(s.texture.GetPixel(0, 0).a, 0.05f, "corner should be cut");
                Assert.Greater(s.texture.GetPixel(s.texture.width / 2, s.texture.height / 2).a, 0.99f);
                Assert.Less(ring.texture.GetPixel(ring.texture.width / 2, ring.texture.height / 2).a, 0.01f, "ring centre should be empty");
            }
        }

        [Test]
        public void ShadowSprite_IsHollowAndOffsetDown()
        {
            var s = UiSprites.Shadow(UiKit.RPanel, 10, 30, 0, out var m);
            Assert.Greater(s.border.y, s.border.w, "bottom border (offset shadow) should be larger than the top");
            Assert.Greater(m.y, m.w, "shadow should extend further below than above");
            var t = s.texture;
            Assert.Less(t.GetPixel(t.width / 2, t.height / 2).a, 0.01f, "shadow must not draw under the panel");
            var pill = UiSprites.Shadow(14, 10, 30, 28, out _);
            Assert.AreEqual(pill.rect.height - 1f, pill.border.y + pill.border.w, 1e-3f, "fixed-height shadow should not stretch vertically");
        }

        [Test]
        public void Panel_GetsRoundedSlicedImage_BorderAndShadow()
        {
            var parent = new GameObject("UiKitTestRoot", typeof(RectTransform));
            try
            {
                var p = UiKit.PanelBox("P", parent.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(200f, 80f));
                Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, p.type);
                Assert.IsNotNull(p.sprite);
                Assert.Greater(p.sprite.border.x, 0f);
                Assert.IsNotNull(p.transform.Find("Border"));
                Assert.AreEqual(0, p.transform.Find("Shadow").GetSiblingIndex(), "shadow must be the first child");
                var b = UiKit.Btn("B", parent.transform, "상점", null, icon: "ui-shop", key: "B");
                var ui = b.GetComponent<UiButton>();
                Assert.IsNotNull(ui.Icon.sprite, "button icon missing");
                Assert.AreEqual("B", ui.Key.Text.text);
                Assert.AreSame(Fonts.Num, ui.Key.Text.font, "keycap uses Sora SemiBold");
                Assert.AreSame(ui.Label, b.GetComponentInChildren<UnityEngine.UI.Text>(), "label must be the first Text");
            }
            finally { Object.DestroyImmediate(parent); }
        }

        [Test]
        public void FiveFonts_LoadAsDistinctAssets()
        {
            var fonts = new[] { Fonts.Regular, Fonts.Body, Fonts.Bold, Fonts.Num, Fonts.NumBold };
            var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < fonts.Length; i++)
            {
                Assert.IsNotNull(fonts[i], "font " + i);
                Assert.AreNotSame(builtin, fonts[i], "font " + i + " fell back to the built-in font");
                for (int j = i + 1; j < fonts.Length; j++) Assert.AreNotSame(fonts[i], fonts[j], "fonts " + i + " and " + j + " are the same asset");
            }
            Assert.AreSame(Fonts.Body, Fonts.For("배송", false));
            Assert.AreSame(Fonts.Bold, Fonts.For("₩ 40", true));
            Assert.AreSame(Fonts.NumBold, Fonts.For("−30", true));
            Assert.AreSame(Fonts.Num, Fonts.For("EXP 120", false));
        }

        [Test]
        public void NineteenIcons_ImportAsSprites()
        {
            Assert.AreEqual(19, IconNames.Length);
            foreach (var n in IconNames)
            {
                Assert.IsNotNull(Resources.Load<Sprite>("UI/Icons/" + n), n + " is not imported as a sprite");
                Assert.IsNotNull(UiKit.Icon(n), n);
            }
        }
    }
}
