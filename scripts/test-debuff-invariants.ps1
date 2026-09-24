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

        static void AssertVulnerableCase(
            string name,
            long baseDamage,
            double vulnerableMultiplier,
            double paperPhrogExtra,
            double crueltyExtra,
            bool hasDebilitate,
            double expectedMultiplier,
            double expectedVulnerableDamage,
            double expectedDebilitateDamage,
            double expectedAttackerDamage)
        {
            var result = DpsAttributionMath.CalculateVulnerableBreakdown(
                baseDamage,
                vulnerableMultiplier,
                paperPhrogExtra,
                crueltyExtra,
                hasDebilitate);
            AssertClose(expectedMultiplier, result.FinalMultiplier, 1e-10, $"{name}：最终倍率错误");
            AssertClose(expectedVulnerableDamage, result.VulnerableExtraDamage, 1e-8, $"{name}：易伤基础贡献错误");
            AssertClose(expectedDebilitateDamage, result.DebilitateExtraDamage, 1e-8, $"{name}：易伤强化贡献错误");
            AssertClose(expectedAttackerDamage, result.AttackerConditionalExtraDamage, 1e-8, $"{name}：攻击者条件增伤错误");
            AssertClose(
                expectedVulnerableDamage + expectedDebilitateDamage,
                result.TransferableExtraDamage,
                1e-8,
                $"{name}：可转移额外伤害错误");
        }
        static DpsPlayerShareInput Share(ulong? playerId, decimal fraction)
        {
            return new DpsPlayerShareInput(playerId, fraction);
        }

        static DpsDebuffPowerInput Power(
            DpsDebuffPowerKind kind,
            double amount,
            bool isDealerApplied,
            params DpsPlayerShareInput[] shares)
        {
            return new DpsDebuffPowerInput(kind, amount, isDealerApplied, shares);
        }

        static DpsDebuffResolutionInput Resolution(
            double paperPhrogExtra,
            double crueltyExtra,
            params DpsDebuffPowerInput[] powers)
        {
            return new DpsDebuffResolutionInput(paperPhrogExtra, crueltyExtra, powers);
        }

        static long TransferredDamage(IReadOnlyList<DpsDebuffTransferAllocation> transfers)
        {
            return transfers.Sum(item => item.Components.Damage);
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

        Test("易伤族倍率口径冻结", () =>
        {
            AssertVulnerableCase("只有易伤", 100, 1.5d, 0d, 0d, false, 1.5d, 50d, 0d, 0d);
            AssertVulnerableCase("易伤+PaperPhrog", 100, 1.5d, 0.25d, 0d, false, 1.75d, 50d, 0d, 25d);
            AssertVulnerableCase("易伤+Cruelty5", 100, 1.5d, 0d, 0.05d, false, 1.55d, 50d, 0d, 5d);
            AssertVulnerableCase("易伤+Cruelty20", 100, 1.5d, 0d, 0.2d, false, 1.7d, 50d, 0d, 20d);
            AssertVulnerableCase("易伤+Cruelty50+Debilitate", 100, 1.5d, 0d, 0.5d, true, 3d, 50d, 50d, 100d);
            AssertVulnerableCase("易伤+Debilitate", 100, 1.5d, 0d, 0d, true, 2d, 50d, 50d, 0d);
            AssertVulnerableCase("易伤+PaperPhrog+Debilitate", 100, 1.5d, 0.25d, 0d, true, 2.5d, 50d, 50d, 50d);
            AssertVulnerableCase("易伤+PaperPhrog+Cruelty20", 100, 1.5d, 0.25d, 0.2d, false, 1.95d, 50d, 0d, 45d);
            AssertVulnerableCase("易伤+PaperPhrog+Cruelty20+Debilitate", 100, 1.5d, 0.25d, 0.2d, true, 2.9d, 50d, 50d, 90d);
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

        Test("易伤正例、倍率排除与多玩家份额", () =>
        {
            var factors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(
                        DpsDebuffPowerKind.Vulnerable,
                        1.5d,
                        false,
                        Share(1, 0.4m),
                        Share(2, 0.6m))));
            AssertEqual(1, factors.Count, "易伤应生成一个可转移因子");
            AssertClose(1.5d, (double)factors[0].Multiplier, 1e-10, "易伤倍率错误");
            AssertClose(1d, factors[0].TransferableFraction, 1e-10, "纯易伤应全部可转移");

            var transfers = DpsAttributionMath.CalculateDebuffTransfers(100, 0, 0, 1, 0.5f, factors);
            AssertEqual(1, transfers.Count, "攻击者份额不应生成转移，仅队友份额应转移");
            AssertEqual(2, (long)transfers[0].PlayerId, "转移应归属非攻击者份额玩家");
            AssertEqual(10, TransferredDamage(transfers), "易伤按份额和权重转移错误");

            var excluded = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(DpsDebuffPowerKind.Vulnerable, 1d, false, Share(2, 1m))));
            AssertEqual(0, excluded.Count, "倍率不超过 1 的易伤必须排除");
        });

        Test("易伤：Paper Phrog 条件增伤不转移", () =>
        {
            var factors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0.25d,
                    0d,
                    Power(DpsDebuffPowerKind.Vulnerable, 1.5d, false, Share(2, 1m))));
            AssertEqual(1, factors.Count, "Paper Phrog 场景应生成易伤因子");
            AssertClose(2d / 3d, factors[0].TransferableFraction, 1e-10, "Paper Phrog 增伤不应进入可转移份额");
            var transfers = DpsAttributionMath.CalculateDebuffTransfers(175, 0, 0, 1, 1f, factors);
            AssertEqual(50, TransferredDamage(transfers), "Paper Phrog 场景只应转移易伤基础部分");
        });

        Test("易伤：Cruelty 条件增伤不转移", () =>
        {
            var factors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0.5d,
                    Power(DpsDebuffPowerKind.Vulnerable, 1.5d, false, Share(2, 1m))));
            AssertEqual(1, factors.Count, "Cruelty 场景应生成易伤因子");
            AssertClose(0.5d, factors[0].TransferableFraction, 1e-10, "Cruelty 增伤不应进入可转移份额");
            var transfers = DpsAttributionMath.CalculateDebuffTransfers(200, 0, 0, 1, 1f, factors);
            AssertEqual(50, TransferredDamage(transfers), "Cruelty 场景只应转移易伤基础部分");
        });

        Test("易伤强化正例与无易伤排除例", () =>
        {
            var factors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(DpsDebuffPowerKind.Vulnerable, 1.5d, false, Share(2, 1m)),
                    Power(DpsDebuffPowerKind.Debilitate, 1d, false, Share(3, 1m))));
            AssertEqual(1, factors.Count, "易伤和易伤强化应共享一个复合因子");
            AssertClose(2d, (double)factors[0].Multiplier, 1e-10, "易伤强化倍率错误");
            AssertEqual(2, factors[0].Components.Count, "复合因子应包含易伤和易伤强化两个分量");
            AssertClose(0.5d, factors[0].Components[0].Weight, 1e-10, "易伤分量权重错误");
            AssertClose(0.5d, factors[0].Components[1].Weight, 1e-10, "易伤强化分量权重错误");

            var transfers = DpsAttributionMath.CalculateDebuffTransfers(200, 0, 0, 1, 1f, factors);
            AssertEqual(2, transfers.Count, "易伤与易伤强化应生成两条来源转移");
            AssertEqual(50, (long)transfers.Single(item => item.InternalId == "ASSIST_VULNERABLE_POWER").Components.Damage, "易伤分量转移错误");
            AssertEqual(50, (long)transfers.Single(item => item.InternalId == "ASSIST_DEBILITATE_VULNERABLE").Components.Damage, "易伤强化分量转移错误");

            var excluded = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(DpsDebuffPowerKind.Debilitate, 1d, false, Share(3, 1m))));
            AssertEqual(0, excluded.Count, "没有易伤时易伤强化不得生成因子");
        });

        Test("侧击正例与攻击者自施加排除例", () =>
        {
            var factors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(DpsDebuffPowerKind.Flanking, 2d, false, Share(2, 1m))));
            AssertEqual(1, factors.Count, "非攻击者侧击应生成因子");
            var transfers = DpsAttributionMath.CalculateDebuffTransfers(100, 0, 0, 1, 1f, factors);
            AssertEqual(50, TransferredDamage(transfers), "侧击转移错误");

            var selfFactors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(DpsDebuffPowerKind.Flanking, 2d, true, Share(1, 1m))));
            AssertEqual(0, selfFactors.Count, "攻击者自己施加的侧击必须排除");
        });

        Test("击倒正例与攻击者自施加排除例", () =>
        {
            var factors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(DpsDebuffPowerKind.Knockdown, 2d, false, Share(2, 1m))));
            AssertEqual(1, factors.Count, "非攻击者击倒应生成因子");
            var transfers = DpsAttributionMath.CalculateDebuffTransfers(100, 0, 0, 1, 1f, factors);
            AssertEqual(50, TransferredDamage(transfers), "击倒转移错误");

            var selfFactors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(DpsDebuffPowerKind.Knockdown, 2d, true, Share(1, 1m))));
            AssertEqual(0, selfFactors.Count, "攻击者自己施加的击倒必须排除");
        });

        Test("减速正例与零层排除例", () =>
        {
            var factors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(DpsDebuffPowerKind.Slow, 5d, false, Share(2, 1m))));
            AssertEqual(1, factors.Count, "正层数减速应生成因子");
            AssertClose(1.5d, (double)factors[0].Multiplier, 1e-10, "减速倍率换算错误");
            var transfers = DpsAttributionMath.CalculateDebuffTransfers(100, 0, 0, 1, 1f, factors);
            AssertEqual(33, TransferredDamage(transfers), "减速转移取整错误");

            var zeroFactors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(DpsDebuffPowerKind.Slow, 0d, false, Share(2, 1m))));
            AssertEqual(0, zeroFactors.Count, "零层减速必须排除");
        });

        Test("虚弱明确排除", () =>
        {
            var factors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(DpsDebuffPowerKind.Weak, 10d, false, Share(2, 1m))));
            AssertEqual(0, factors.Count, "虚弱不得生成助攻伤害因子");
            AssertEqual(0, DpsAttributionMath.CalculateDebuffTransfers(100, 0, 0, 1, 1f, factors).Count, "虚弱不得生成转移");
        });

        Test("多 debuff 同时存在", () =>
        {
            var factors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(DpsDebuffPowerKind.Vulnerable, 1.5d, false, Share(2, 1m)),
                    Power(DpsDebuffPowerKind.Flanking, 1.5d, false, Share(2, 1m))));
            AssertEqual(2, factors.Count, "多 debuff 应分别保留因子");
            var transfers = DpsAttributionMath.CalculateDebuffTransfers(225, 0, 0, 1, 1f, factors);
            AssertEqual(2, transfers.Count, "多 debuff 应生成两条来源转移");
            AssertEqual(125, TransferredDamage(transfers), "多 debuff 组合转移错误");
        });

        Test("多玩家份额、未识别施加者与取整守恒", () =>
        {
            var factors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(
                        DpsDebuffPowerKind.Vulnerable,
                        1.5d,
                        false,
                        Share(1, 0.2m),
                        Share(2, 0.3m),
                        Share(3, 0.3m),
                        Share(null, 0.2m))));
            var transfers = DpsAttributionMath.CalculateDebuffTransfers(100, 0, 0, 1, 1f, factors);
            AssertEqual(2, transfers.Count, "攻击者和未识别施加者份额都不得生成转移");
            AssertEqual(20, TransferredDamage(transfers), "多玩家份额转移错误");
            AssertTrue(transfers.All(item => item.PlayerId != 1), "攻击者份额泄漏");
            AssertTrue(transfers.All(item => item.PlayerId == 2 || item.PlayerId == 3), "未识别施加者份额泄漏");
        });

        Test("factor=0 排除与 factor 权重线性", () =>
        {
            var factors = DpsAttributionMath.ResolveDebuffFactors(
                Resolution(
                    0d,
                    0d,
                    Power(DpsDebuffPowerKind.Vulnerable, 1.5d, false, Share(2, 1m))));
            AssertEqual(0, TransferredDamage(DpsAttributionMath.CalculateDebuffTransfers(400, 0, 0, 1, 0f, factors)), "factor=0 不应转移");
            var half = DpsAttributionMath.CalculateDebuffTransfers(400, 0, 0, 1, 0.5f, factors);
            var full = DpsAttributionMath.CalculateDebuffTransfers(400, 0, 0, 1, 1f, factors);
            AssertEqual(67, TransferredDamage(half), "factor=0.5 转移取整错误");
            AssertEqual(133, TransferredDamage(full), "factor=1 转移取整错误");
            AssertTrue(TransferredDamage(full) > TransferredDamage(half), "factor 增大应提高转移量");
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

function Assert-SourceLocksVulnerableBreakdown {
    param(
        [Parameter(Mandatory = $true)][string]$Text
    )

    if (-not [regex]::IsMatch($Text, '\bCalculateVulnerableBreakdown\b')) {
        throw '易伤族倍率必须调用共享的 CalculateVulnerableBreakdown 纯计算函数。'
    }

    if ([regex]::IsMatch($Text, '\b(?:debilitatedMultiplier|expandedMultiplier)\b')) {
        throw '易伤族倍率仍在使用 debilitatedMultiplier/expandedMultiplier 内联公式。'
    }
}
$mathSource = Get-Content -LiteralPath $mathSourcePath -Raw
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
Assert-SourceMatches $debuffSource 'DpsAttributionMath\.ResolveDebuffFactors' '适配层应调用 ResolveDebuffFactors 纯计算入口。'
Assert-SourceMatches $debuffSource 'DpsAttributionMath\.CalculateDebuffTransfers' '适配层应调用 CalculateDebuffTransfers 纯计算入口。'
if ([regex]::IsMatch($debuffSource, '\b(?:CalculateVulnerableBreakdown|CalculateExtraDamage|ClampTransferTotal|AllocateComponents|AllocateByWeights)\b')) {
    throw '游戏对象适配层仍包含倍率或分配内联计算。'
}
if ([regex]::IsMatch($debuffSource, 'private sealed record DebuffFactor(?:Component)?\(')) {
    throw '游戏对象适配层仍保留重复的 DebuffFactor 类型。'
}
Assert-SourceMatches $mathSource 'public static IReadOnlyList<DebuffFactor> ResolveDebuffFactors' '纯计算层必须提供 ResolveDebuffFactors。'
Assert-SourceMatches $mathSource 'public static IReadOnlyList<DpsDebuffTransferAllocation> CalculateDebuffTransfers' '纯计算层必须提供 CalculateDebuffTransfers。'
Assert-SourceMatches $mathSource 'private static void AddSingleFactor' '纯计算层必须集中处理单 debuff 因子。'
Assert-SourceLocksVulnerableBreakdown $mathSource
Assert-SourceMatches $statsSource 'DpsAttributionMath\.TrySubtractComponents' '转移扣减应调用共享纯计算函数。'

Assert-SourceMatches $settingsSource 'MinDebuffDamageAttributionFactor\s*=\s*0f' '最小助攻权重必须为 0。'
Assert-SourceMatches $settingsSource 'MaxDebuffDamageAttributionFactor\s*=\s*1f' '最大助攻权重必须为 1。'
Assert-SourceMatches $settingsSource 'DebuffDamageAttributionFactorStep\s*=\s*0\.05f' '助攻权重步长必须为 0.05。'

Write-Output '分支与常量静态不变量通过：13 项'
