using System;
using System.Collections.Generic;
using Godot;

namespace Sts2DpsMeter;

// 每种来源只创建一次纹理，不依赖字体字形；原生资源仅由 HUD 主线程访问。
internal static class DpsHudSourceIcons
{
    internal const int ColumnSize = 16;
    private static readonly Dictionary<DamageSourceType, Texture2D> Textures = new();

    internal static Texture2D GetTexture(DamageSourceType type)
    {
        type = Normalize(type);
        if (Textures.TryGetValue(type, out var cached) && GodotObject.IsInstanceValid(cached)) return cached;

        using var image = new Image();
        var error = image.LoadSvgFromString(GetSvg(type), 2f);
        Texture2D texture;
        if (error == Error.Ok)
        {
            texture = ImageTexture.CreateFromImage(image);
        }
        else
        {
            // 极端情况下 SVG 模块不可用，仍显示独立于字体的问号像素图。
            GD.PushWarning($"[{ModEntry.ModId}] 来源图标 {type} 解码失败（{error}），使用位图回退。");
            using var fallback = Image.CreateFromData(8, 8, false, Image.Format.Rgba8, CreateFallbackPixels());
            texture = ImageTexture.CreateFromImage(fallback);
        }
        Textures[type] = texture;
        return texture;
    }

    internal static DamageSourceType Normalize(DamageSourceType type) =>
        Enum.IsDefined(type) ? type : DamageSourceType.Unknown;

    // 使用 SVG 基础图元和路径，整图 64px 采样后缩入固定 16px 列。
    internal static string GetSvg(DamageSourceType type)
    {
        var shape = Normalize(type) switch
        {
            DamageSourceType.Card => """
                <rect x="5" y="3" width="14" height="18" rx="2"/>
                <path d="M12 7 15 11 12 15 9 11Z M9 18h6"/>
                """,
            DamageSourceType.Power => "<path d=\"m13 2-8 12h6l-1 8 9-13h-7Z\"/>",
            DamageSourceType.Relic => "<path d=\"m12 3 8 7-8 11-8-11Z M4 10h16 M8 6l4 15 4-15\"/>",
            DamageSourceType.Potion => """
                <path d="M9 3h6 M10 3v6L5 17q-2 4 3 4h8q5 0 3-4l-5-8V3 M8 14h8"/>
                """,
            DamageSourceType.Orb => """
                <circle cx="12" cy="12" r="9"/><path d="M7 12a5 5 0 0 1 5-5 M15 15h.01"/>
                """,
            DamageSourceType.Pet => """
                <ellipse cx="12" cy="16" rx="5" ry="4"/>
                <ellipse cx="5" cy="10" rx="2" ry="3"/><ellipse cx="19" cy="10" rx="2" ry="3"/>
                <ellipse cx="9" cy="5" rx="2" ry="3"/><ellipse cx="15" cy="5" rx="2" ry="3"/>
                """,
            DamageSourceType.Enemy => "<path d=\"M5 7 3 3l6 3h6l6-3-2 4 2 5-4 8H7l-4-8Z M8 11h1 M15 11h1 M9 16h6\"/>",
            DamageSourceType.Event => "<path d=\"m12 2 3 6 7 1-5 5 1 7-6-3-6 3 1-7-5-5 7-1Z\"/>",
            DamageSourceType.Self => "<path d=\"m9 4-6 6 6 6 M3 10h10a7 7 0 0 1 0 14\" transform=\"translate(1 -2)\"/>",
            _ => "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M9 9a3 3 0 1 1 4 3l-1 1v1 M12 17h.01\"/>"
        };
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24">
            <g fill="none" stroke="#ffffff" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">{shape}</g>
            </svg>
            """;
    }

    internal static byte[] CreateFallbackPixels()
    {
        string[] mask = ["00111000", "01000100", "00000100", "00001000", "00010000", "00000000", "00010000", "00000000"];
        var pixels = new byte[8 * 8 * 4];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (mask[y][x] != '1') continue;
                var offset = (y * 8 + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = pixels[offset + 3] = 255;
            }
        }
        return pixels;
    }
}
