using System;
using System.Reflection;

namespace Seedfall
{
    /// <summary>
    /// ★★ 把"主城皮肤"接进存档 —— **全程反射，零编译期依赖** ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 为什么反射，而不是直接写 `TownSkins.Owned(i)` / `TownSkins.Equip(i)`
    /// ══════════════════════════════════════════════════════════════════════
    /// ① `Assets/Scripts/Town/TownSkins.cs` 是**另一路正在写的文件**（本轮我接手时它刚落地），
    ///    它的 API 随时可能改名。直接引用 ⇒ 对方一次重构就把我这边的编译搞红 ✗
    ///    反射 ⇒ 对方改名时我这层**只是不再接管皮肤**（`Available=false`，存档里 skins 为空），
    ///    登录 / 存档 / 设置全都照常 ✓
    /// ② 恢复"已买"**不能用公开的 `TryBuy`** —— 那个方法会真的**扣金币**（`TownState.TrySpend(price)`），
    ///    读档时用它就等于"读一次档再花一遍钱" ✗
    ///    ⇒ 只能写它的两个私有静态容器 `_owned[]` / `_equipped[]`（元素就是 `bool` 与 `int`，很稳）
    /// ③ 写完之后把 `_appliedVersion` 打成"无效"，逼主城下一次 `Refresh` 重建身上的装饰 ✓
    ///    （不这么做，存档里换过的皮肤在同一会话里**看起来没变**）
    ///
    /// 失败一律**不静默**：拿不到类型 / 字段名变了 ⇒ 返回 false + `Explain()` 里写清哪一步断了。
    /// </summary>
    internal static class TownSkinBridge
    {
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static Type _type;
        private static bool _looked;
        private static string _why = "（还没检查）";

        /// <summary>皮肤系统在不在（不在 ⇒ 存档里 skins 为空，其余功能照常）。</summary>
        public static bool Available => SkinType() != null;

        /// <summary>一句话说明：能用 / 为什么不能用。</summary>
        public static string Explain() => _why;

        private static Type SkinType()
        {
            if (_looked) return _type;
            _looked = true;
            try
            {
                _type = Type.GetType("Seedfall.TownSkins, Seedfall.Runtime");
                if (_type == null)
                {
                    // 兜底：扫一遍已载入的程序集（万一以后被挪进别的 asmdef）
                    Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < all.Length; i++)
                    {
                        Type t = all[i].GetType("Seedfall.TownSkins", false);
                        if (t != null) { _type = t; break; }
                    }
                }
                _why = _type == null ? "没有网络：工程里找不到 Seedfall.TownSkins（皮肤系统未落地）" : "皮肤系统已接入";
            }
            catch (Exception e)
            {
                _type = null;
                _why = "找 TownSkins 时异常：" + e.Message;
            }
            return _type;
        }

        // ══════════════════════════════════════════════════════════════════
        // 读：拥有的 / 穿着的
        // ══════════════════════════════════════════════════════════════════

        /// <summary>把"拥有的皮肤 id"与"穿着的皮肤 id"拍进存档对象。返回是否成功。</summary>
        public static bool Capture(TownSaveData data)
        {
            if (data == null) return false;
            data.skins = Array.Empty<string>();
            data.equippedSkins = Array.Empty<string>();
            Type t = SkinType();
            if (t == null) return false;

            try
            {
                MethodInfo tryLoad = t.GetMethod("TryLoad", StaticFlags);
                if (tryLoad != null && tryLoad.GetParameters().Length == 0) tryLoad.Invoke(null, null);

                Array items = ItemsOf(t);
                if (items == null) { _why = "拿不到 TownSkins.Items"; return false; }

                MethodInfo owned = t.GetMethod("Owned", StaticFlags, null, new[] { typeof(int) }, null);
                MethodInfo equipped = t.GetMethod("IsEquipped", StaticFlags, null, new[] { typeof(int) }, null);
                if (owned == null || equipped == null) { _why = "TownSkins.Owned/IsEquipped 签名变了"; return false; }

                var ownedIds = new System.Collections.Generic.List<string>(items.Length);
                var equipIds = new System.Collections.Generic.List<string>(items.Length);
                for (int i = 0; i < items.Length; i++)
                {
                    object def = items.GetValue(i);
                    string id = FieldString(def, "id");
                    if (string.IsNullOrEmpty(id)) continue;
                    if ((bool)owned.Invoke(null, new object[] { i })) ownedIds.Add(id);
                    if ((bool)equipped.Invoke(null, new object[] { i })) equipIds.Add(id);
                }
                data.skins = ownedIds.ToArray();
                data.equippedSkins = equipIds.ToArray();
                _why = $"皮肤系统已接入（拥有 {ownedIds.Count} 件 / 穿着 {equipIds.Count} 件）";
                return true;
            }
            catch (Exception e)
            {
                _why = "读皮肤状态失败：" + Unwrap(e);
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 写：按存档还原
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 按存档还原"拥有 / 穿着"。**不花任何金币**（直接写容器数组）。
        /// 返回 false 时 <paramref name="note"/> 有原因（调用方打进日志，不影响读档本身）。
        /// </summary>
        public static bool Restore(TownSaveData data, out string note)
        {
            note = null;
            Type t = SkinType();
            if (t == null) { note = _why; return false; }
            if (data == null) { note = "存档为空"; return false; }

            try
            {
                MethodInfo tryLoad = t.GetMethod("TryLoad", StaticFlags);
                if (tryLoad != null && tryLoad.GetParameters().Length == 0 && !(bool)tryLoad.Invoke(null, null))
                {
                    note = "TownSkins 载入失败：" + (t.GetProperty("LastError", StaticFlags)?.GetValue(null) as string ?? "?");
                    return false;
                }

                Array items = ItemsOf(t);
                if (items == null) { note = "拿不到 TownSkins.Items"; return false; }

                FieldInfo ownedField = t.GetField("_owned", StaticFlags);
                FieldInfo equipField = t.GetField("_equipped", StaticFlags);
                if (ownedField == null || equipField == null) { note = "TownSkins 的 _owned/_equipped 改名了"; return false; }

                var ownedArr = ownedField.GetValue(null) as bool[];
                var equipArr = equipField.GetValue(null) as int[];
                if (ownedArr == null || equipArr == null) { note = "TownSkins 的容器还没初始化"; return false; }

                Array.Clear(ownedArr, 0, ownedArr.Length);
                for (int i = 0; i < equipArr.Length; i++) equipArr[i] = -1;

                int ownedCount = 0, equippedCount = 0;

                if (data.skins != null)
                {
                    for (int k = 0; k < data.skins.Length; k++)
                    {
                        int index = IndexOfId(items, data.skins[k]);
                        if (index < 0) { Log.Warn(LoginSettings.Tag + $"存档里的皮肤 {data.skins[k]} 不在当前 town_skins 表里，已跳过"); continue; }
                        if (index < ownedArr.Length) { ownedArr[index] = true; ownedCount++; }
                    }
                }

                if (data.equippedSkins != null)
                {
                    for (int k = 0; k < data.equippedSkins.Length; k++)
                    {
                        int index = IndexOfId(items, data.equippedSkins[k]);
                        if (index < 0) continue;
                        if (!ownedArr[index]) ownedArr[index] = true;              // 穿着必然拥有
                        int slotIndex = SlotIndex(t, FieldString(items.GetValue(index), "slot"));
                        if (slotIndex >= 0 && slotIndex < equipArr.Length)
                        {
                            equipArr[slotIndex] = index;
                            equippedCount++;
                        }
                    }
                }

                // 逼主城下一次 Refresh 重建身上的装饰（否则"换了皮肤看着没换"）
                FieldInfo applied = t.GetField("_appliedVersion", StaticFlags);
                if (applied != null && applied.FieldType == typeof(int)) applied.SetValue(null, int.MinValue);

                note = $"皮肤：拥有 {ownedCount} 件 / 穿着 {equippedCount} 件";
                return true;
            }
            catch (Exception e)
            {
                note = "还原皮肤失败：" + Unwrap(e);
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 小工具
        // ══════════════════════════════════════════════════════════════════

        private static Array ItemsOf(Type t)
        {
            PropertyInfo p = t.GetProperty("Items", StaticFlags);
            var arr = p?.GetValue(null) as Array;
            if (arr != null) return arr;
            FieldInfo f = t.GetField("Items", StaticFlags);
            return f?.GetValue(null) as Array;
        }

        private static string FieldString(object def, string name)
        {
            if (def == null) return null;
            FieldInfo f = def.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null) return null;
            var v = f.GetValue(def);
            return v as string;
        }

        private static int IndexOfId(Array items, string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < items.Length; i++)
                if (FieldString(items.GetValue(i), "id") == id) return i;
            return -1;
        }

        /// <summary>`"Hat"` → `(int)SkinSlot.Hat`。槽位枚举是另一路定义的，同样只按名字找。</summary>
        private static int SlotIndex(Type townSkins, string slotName)
        {
            if (string.IsNullOrEmpty(slotName)) return -1;
            try
            {
                Type slotType = townSkins.Assembly.GetType("Seedfall.SkinSlot", false);
                if (slotType == null || !slotType.IsEnum) return -1;
                object value = Enum.Parse(slotType, slotName, true);
                return Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static string Unwrap(Exception e)
            => e.InnerException != null ? e.InnerException.Message : e.Message;
    }
}
