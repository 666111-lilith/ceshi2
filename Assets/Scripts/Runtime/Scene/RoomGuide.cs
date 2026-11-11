using UnityEngine;

namespace Palworks
{
    /// <summary>Inspector notes for the editable camera / card-layout prototype.</summary>
    public sealed class RoomGuide : MonoBehaviour
    {
        [TextArea(6, 16)] public string notes =
            "PALWORKS · 卡片场景原型\n" +
            "Play 后用 WASD 移动。人物在 X/Z 地面上走动，Y 为高度。\n" +
            "场景中所有环境物件均为可编辑卡片；碰撞体放在独立分组。\n" +
            "Camera：正交、浅俯视。Environment：Cult of the Lamb 原素材 + URP 双面裁剪。\n" +
            "不包含战斗、表演层、投影解谜或升级系统。\n" +
            "直接移动 / 复制场景里的卡片即可继续布置；不会在运行时重建场景。";
        public Vector2 walkableX = new Vector2(-9f, 9f);
        public Vector2 walkableZ = new Vector2(-1.8f, 1.8f);
    }
}
