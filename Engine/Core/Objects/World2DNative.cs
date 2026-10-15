// SpriteRenderer (serialized state; animatable through m_Sprite / m_Color / m_FlipX …).
using System;
using MiniUnity;

namespace UnityEngine
{
    public partial class SpriteRenderer
    {
        public SpriteRenderer() { }
        public Sprite sprite { get => NRef<Sprite>("m_Sprite"); set { NSetRef("m_Sprite", value); NSetB("m_WasSpriteAssigned", value != null); } }
        public Color color { get => NGet("m_Color") != null ? NColor("m_Color") : Color.white; set => NSetColor("m_Color", value); }
        public bool flipX { get => NBool("m_FlipX"); set => NSetB("m_FlipX", value); }
        public bool flipY { get => NBool("m_FlipY"); set => NSetB("m_FlipY", value); }
        public SpriteDrawMode drawMode { get => (SpriteDrawMode)NInt("m_DrawMode"); set => NSetI("m_DrawMode", (int)value); }
        public Vector2 size { get => NGet("m_Size") != null ? NVec2("m_Size") : Vector2.one; set => NSetVec2("m_Size", value); }
        public float adaptiveModeThreshold { get => NFloat("m_AdaptiveModeThreshold", 0.5f); set => NSetF("m_AdaptiveModeThreshold", value); }
        public SpriteTileMode tileMode { get => (SpriteTileMode)NInt("m_SpriteTileMode"); set => NSetI("m_SpriteTileMode", (int)value); }
        public SpriteMaskInteraction maskInteraction { get => (SpriteMaskInteraction)NInt("m_MaskInteraction"); set => NSetI("m_MaskInteraction", (int)value); }
        public SpriteSortPoint spriteSortPoint { get => (SpriteSortPoint)NInt("m_SpriteSortPoint"); set => NSetI("m_SpriteSortPoint", (int)value); }
        public void RegisterSpriteChangeCallback(Events.UnityAction<SpriteRenderer> callback) { }
        public void UnregisterSpriteChangeCallback(Events.UnityAction<SpriteRenderer> callback) { }
    }
}
