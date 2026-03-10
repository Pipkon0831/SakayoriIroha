# -*- coding: utf-8 -*-
"""
按实验分组绘制中文统计图（增强版：带数值标注 + 综合性能雷达图）

用法：
    python plot_experiment_by_group_cn.py

要求：
    当前目录下存在 llm_experiment_output.csv

输出：
    experiment_charts_cn/
        01_不同方式成功率对比.png
        02_不同方式延迟对比.png
        03_不同方式Token消耗对比.png
        04_主要错误类型分布.png
        05_不同方式延迟箱线图.png
        06_综合性能雷达图.png
    experiment_summary_by_group.csv
"""

import os
import math
import warnings
from typing import List

import pandas as pd
import matplotlib.pyplot as plt
from matplotlib import font_manager as fm
import numpy as np

warnings.filterwarnings("ignore")

CSV_FILE = "llm_experiment_output.csv"
OUTPUT_DIR = "experiment_charts_cn"
SUMMARY_CSV = "experiment_summary_by_group.csv"


# =========================
# 字体处理：尽量自动找中文字体
# =========================
def setup_chinese_font():
    candidate_fonts = [
        "Microsoft YaHei",
        "SimHei",
        "Noto Sans CJK SC",
        "Source Han Sans SC",
        "WenQuanYi Zen Hei",
        "PingFang SC",
        "Heiti SC",
        "Arial Unicode MS",
    ]

    available = {f.name for f in fm.fontManager.ttflist}
    for font_name in candidate_fonts:
        if font_name in available:
            plt.rcParams["font.sans-serif"] = [font_name]
            plt.rcParams["axes.unicode_minus"] = False
            print(f"已使用中文字体: {font_name}")
            return

    plt.rcParams["axes.unicode_minus"] = False
    print("未找到常见中文字体，图中文字可能显示异常。")


# =========================
# 基础工具
# =========================
def ensure_dir(path: str):
    if not os.path.exists(path):
        os.makedirs(path)


def normalize_binary_series(series: pd.Series) -> pd.Series:
    def convert(x):
        if pd.isna(x):
            return 0
        if isinstance(x, bool):
            return int(x)
        if isinstance(x, (int, float)):
            return 1 if x != 0 else 0
        s = str(x).strip().lower()
        if s in ("1", "true", "yes", "y", "ok"):
            return 1
        if s in ("0", "false", "no", "n", ""):
            return 0
        return 0

    return series.map(convert)


def choose_group_column(df: pd.DataFrame) -> str:
    candidates = ["tag", "config_tag", "mode"]
    for col in candidates:
        if col in df.columns:
            if df[col].nunique(dropna=True) >= 2:
                return col
    for col in candidates:
        if col in df.columns:
            return col
    raise ValueError("数据中未找到可用分组列：tag / config_tag / mode")


def prettify_group_name(x) -> str:
    s = str(x)
    mapping = {
        "Prompt": "Prompt约束",
        "ApiJson": "API JSON",
        "SchemaValidation": "Schema校验",
        "SchemaRetry": "Schema重试",
        "prompt": "Prompt约束",
        "api_json": "API JSON",
        "schema_validation": "Schema校验",
        "schema_retry": "Schema重试",
        "0": "Prompt约束",
        "1": "API JSON",
        "2": "Schema校验",
        "3": "Schema重试",
        0: "Prompt约束",
        1: "API JSON",
        2: "Schema校验",
        3: "Schema重试",
    }
    return mapping.get(x, mapping.get(s, s))


def clean_fail_reason(x) -> str:
    if pd.isna(x):
        return "未知错误"
    s = str(x).strip()
    return s if s else "未知错误"


def safe_p95(series: pd.Series) -> float:
    series = pd.to_numeric(series, errors="coerce").dropna()
    if len(series) == 0:
        return float("nan")
    return float(series.quantile(0.95))


def safe_mean(series: pd.Series) -> float:
    series = pd.to_numeric(series, errors="coerce").dropna()
    if len(series) == 0:
        return float("nan")
    return float(series.mean())


def rate_to_percent(v: float) -> float:
    if pd.isna(v):
        return float("nan")
    return v * 100.0


def annotate_bars_from_containers(ax, containers, fmt="{:.1f}", y_offset_ratio=0.01, fontsize=9):
    """
    给柱状图自动加数值标签
    """
    ymin, ymax = ax.get_ylim()
    y_span = ymax - ymin
    offset = y_span * y_offset_ratio

    for container in containers:
        for rect in container:
            h = rect.get_height()
            if pd.isna(h):
                continue
            x = rect.get_x() + rect.get_width() / 2
            ax.text(
                x, h + offset, fmt.format(h),
                ha="center", va="bottom", fontsize=fontsize
            )


def shorten_label(s: str, max_len: int = 28) -> str:
    s = str(s)
    return s if len(s) <= max_len else s[:max_len - 3] + "..."


# =========================
# 汇总统计
# =========================
def build_summary(df: pd.DataFrame, group_col: str) -> pd.DataFrame:
    work = df.copy()

    for col in ["parse_ok", "schema_ok", "semantic_ok", "all_ok"]:
        if col in work.columns:
            work[col] = normalize_binary_series(work[col])
        else:
            work[col] = 0

    for col in ["latency_ms", "prompt_tokens", "completion_tokens", "total_tokens"]:
        if col in work.columns:
            work[col] = pd.to_numeric(work[col], errors="coerce")
        else:
            work[col] = float("nan")

    grouped = work.groupby(group_col, dropna=False)

    rows = []
    for group_name, g in grouped:
        row = {
            "分组": prettify_group_name(group_name),
            "样本数": len(g),
            "解析成功率(%)": rate_to_percent(g["parse_ok"].mean()),
            "结构成功率(%)": rate_to_percent(g["schema_ok"].mean()),
            "语义成功率(%)": rate_to_percent(g["semantic_ok"].mean()),
            "总成功率(%)": rate_to_percent(g["all_ok"].mean()),
            "平均延迟(ms)": safe_mean(g["latency_ms"]),
            "P95延迟(ms)": safe_p95(g["latency_ms"]),
            "平均PromptToken": safe_mean(g["prompt_tokens"]),
            "平均CompletionToken": safe_mean(g["completion_tokens"]),
            "平均总Token": safe_mean(g["total_tokens"]),
        }
        rows.append(row)

    summary = pd.DataFrame(rows)

    preferred_order = ["Prompt约束", "API JSON", "Schema校验", "Schema重试"]
    summary["排序键"] = summary["分组"].map(
        lambda x: preferred_order.index(x) if x in preferred_order else 999
    )
    summary = summary.sort_values(["排序键", "分组"]).drop(columns=["排序键"]).reset_index(drop=True)
    return summary


# =========================
# 各类图
# =========================
def plot_success_rates(summary: pd.DataFrame, output_dir: str):
    groups = summary["分组"].tolist()
    parse_rates = summary["解析成功率(%)"].tolist()
    schema_rates = summary["结构成功率(%)"].tolist()
    semantic_rates = summary["语义成功率(%)"].tolist()
    all_rates = summary["总成功率(%)"].tolist()

    x = np.arange(len(groups))
    width = 0.2

    plt.figure(figsize=(13, 7))
    ax = plt.gca()

    b1 = ax.bar(x - 1.5 * width, parse_rates, width=width, label="解析成功率")
    b2 = ax.bar(x - 0.5 * width, schema_rates, width=width, label="结构成功率")
    b3 = ax.bar(x + 0.5 * width, semantic_rates, width=width, label="语义成功率")
    b4 = ax.bar(x + 1.5 * width, all_rates, width=width, label="总成功率")

    ax.set_xticks(x)
    ax.set_xticklabels(groups)
    ax.set_ylabel("成功率（%）")
    ax.set_xlabel("实验方式")
    ax.set_title("不同方式成功率对比")
    ax.set_ylim(0, 108)
    ax.legend()

    annotate_bars_from_containers(ax, [b1, b2, b3, b4], fmt="{:.1f}", y_offset_ratio=0.005, fontsize=8)

    plt.tight_layout()
    plt.savefig(os.path.join(output_dir, "01_不同方式成功率对比.png"), dpi=220)
    plt.close()


def plot_latency(summary: pd.DataFrame, output_dir: str):
    groups = summary["分组"].tolist()
    mean_latency = summary["平均延迟(ms)"].tolist()
    p95_latency = summary["P95延迟(ms)"].tolist()

    x = np.arange(len(groups))
    width = 0.35

    plt.figure(figsize=(12, 7))
    ax = plt.gca()

    b1 = ax.bar(x - width / 2, mean_latency, width=width, label="平均延迟")
    b2 = ax.bar(x + width / 2, p95_latency, width=width, label="P95延迟")

    ax.set_xticks(x)
    ax.set_xticklabels(groups)
    ax.set_ylabel("延迟（毫秒）")
    ax.set_xlabel("实验方式")
    ax.set_title("不同方式延迟对比")
    ax.legend()

    annotate_bars_from_containers(ax, [b1, b2], fmt="{:.0f}", y_offset_ratio=0.008, fontsize=9)

    plt.tight_layout()
    plt.savefig(os.path.join(output_dir, "02_不同方式延迟对比.png"), dpi=220)
    plt.close()


def plot_tokens(summary: pd.DataFrame, output_dir: str):
    groups = summary["分组"].tolist()
    prompt_tokens = summary["平均PromptToken"].tolist()
    completion_tokens = summary["平均CompletionToken"].tolist()
    total_tokens = summary["平均总Token"].tolist()

    x = np.arange(len(groups))
    width = 0.25

    plt.figure(figsize=(13, 7))
    ax = plt.gca()

    b1 = ax.bar(x - width, prompt_tokens, width=width, label="平均 Prompt Token")
    b2 = ax.bar(x, completion_tokens, width=width, label="平均 Completion Token")
    b3 = ax.bar(x + width, total_tokens, width=width, label="平均总 Token")

    ax.set_xticks(x)
    ax.set_xticklabels(groups)
    ax.set_ylabel("Token 数")
    ax.set_xlabel("实验方式")
    ax.set_title("不同方式 Token 消耗对比")
    ax.legend()

    annotate_bars_from_containers(ax, [b1, b2, b3], fmt="{:.1f}", y_offset_ratio=0.01, fontsize=8)

    plt.tight_layout()
    plt.savefig(os.path.join(output_dir, "03_不同方式Token消耗对比.png"), dpi=220)
    plt.close()


def plot_fail_reasons(df: pd.DataFrame, output_dir: str, top_n: int = 10):
    work = df.copy()

    if "all_ok" not in work.columns or "fail_reason" not in work.columns:
        print("缺少 all_ok 或 fail_reason 列，跳过错误分布图。")
        return

    work["all_ok"] = normalize_binary_series(work["all_ok"])
    fails = work[work["all_ok"] == 0].copy()
    if len(fails) == 0:
        print("没有失败数据，跳过错误分布图。")
        return

    fails["fail_reason"] = fails["fail_reason"].map(clean_fail_reason)
    top_fail = fails["fail_reason"].value_counts().head(top_n)

    labels = [shorten_label(x, 32) for x in top_fail.index]
    values = top_fail.values

    plt.figure(figsize=(15, 8))
    ax = plt.gca()
    bars = ax.bar(range(len(values)), values)

    ax.set_xticks(range(len(values)))
    ax.set_xticklabels(labels, rotation=25, ha="right")
    ax.set_ylabel("出现次数")
    ax.set_xlabel("错误类型")
    ax.set_title("主要错误类型分布")

    annotate_bars_from_containers(ax, [bars], fmt="{:.0f}", y_offset_ratio=0.01, fontsize=9)

    plt.tight_layout()
    plt.savefig(os.path.join(output_dir, "04_主要错误类型分布.png"), dpi=220)
    plt.close()


def plot_latency_boxplot(df: pd.DataFrame, group_col: str, output_dir: str):
    work = df.copy()
    if "latency_ms" not in work.columns:
        print("缺少 latency_ms 列，跳过箱线图。")
        return

    work["latency_ms"] = pd.to_numeric(work["latency_ms"], errors="coerce")
    work = work.dropna(subset=["latency_ms"])

    if len(work) == 0:
        print("latency_ms 无有效数据，跳过箱线图。")
        return

    group_names = list(work[group_col].dropna().unique())
    group_names = [prettify_group_name(x) for x in group_names]

    preferred_order = ["Prompt约束", "API JSON", "Schema校验", "Schema重试"]
    if all(name in preferred_order for name in group_names):
        group_names = sorted(group_names, key=lambda x: preferred_order.index(x))

    data = []
    medians = []
    for display_name in group_names:
        s = work[work[group_col].map(prettify_group_name) == display_name]["latency_ms"].dropna()
        data.append(s.values)
        medians.append(float(np.median(s.values)) if len(s) > 0 else np.nan)

    plt.figure(figsize=(13, 7))
    ax = plt.gca()

    bp = ax.boxplot(data, labels=group_names, showfliers=True)

    ax.set_ylabel("延迟（毫秒）")
    ax.set_xlabel("实验方式")
    ax.set_title("不同方式延迟箱线图")

    # 标注中位数
    for i, median in enumerate(medians, start=1):
        if not pd.isna(median):
            ax.text(i + 0.03, median, f"{median:.0f}", fontsize=9, va="bottom")

    plt.tight_layout()
    plt.savefig(os.path.join(output_dir, "05_不同方式延迟箱线图.png"), dpi=220)
    plt.close()


def plot_radar(summary: pd.DataFrame, output_dir: str):
    """
    综合性能雷达图
    维度说明（统一转成“越大越好”）：
    - 总成功率：直接使用
    - 平均延迟得分：按组内 min-max 反向归一
    - P95延迟得分：按组内 min-max 反向归一
    - Token成本得分：按组内 min-max 反向归一（平均总Token）
    - 解析成功率：直接使用
    """

    df = summary.copy()

    # 原始指标
    total_success = df["总成功率(%)"].astype(float).values
    parse_success = df["解析成功率(%)"].astype(float).values
    mean_latency = df["平均延迟(ms)"].astype(float).values
    p95_latency = df["P95延迟(ms)"].astype(float).values
    total_token = df["平均总Token"].astype(float).values

    def reverse_minmax_score(arr):
        arr = np.array(arr, dtype=float)
        if np.nanmax(arr) == np.nanmin(arr):
            return np.ones_like(arr) * 100.0
        # 越小越好 -> 转成越大越好
        return (np.nanmax(arr) - arr) / (np.nanmax(arr) - np.nanmin(arr)) * 100.0

    mean_latency_score = reverse_minmax_score(mean_latency)
    p95_latency_score = reverse_minmax_score(p95_latency)
    token_score = reverse_minmax_score(total_token)

    categories = [
        "总成功率",
        "解析成功率",
        "平均延迟得分",
        "P95延迟得分",
        "Token成本得分",
    ]
    N = len(categories)

    angles = np.linspace(0, 2 * np.pi, N, endpoint=False).tolist()
    angles += angles[:1]

    plt.figure(figsize=(10, 10))
    ax = plt.subplot(111, polar=True)

    # 极坐标起始位置
    ax.set_theta_offset(np.pi / 2)
    ax.set_theta_direction(-1)

    plt.xticks(angles[:-1], categories, fontsize=11)
    ax.set_rlabel_position(0)
    plt.yticks([20, 40, 60, 80, 100], ["20", "40", "60", "80", "100"], fontsize=9)
    plt.ylim(0, 100)
    plt.title("不同方式综合性能雷达图", y=1.08, fontsize=16)

    for i, row in df.iterrows():
        values = [
            float(row["总成功率(%)"]),
            float(row["解析成功率(%)"]),
            float(mean_latency_score[i]),
            float(p95_latency_score[i]),
            float(token_score[i]),
        ]
        values_closed = values + values[:1]

        ax.plot(angles, values_closed, linewidth=2, label=row["分组"])
        ax.fill(angles, values_closed, alpha=0.08)

        # 在每个点附近标注具体数值
        for angle, val in zip(angles[:-1], values):
            ax.text(angle, min(val + 4, 102), f"{val:.1f}", fontsize=8, ha="center", va="center")

    plt.legend(loc="upper right", bbox_to_anchor=(1.25, 1.10))
    plt.tight_layout()
    plt.savefig(os.path.join(output_dir, "06_综合性能雷达图.png"), dpi=220, bbox_inches="tight")
    plt.close()


# =========================
# 主流程
# =========================
def main():
    setup_chinese_font()

    if not os.path.exists(CSV_FILE):
        raise FileNotFoundError(f"未找到文件：{CSV_FILE}")

    ensure_dir(OUTPUT_DIR)

    df = pd.read_csv(CSV_FILE)

    print(f"成功读取数据：{CSV_FILE}")
    print(f"总行数：{len(df)}")
    print(f"列名：{list(df.columns)}")

    group_col = choose_group_column(df)
    print(f"自动选择分组列：{group_col}")

    summary = build_summary(df, group_col)
    summary.to_csv(SUMMARY_CSV, index=False, encoding="utf-8-sig")
    print(f"已导出汇总表：{SUMMARY_CSV}")

    print("\n===== 分组汇总 =====")
    with pd.option_context("display.max_columns", None, "display.width", 200):
        print(summary)

    plot_success_rates(summary, OUTPUT_DIR)
    print("已生成：01_不同方式成功率对比.png")

    plot_latency(summary, OUTPUT_DIR)
    print("已生成：02_不同方式延迟对比.png")

    plot_tokens(summary, OUTPUT_DIR)
    print("已生成：03_不同方式Token消耗对比.png")

    plot_fail_reasons(df, OUTPUT_DIR, top_n=10)
    print("已生成：04_主要错误类型分布.png")

    plot_latency_boxplot(df, group_col, OUTPUT_DIR)
    print("已生成：05_不同方式延迟箱线图.png")

    plot_radar(summary, OUTPUT_DIR)
    print("已生成：06_综合性能雷达图.png")

    print("\n全部图表生成完成。输出目录：", OUTPUT_DIR)


if __name__ == "__main__":
    main()