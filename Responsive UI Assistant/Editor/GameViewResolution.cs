using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ResponsiveUIAssistant
{
    public static class GameViewResolution
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        public static bool Valid(Vector2 value) => value.x >= 16 && value.y >= 16 && value.x <= 8192 && value.y <= 8192 && value.x == Mathf.Round(value.x) && value.y == Mathf.Round(value.y);
        public static string Aspect(Vector2 value)
        {
            int a = (int)value.x, b = (int)value.y;
            if (a <= 0 || b <= 0) return "—";
            if ((long)a * 10 == (long)b * 16) return "16:10";
            int x = a, y = b;
            while (y != 0) { int remainder = x % y; x = y; y = remainder; }
            return $"{a / x}:{b / x}";
        }
        public static Vector2 Set(Vector2 resolution, bool focus = true)
        {
            if (!Valid(resolution)) throw new ArgumentException("Resolusi harus berupa piksel bulat, antara 16 dan 8192 pada setiap sisi.");
            var assembly = typeof(EditorWindow).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes", true);
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance", Flags).GetValue(null);
            var group = sizesType.GetProperty("currentGroup", Flags).GetValue(sizes);
            var groupType = group.GetType();
            int total = (int)groupType.GetMethod("GetTotalCount", Flags).Invoke(group, null), index = -1;
            for (int i = 0; i < total; i++)
            {
                var size = groupType.GetMethod("GetGameViewSize", Flags).Invoke(group, new object[] { i });
                var type = size.GetType();
                if ((int)type.GetProperty("width", Flags).GetValue(size) == (int)resolution.x && (int)type.GetProperty("height", Flags).GetValue(size) == (int)resolution.y && type.GetProperty("sizeType", Flags).GetValue(size).ToString() == "FixedResolution") { index = i; break; }
            }
            if (index < 0)
            {
                var type = assembly.GetType("UnityEditor.GameViewSize", true);
                var mode = Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType", true), "FixedResolution");
                var size = Activator.CreateInstance(type, new object[] { mode, (int)resolution.x, (int)resolution.y, "Responsive UI Preview" });
                groupType.GetMethod("AddCustomSize", Flags).Invoke(group, new[] { size });
                index = total;
            }
            var gameType = assembly.GetType("UnityEditor.GameView", true);
            var game = EditorWindow.GetWindow(gameType);
            int previous = (int)gameType.GetProperty("selectedSizeIndex", Flags).GetValue(game);
            try
            {
                gameType.GetMethod("SizeSelectionCallback", Flags).Invoke(game, new object[] { index, null });
                game.Repaint();
                var actual = (Vector2)gameType.GetProperty("targetRenderSize", Flags).GetValue(game);
                if ((actual - resolution).sqrMagnitude > 1) throw new InvalidOperationException($"Game View dibatasi menjadi {actual.x:F0} × {actual.y:F0}; target tidak tercapai.");
                if (focus) game.Focus();
                return actual;
            }
            catch
            {
                gameType.GetMethod("SizeSelectionCallback", Flags).Invoke(game, new object[] { previous, null });
                throw;
            }
        }
    }
}
