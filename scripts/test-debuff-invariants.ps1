[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$mathSourcePath = Join-Path $repoRoot 'DpsAttributionMath.cs'
$statsSourcePath = Join-Path $repoRoot 'DpsStats.cs'
$debuffSourcePath = Join-Path $repoRoot 'DpsDebuffAttribution.cs'
$settingsSourcePath = Join-Path $repoRoot 'DpsMeterSettings.cs'

foreach ($requiredPath in @($mathSourcePath, $statsSourcePath, $debuffSourcePath, $settingsSourcePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "缺少不变量测试所需文件：$requiredPath"
    }
}

$runnerPath = Join-Path $env:TEMP "dps-attribution-invariants-$([guid]::NewGuid().ToString('N')).cs"
$runnerSource = @'
using System;
using System.Collections.Generic;
using System.Linq;
using Sts2DpsMeter;

public static class DpsAttributionInvariantRunner
{
    public static string Run()
    {
        var passed = 0;

        void Test(string name, Action body)
        {
            try
            {
                body();
                passed++;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"用例失败：{name}：{exception.Message}", exception);
            }
        }

        static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        static void AssertEqual(long expected, long actual, string message)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException($"{message}，期望 {expected}，实际 {actual}");
            }
        }

        static void AssertClose(double expected, double actual, double tolerance, string message)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException($"{message}，期望 {expected}，实际 {actual}");
            }
        }

        static void AssertSequence(long[] expected, long[] actual, string message)
        {
            if (!expected.SequenceEqual(actual))
            {
                throw new InvalidOperationException(
                    $"{message}，期望 [{string.Join(", ", expected)}]，实际 [{string.Join(", ", actual)}]");
            }
        }

        Test("助攻资格矩阵", () =>
        {
            AssertTrue(DpsAttributionMath.ShouldResolveDebuffTransfer(0.5f, true, true, true, 10), "有效输入应允许计算");
            AssertTrue(!DpsAttributionMath.ShouldResolveDebuffTransfer(0f, true, true, true, 10), "权重 0 不应转移");
            AssertTrue(!DpsAttributionMath.ShouldResolveDebuffTransfer(-1f, true, true, true, 10), "负权重不应转移");
            AssertTrue(!DpsAttributionMath.ShouldResolveDebuffTransfer(float.NaN, true, true, true, 10), "NaN 权重不应转移");
            AssertTrue(!DpsAttributionMath.ShouldResolveDebuffTransfer(0.5f, false, true, true, 10), "无施加者不应转移");
            AssertTrue(!DpsAttributionMath.ShouldResolveDebuffTransfer(0.5f, true, false, true, 10), "目标非敌方不应转移");
            AssertTrue(!DpsAttributionMath.ShouldResolveDebuffTransfer(0.5f, true, true, false, 10), "非能力攻击不应转移");
            AssertTrue(!DpsAttributionMath.ShouldResolveDebuffTransfer(0.5f, true, true, true, 0), "零伤害不应转移");
        });

        Test("权重归一化边界", () =>
        {
            AssertClose(0d, DpsAttributionMath.NormalizeFactor(-1f), 0d, "负权重应归零");
            AssertClose(0d, DpsAttributionMath.NormalizeFactor(0f), 0d, "零权重应保持零");
            AssertClose(0.5d, DpsAttributionMath.NormalizeFactor(0.5f), 1e-10, "半权重应保持");
            AssertClose(1d, DpsAttributionMath.NormalizeFactor(1f), 0d, "权重 1 应保持");
            AssertClose(1d, DpsAttributionMath.NormalizeFactor(2f), 0d, "超过 1 的权重应封顶");
            AssertClose(0d, DpsAttributionMath.NormalizeFactor(float.NaN), 0d, "NaN 权重应归零");
        });

        Test("额外伤害公式", () =>
        {
            AssertClose(0d, DpsAttributionMath.CalculateExtraDamage(100, 1d), 0d, "倍率 1 不应产生额外伤害");
            AssertClose(50d, DpsAttributionMath.CalculateExtraDamage(100, 2d), 1e-10, "2 倍率应产生 50 额外伤害");
            AssertClose(33.3333333333d, DpsAttributionMath.CalculateExtraDamage(100, 1.5d), 1e-8, "1.5 倍率额外伤害错误");
            AssertClose(0d, DpsAttributionMath.CalculateExtraDamage(100, double.PositiveInfinity), 0d, "无穷倍率应拒绝");
            AssertClose(0d, DpsAttributionMath.CalculateExtraDamage(100, double.NaN), 0d, "NaN 倍率应拒绝");
        });

        Test("转移总量取整与封顶", () =>
        {
            AssertEqual(49, DpsAttributionMath.ClampTransferTotal(100, 49.49d), "向下取整错误");
            AssertEqual(50, DpsAttributionMath.ClampTransferTotal(100, 49.5d), "四舍五入错误");
            AssertEqual(100, DpsAttributionMath.ClampTransferTotal(100, 200d), "转移总量应封顶");
            AssertEqual(100, DpsAttributionMath.ClampTransferTotal(100, double.PositiveInfinity), "无穷转移量应封顶");
            AssertEqual(0, DpsAttributionMath.ClampTransferTotal(100, double.NaN), "NaN 转移量应归零");
            AssertEqual(0, DpsAttributionMath.ClampTransferTotal(100, -1d), "负转移量应归零");
        });

        Test("按权重分配守恒且确定", () =>
        {
            var cases = new (long Total, double[] Weights)[]
            {
                (1, new[] { 1d, 1d }),
                (2, new[] { 1d, 2d, 3d }),
                (7, new[] { 0.25d, 0.5d, 0.25d }),
                (13, new[] { 2d, 3d, 5d, 7d }),
                (101, new[] { 0d, 1.5d, 2.5d, 9d })
            };

            foreach (var testCase in cases)
            {
                var result = DpsAttributionMath.AllocateByWeights(testCase.Total, testCase.Weights);
                AssertEqual(testCase.Total, result.Aggregate(0L, (sum, value) => sum + value), "分配总量未守恒");
                AssertTrue(result.All(value => value >= 0), "分配结果不应为负");
            }

            AssertSequence(
                new[] { 1L, 1L, 1L },
                DpsAttributionMath.AllocateByWeights(3, new[] { 1d, 1d, 1d }),
                "平均权重应按索引稳定分配余数");
            AssertSequence(
                new[] { 0L, 0L },
                DpsAttributionMath.AllocateByWeights(5, new[] { 0d, 0d }),
                "全零权重不应凭空分配");
        });

        Test("三分量分配守恒", () =>
        {
            var components = DpsAttributionMath.AllocateComponents(10, 1, 2, 7);
            AssertEqual(10, components.Damage, "三分量分配总量未守恒");
            AssertTrue(components.BlockedDamage <= 1, "格挡分配超过原分量");
            AssertTrue(components.UnblockedDamage <= 2, "未格挡分配超过原分量");
            AssertTrue(components.OverkillDamage <= 7, "溢出分配超过原分量");
        });

        Test("转移扣减守恒且越界回退", () =>
        {
            var original = new DamageComponents(10, 20, 5);
            var transfers = new[]
            {
                new DamageComponents(1, 2, 3),
                new DamageComponents(2, 3, 1)
            };
            AssertTrue(
                DpsAttributionMath.TrySubtractComponents(original, transfers, out var remaining),
                "合法转移应成功");
            AssertEqual(original.Damage, remaining.Damage + transfers.Sum(item => item.Damage), "扣减前后总量未守恒");
            AssertTrue(remaining.BlockedDamage >= 0 && remaining.UnblockedDamage >= 0 && remaining.OverkillDamage >= 0, "剩余分量不应为负");

            AssertTrue(
                !DpsAttributionMath.TrySubtractComponents(
                    original,
                    new[] { new DamageComponents(11, 0, 0) },
                    out var oversizedRemaining),
                "超量转移应失败");
            AssertEqual(original.Damage, oversizedRemaining.Damage, "失败时必须保留原分配");

            AssertTrue(
                !DpsAttributionMath.TrySubtractComponents(
                    original,
                    new[] { new DamageComponents(-1, 0, 0) },
                    out var negativeRemaining),
                "负转移应失败");
            AssertEqual(original.Damage, negativeRemaining.Damage, "负数失败时必须保留原分配");
        });

        return $"纯计算不变量通过：{passed} 项";
    }
}
'@

[System.IO.File]::WriteAllText($runnerPath, $runnerSource, [System.Text.UTF8Encoding]::new($false))

try {
    Add-Type -Path @($mathSourcePath, $runnerPath)
    Write-Output ([DpsAttributionInvariantRunner]::Run())
}
finally {
    Remove-Item -LiteralPath $runnerPath -Force -ErrorAction SilentlyContinue
}

function Assert-SourceMatches {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not [regex]::IsMatch($Text, $Pattern)) {
        throw $Message
    }
}

$statsSource = Get-Content -LiteralPath $statsSourcePath -Raw
$debuffSource = Get-Content -LiteralPath $debuffSourcePath -Raw
$settingsSource = Get-Content -LiteralPath $settingsSourcePath -Raw

$recordDamageMatch = [regex]::Match(
    $statsSource,
    'private static bool RecordDamage\([\s\S]*?private static void RecordDamageAllocation\(')
if (-not $recordDamageMatch.Success) {
    throw '无法定位 RecordDamage 实现块。'
}
Assert-SourceMatches $recordDamageMatch.Value 'countAsHit:\s*true' '基础攻击必须计为命中。'
Assert-SourceMatches $recordDamageMatch.Value 'countAsHit:\s*false' '助攻转移不得计为命中。'

$doomKillMatch = [regex]::Match(
    $statsSource,
    'public static bool RecordDoomKills\([\s\S]*?public static bool Record\(')
if (-not $doomKillMatch.Success) {
    throw '无法定位灾厄击杀记录块。'
}
Assert-SourceMatches $doomKillMatch.Value 'allowDebuffTransfer:\s*false' '灾厄击杀必须排除助攻转移。'

Assert-SourceMatches $debuffSource 'DpsAttributionMath\.ShouldResolveDebuffTransfer' '助攻资格应调用共享纯计算函数。'
Assert-SourceMatches $debuffSource 'DpsAttributionMath\.CalculateExtraDamage' '额外伤害应调用共享纯计算函数。'
Assert-SourceMatches $debuffSource 'DpsAttributionMath\.ClampTransferTotal' '转移总量应调用共享纯计算函数。'
Assert-SourceMatches $debuffSource 'DpsAttributionMath\.AllocateComponents' '三分量分配应调用共享纯计算函数。'
Assert-SourceMatches $debuffSource 'DpsAttributionMath\.AllocateByWeights' '来源权重分配应调用共享纯计算函数。'
Assert-SourceMatches $statsSource 'DpsAttributionMath\.TrySubtractComponents' '转移扣减应调用共享纯计算函数。'

Assert-SourceMatches $settingsSource 'MinDebuffDamageAttributionFactor\s*=\s*0f' '最小助攻权重必须为 0。'
Assert-SourceMatches $settingsSource 'MaxDebuffDamageAttributionFactor\s*=\s*1f' '最大助攻权重必须为 1。'
Assert-SourceMatches $settingsSource 'DebuffDamageAttributionFactorStep\s*=\s*0\.05f' '助攻权重步长必须为 0.05。'

Write-Output '分支与常量静态不变量通过：12 项'
