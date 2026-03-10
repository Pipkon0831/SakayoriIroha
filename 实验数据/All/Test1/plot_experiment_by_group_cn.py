# -*- coding: utf-8 -*-
"""
按实验分组绘制中文统计图
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
    experiment_summary_by_group.csv
"""

import os
import math
import warnings
from typing import List, Dict

import pandas as pd
import matplotlib.pyplot as plt
from matplotlib import font_manager as fm

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

    # 找不到就使用默认字体，中文可能显示为方块，但脚本仍可运行
    plt.rcParams["axes.unicode_minus"] = False
    print("未找到常见中文字体，图中文字可能无法正常显示中文。")


# =========================
# 工具函数
# =========================
def ensure_dir(path: str):
    if not os.path.exists(path):
        os.makedirs(path)


def normalize_binary_series(series: pd.Series) -> pd.Series:
    """
    把 True/False, 1/0, "true"/"false" 等统一转成 1/0
    """
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
    """
    自动选择分组列：
    tag > config_tag > mode
    但要求该列至少有 2 个不同值，否则继续往后找
    """
    candidates = ["tag", "config_tag", "mode"]
    for col in candidates:
        if col in df.columns:
            nunique = df[col].nunique(dropna=True)
            if nunique >= 2:
                return col

    # 如果都不满足，最后兜底
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
    }
    return mapping.get(s, s)


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


def annotate_bars(ax, values: List[float], fmt: str = "{:.1f}", rotation: int = 0):
    for i, v in enumerate(values):
        if pd.isna(v):
            continue
        ax.text(i, v, fmt.format(v), ha="center", va="bottom", fontsize=9, rotation=rotation)


# =========================
# 统计
# =========================
def build_summary(df: pd.DataFrame, group_col: str) -> pd.DataFrame:
    work = df.copy()

    # 标准化布尔列
    for col in ["parse_ok", "schema_ok", "semantic_ok", "all_ok"]:
        if col in work.columns:
            work[col] = normalize_binary_series(work[col])
        else:
            work[col] = 0

    # 数值列
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
# 画图
# =========================
def plot_success_rates(summary: pd.DataFrame, output_dir: str):
    groups = summary["分组"].tolist()
    parse_rates = summary["解析成功率(%)"].tolist()
    schema_rates = summary["结构成功率(%)"].tolist()
    semantic_rates = summary["语义成功率(%)"].tolist()
    all_rates = summary["总成功率(%)"].tolist()

    x = range(len(groups))
    width = 0.2

    plt.figure(figsize=(12, 6))
    plt.bar([i - 1.5 * width for i in x], parse_rates, width=width, label="解析成功率")
    plt.bar([i - 0.5 * width for i in x], schema_rates, width=width, label="结构成功率")
    plt.bar([i + 0.5 * width for i in x], semantic_rates, width=width, label="语义成功率")
    plt.bar([i + 1.5 * width for i in x], all_rates, width=width, label="总成功率")

    plt.xticks(list(x), groups, rotation=0)
    plt.ylabel("成功率（%）")
    plt.xlabel("实验方式")
    plt.title("不同方式成功率对比")
    plt.ylim(0, 105)
    plt.legend()
    plt.tight_layout()
    plt.savefig(os.path.join(output_dir, "01_不同方式成功率对比.png"), dpi=200)
    plt.close()


def plot_latency(summary: pd.DataFrame, output_dir: str):
    groups = summary["分组"].tolist()
    mean_latency = summary["平均延迟(ms)"].tolist()
    p95_latency = summary["P95延迟(ms)"].tolist()

    x = range(len(groups))
    width = 0.35

    plt.figure(figsize=(11, 6))
    plt.bar([i - width / 2 for i in x], mean_latency, width=width, label="平均延迟")
    plt.bar([i + width / 2 for i in x], p95_latency, width=width, label="P95延迟")

    plt.xticks(list(x), groups)
    plt.ylabel("延迟（毫秒）")
    plt.xlabel("实验方式")
    plt.title("不同方式延迟对比")
    plt.legend()
    plt.tight_layout()
    plt.savefig(os.path.join(output_dir, "02_不同方式延迟对比.png"), dpi=200)
    plt.close()


def plot_tokens(summary: pd.DataFrame, output_dir: str):
    groups = summary["分组"].tolist()
    prompt_tokens = summary["平均PromptToken"].tolist()
    completion_tokens = summary["平均CompletionToken"].tolist()
    total_tokens = summary["平均总Token"].tolist()

    x = range(len(groups))
    width = 0.25

    plt.figure(figsize=(12, 6))
    plt.bar([i - width for i in x], prompt_tokens, width=width, label="平均 Prompt Token")
    plt.bar([i for i in x], completion_tokens, width=width, label="平均 Completion Token")
    plt.bar([i + width for i in x], total_tokens, width=width, label="平均总 Token")

    plt.xticks(list(x), groups)
    plt.ylabel("Token 数")
    plt.xlabel("实验方式")
    plt.title("不同方式 Token 消耗对比")
    plt.legend()
    plt.tight_layout()
    plt.savefig(os.path.join(output_dir, "03_不同方式Token消耗对比.png"), dpi=200)
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

    plt.figure(figsize=(13, 7))
    plt.bar(range(len(top_fail)), top_fail.values)
    plt.xticks(range(len(top_fail)), top_fail.index, rotation=25, ha="right")
    plt.ylabel("出现次数")
    plt.xlabel("错误类型")
    plt.title("主要错误类型分布")
    plt.tight_layout()
    plt.savefig(os.path.join(output_dir, "04_主要错误类型分布.png"), dpi=200)
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

    group_names_raw = list(work[group_col].dropna().unique())
    group_names = [prettify_group_name(x) for x in group_names_raw]

    preferred_order = ["Prompt约束", "API JSON", "Schema校验", "Schema重试"]
    if all(name in preferred_order for name in group_names):
        group_names = sorted(group_names, key=lambda x: preferred_order.index(x))

    data = []
    for display_name in group_names:
        candidates = work[work[group_col].map(prettify_group_name) == display_name]["latency_ms"].dropna()
        data.append(candidates.values)

    plt.figure(figsize=(12, 6))
    plt.boxplot(data, labels=group_names, showfliers=True)
    plt.ylabel("延迟（毫秒）")
    plt.xlabel("实验方式")
    plt.title("不同方式延迟箱线图")
    plt.tight_layout()
    plt.savefig(os.path.join(output_dir, "05_不同方式延迟箱线图.png"), dpi=200)
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

    print("\n全部图表生成完成。输出目录：", OUTPUT_DIR)


if __name__ == "__main__":
    main()