# -*- coding: utf-8 -*-
import datetime
import html
import json
import math
import os
import statistics
import sys
import webbrowser
from collections import Counter, defaultdict
from urllib.parse import urlparse


def read_input_payload():
    # Читает JSON, который OutputPipelineManager передаёт анализатору.
    raw = sys.stdin.read()
    if not raw:
        raise RuntimeError("Analyzer received empty input")

    payload = json.loads(raw)
    data = payload.get("data", [])
    meta = payload.get("meta", {})

    if not isinstance(data, list):
        data = []
    if not isinstance(meta, dict):
        meta = {}

    return data, meta


def build_report_path(analyzer_file):
    # Создаёт путь для HTML-отчёта в общей папке reports.
    root_dir = os.path.abspath(os.path.join(os.path.dirname(analyzer_file), ".."))
    reports_dir = os.path.join(root_dir, "reports")
    os.makedirs(reports_dir, exist_ok=True)

    analyzer_name = os.path.splitext(os.path.basename(analyzer_file))[0]
    timestamp = datetime.datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    return os.path.join(reports_dir, f"{analyzer_name}_{timestamp}.html")


def finish_report(file_path, html_content, result=None, open_report=True):
    # Сохраняет HTML и возвращает JSON-результат для AutoScope.
    with open(file_path, "w", encoding="utf-8", errors="ignore") as file:
        file.write(html_content)

    if open_report:
        try:
            webbrowser.open(f"file://{file_path}")
        except Exception:
            pass

    result = result or {}
    result.setdefault("status", "ok")
    result.setdefault("file", file_path)
    print(json.dumps(result, ensure_ascii=False))


def is_missing(value):
    # Единая проверка пустых значений. Особенно важно для pandas.NaN:
    # в Python bool(float("nan")) == True, поэтому обычные проверки не работают.
    if value is None:
        return True
    if isinstance(value, bool):
        return False
    if isinstance(value, float):
        return math.isnan(value) or math.isinf(value)
    try:
        import pandas as pd
        if pd.isna(value):
            return True
    except Exception:
        pass
    if isinstance(value, str):
        return value.strip().lower() in ["", "nan", "none", "null", "nat"]
    return False


def clean_string(value):
    if is_missing(value):
        return ""
    if isinstance(value, str):
        return value.strip()
    return str(value).strip()


def to_int(value):
    if is_missing(value):
        return None
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, float):
        return int(value)
    if isinstance(value, str):
        digits = "".join(ch for ch in value if ch.isdigit() or ch == "-")
        if digits in ["", "-"]:
            return None
        try:
            return int(digits)
        except ValueError:
            return None
    return None


def to_float(value):
    if is_missing(value):
        return None
    if isinstance(value, bool):
        return None
    if isinstance(value, (int, float)):
        return float(value)
    if isinstance(value, str):
        value = value.replace(",", ".")
        filtered = "".join(ch for ch in value if ch.isdigit() or ch in [".", "-"])
        if filtered in ["", "-", "."]:
            return None
        try:
            return float(filtered)
        except ValueError:
            return None
    return None


def normalize_item(item):
    # Приводит основные поля объявления к удобному для анализа виду.
    item = item if isinstance(item, dict) else {}
    normalized = dict(item)

    for field in [
        "source", "url", "brand", "model", "brand_origin_country", "sale_region",
        "transmission", "drive_type", "color", "body_type", "steering_wheel",
        "engine_model", "fuel_type", "powertrain_type", "description", "checked_at",
    ]:
        normalized[field] = clean_string(normalized.get(field))

    for field in ["price", "year", "mileage", "engine_power", "octane", "has_changes"]:
        normalized[field] = to_int(normalized.get(field))

    normalized["engine_volume"] = to_float(normalized.get("engine_volume"))

    return normalized


def normalize_data(data):
    return [normalize_item(item) for item in data]


def values(data, field):
    result = []
    for item in data:
        value = item.get(field)
        if not is_missing(value):
            result.append(value)
    return result


def numeric_values(data, field):
    result = []
    for item in data:
        value = to_float(item.get(field))
        if value is not None:
            result.append(value)
    return result


def safe_mean(numbers):
    return sum(numbers) / len(numbers) if numbers else None


def safe_median(numbers):
    return statistics.median(numbers) if numbers else None


def percentile(numbers, percent):
    if not numbers:
        return None
    numbers = sorted(numbers)
    if len(numbers) == 1:
        return numbers[0]
    position = (len(numbers) - 1) * percent
    lower = math.floor(position)
    upper = math.ceil(position)
    if lower == upper:
        return numbers[int(position)]
    lower_value = numbers[lower]
    upper_value = numbers[upper]
    return lower_value + (upper_value - lower_value) * (position - lower)


def format_number(value):
    number = to_float(value)
    if number is None:
        return "—"
    try:
        return f"{int(round(number)):,}".replace(",", " ")
    except Exception:
        return str(value)


def format_price(value):
    if to_float(value) is None:
        return "—"
    return f"{format_number(value)} ₽"


def format_float(value, digits=1):
    number = to_float(value)
    if number is None:
        return "—"
    return f"{number:.{digits}f}".replace(".", ",")


def format_percent(value, digits=1):
    number = to_float(value)
    if number is None:
        return "—"
    return f"{number:.{digits}f}%".replace(".", ",")


def h(value):
    return html.escape(clean_string(value))


def car_label(item):
    parts = [item.get("brand"), item.get("model"), item.get("year")]
    return " ".join([clean_string(part) for part in parts if not is_missing(part)]) or "Без названия"


def display_region(region):
    region = clean_string(region)
    if not region:
        return "—"
    return region


def counter_top(data, field, limit=10):
    counter = Counter()
    for item in data:
        value = clean_string(item.get(field))
        if value:
            counter[value] += 1
    return counter.most_common(limit)


def group_by(data, field):
    grouped = defaultdict(list)
    for item in data:
        key = clean_string(item.get(field)) or "Не указано"
        grouped[key].append(item)
    return grouped


def source_from_url(url):
    try:
        host = urlparse(url).netloc.lower()
    except Exception:
        return ""
    if "drom" in host:
        return "drom"
    if "auto.ru" in host:
        return "auto.ru"
    if "avito" in host:
        return "avito"
    return host


def completeness(item, fields):
    if not fields:
        return 0
    filled = 0
    for field in fields:
        value = item.get(field)
        if not is_missing(value):
            filled += 1
    return filled / len(fields) * 100


def common_required_fields():
    return ["url", "brand", "model", "price", "year", "sale_region", "mileage"]


def common_analysis_fields():
    return [
        "url", "source", "brand", "model", "price", "year", "brand_origin_country",
        "sale_region", "mileage", "transmission", "drive_type", "color", "body_type",
        "steering_wheel", "engine_power", "engine_volume", "engine_model", "fuel_type",
        "octane", "powertrain_type", "description",
    ]


def render_page(title, subtitle, sections, meta=None):
    meta = meta or {}
    created_at = datetime.datetime.now().strftime("%d.%m.%Y %H:%M:%S")
    filters = meta.get("filters", {}) if isinstance(meta.get("filters"), dict) else {}
    filters_html = render_filter_info(filters, meta)

    body = "\n".join(sections)
    return f"""<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="UTF-8">
<title>{h(title)}</title>
<style>
:root {{
    --bg: #f4f6f8;
    --card: #ffffff;
    --text: #17212b;
    --muted: #667085;
    --line: #d9e0e7;
    --accent: #1f6feb;
    --good: #16833a;
    --warn: #b7791f;
    --bad: #b42318;
}}
* {{ box-sizing: border-box; }}
body {{
    margin: 0;
    padding: 28px;
    font-family: Arial, Helvetica, sans-serif;
    background: var(--bg);
    color: var(--text);
}}
a {{ color: var(--accent); text-decoration: none; }}
a:hover {{ text-decoration: underline; }}
.header {{
    background: linear-gradient(135deg, #17212b, #2f4053);
    color: white;
    border-radius: 18px;
    padding: 28px 32px;
    margin-bottom: 22px;
}}
.header h1 {{ margin: 0 0 10px 0; font-size: 30px; }}
.header p {{ margin: 0; color: #dbe5ef; line-height: 1.5; }}
.meta {{ margin-top: 16px; font-size: 13px; color: #dbe5ef; }}
.grid {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 14px; }}
.card {{
    background: var(--card);
    border: 1px solid var(--line);
    border-radius: 14px;
    padding: 18px;
    margin-bottom: 18px;
    box-shadow: 0 1px 3px rgba(16, 24, 40, 0.06);
}}
.metric {{ background: var(--card); border: 1px solid var(--line); border-radius: 14px; padding: 18px; }}
.metric .label {{ color: var(--muted); font-size: 13px; margin-bottom: 8px; }}
.metric .value {{ font-size: 25px; font-weight: 700; }}
h2 {{ margin: 0 0 14px 0; font-size: 21px; }}
h3 {{ margin: 18px 0 10px 0; font-size: 16px; }}
p {{ line-height: 1.55; }}
table {{ width: 100%; border-collapse: collapse; background: white; }}
th, td {{ padding: 10px 9px; border-bottom: 1px solid var(--line); text-align: left; vertical-align: top; }}
th {{ color: #344054; font-size: 13px; background: #f7f9fb; }}
td {{ font-size: 14px; }}
.small {{ color: var(--muted); font-size: 13px; }}
.price {{ font-weight: 700; white-space: nowrap; }}
.badge {{ display: inline-block; padding: 4px 8px; border-radius: 999px; font-size: 12px; background: #eef4ff; color: #174ea6; }}
.badge-good {{ background: #e8f5ec; color: var(--good); }}
.badge-warn {{ background: #fff4dd; color: var(--warn); }}
.badge-bad {{ background: #ffe8e6; color: var(--bad); }}
.bar-row {{ display: grid; grid-template-columns: minmax(120px, 220px) 1fr 70px; align-items: center; gap: 10px; margin: 9px 0; }}
.bar-label {{ overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }}
.bar-bg {{ height: 13px; background: #e9eef5; border-radius: 999px; overflow: hidden; }}
.bar-fill {{ height: 100%; background: var(--accent); border-radius: 999px; }}
.note {{ border-left: 4px solid var(--accent); padding: 10px 12px; background: #f5f9ff; border-radius: 10px; }}
.warning {{ border-left-color: var(--warn); background: #fff9ed; }}
.danger {{ border-left-color: var(--bad); background: #fff4f2; }}
.footer {{ color: var(--muted); font-size: 12px; margin-top: 26px; }}
@media print {{ body {{ background: white; padding: 0; }} .card, .metric, .header {{ box-shadow: none; }} }}
</style>
</head>
<body>
<div class="header">
    <h1>{h(title)}</h1>
    <p>{h(subtitle)}</p>
    <div class="meta">Сформировано: {h(created_at)}</div>
</div>
{filters_html}
{body}
<div class="footer">Отчёт сформирован AutoScope. Числа зависят от текущей выборки, фильтров OutputPipeline и качества данных, полученных парсерами.</div>
</body>
</html>"""


def render_filter_info(filters, meta):
    if not filters and not meta:
        return ""

    items = []
    if meta:
        items.append(f"Только последние снимки: {bool_to_ru(meta.get('latestOnly'))}")
        items.append(f"Только изменённые записи: {bool_to_ru(meta.get('onlyChanged'))}")

    for label, key in [
        ("Бренд", "brand"),
        ("Модель", "model"),
        ("Регион", "sale_region"),
        ("Год от", "year_from"),
        ("Год до", "year_to"),
    ]:
        value = filters.get(key)
        if value not in [None, ""]:
            items.append(f"{label}: {value}")

    if not items:
        return ""

    return "<div class='card small'><b>Параметры выборки:</b> " + h("; ".join(items)) + "</div>"


def bool_to_ru(value):
    return "да" if value else "нет"


def metric_card(label, value, hint=""):
    hint_html = f"<div class='small'>{h(hint)}</div>" if hint else ""
    return f"<div class='metric'><div class='label'>{h(label)}</div><div class='value'>{h(value)}</div>{hint_html}</div>"


def section(title, content):
    return f"<div class='card'><h2>{h(title)}</h2>{content}</div>"


def metrics_grid(metrics):
    return "<div class='grid'>" + "".join(metrics) + "</div>"


def simple_table(headers, rows, empty_text="Нет данных для отображения"):
    if not rows:
        return f"<p class='small'>{h(empty_text)}</p>"

    head_html = "".join(f"<th>{h(header)}</th>" for header in headers)
    rows_html = []
    for row in rows:
        cells = "".join(f"<td>{cell}</td>" for cell in row)
        rows_html.append(f"<tr>{cells}</tr>")
    return f"<table><thead><tr>{head_html}</tr></thead><tbody>{''.join(rows_html)}</tbody></table>"


def link_cell(item):
    url = clean_string(item.get("url"))
    if not url:
        return "—"
    return f"<a href='{h(url)}' target='_blank'>Открыть</a>"


def car_table_rows(items, include_score=False):
    rows = []
    for item in items:
        label = h(car_label(item))
        region = h(display_region(item.get("sale_region")))
        source = h(item.get("source") or source_from_url(item.get("url")))
        score_cell = f"<b>{format_float(item.get('score'), 1)}</b>" if include_score else None
        cells = [
            label,
            f"<span class='price'>{h(format_price(item.get('price')))}</span>",
            h(format_number(item.get("mileage"))),
            region,
            source,
            link_cell(item),
        ]
        if include_score:
            cells.insert(0, score_cell)
        rows.append(cells)
    return rows


def render_bar_chart(items, max_items=10):
    if not items:
        return "<p class='small'>Нет данных для диаграммы.</p>"
    items = items[:max_items]
    max_value = max([value for _, value in items]) if items else 0
    if max_value <= 0:
        max_value = 1

    rows = []
    for label, value in items:
        width = max(2, value / max_value * 100)
        rows.append(
            f"<div class='bar-row'>"
            f"<div class='bar-label'>{h(label)}</div>"
            f"<div class='bar-bg'><div class='bar-fill' style='width:{width:.1f}%'></div></div>"
            f"<div class='small'>{h(format_number(value))}</div>"
            f"</div>"
        )
    return "".join(rows)


def summarize_price_data(data):
    prices = numeric_values(data, "price")
    return {
        "count": len(prices),
        "mean": safe_mean(prices),
        "median": safe_median(prices),
        "min": min(prices) if prices else None,
        "max": max(prices) if prices else None,
        "q1": percentile(prices, 0.25),
        "q3": percentile(prices, 0.75),
    }

# =========================================================
# PANDAS / MATPLOTLIB HELPERS
# =========================================================

def require_visual_libraries():
    # Импортируем тяжёлые библиотеки только тогда, когда анализатору реально нужны графики.
    try:
        import pandas as pd  # noqa: F401
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt  # noqa: F401
    except Exception as exc:
        raise RuntimeError(
            "Для этого анализатора нужны библиотеки pandas и matplotlib. "
            "Установите их в проектный Python: pip install pandas matplotlib. "
            f"Исходная ошибка: {exc}"
        )


def dataframe_from_data(data):
    require_visual_libraries()
    import pandas as pd

    normalized = normalize_data(data)
    df = pd.DataFrame(normalized)

    for field in common_analysis_fields():
        if field not in df.columns:
            df[field] = None

    numeric_fields = ["price", "year", "mileage", "engine_power", "engine_volume", "octane"]
    for field in numeric_fields:
        df[field] = pd.to_numeric(df[field], errors="coerce")

    text_fields = [
        "source", "url", "brand", "model", "brand_origin_country", "sale_region",
        "transmission", "drive_type", "color", "body_type", "steering_wheel",
        "engine_model", "fuel_type", "powertrain_type", "description",
    ]
    for field in text_fields:
        df[field] = df[field].fillna("").astype(str).str.strip()

    if len(df) > 0:
        empty_source = df["source"].eq("")
        if empty_source.any():
            df.loc[empty_source, "source"] = df.loc[empty_source, "url"].apply(source_from_url)

    return df


def dataframe_to_records(df, limit=None):
    records = []
    if limit is not None:
        df = df.head(limit)
    for _, row in df.iterrows():
        item = {}
        for column in df.columns:
            value = row[column]
            try:
                # pandas.isna корректно отрабатывает NaN/NaT.
                import pandas as pd
                if pd.isna(value):
                    value = None
            except Exception:
                pass
            if hasattr(value, "item"):
                try:
                    value = value.item()
                except Exception:
                    pass
            item[column] = value
        records.append(item)
    return records


def plot_to_base64(fig):
    import base64
    import io
    import matplotlib.pyplot as plt

    buffer = io.BytesIO()
    fig.tight_layout()
    fig.savefig(buffer, format="png", dpi=140, bbox_inches="tight")
    plt.close(fig)
    buffer.seek(0)
    return base64.b64encode(buffer.read()).decode("ascii")


def render_plot_image(base64_png, title=None, description=None):
    title_html = f"<h3>{h(title)}</h3>" if title else ""
    desc_html = f"<p class='small'>{h(description)}</p>" if description else ""
    return (
        f"{title_html}{desc_html}"
        f"<img src='data:image/png;base64,{base64_png}' "
        f"style='width:100%; max-width:980px; display:block; margin:8px auto 18px auto; border:1px solid var(--line); border-radius:14px; background:white;' alt='{h(title or 'chart')}'>"
    )


def empty_chart_message(text="Недостаточно данных для построения графика."):
    return f"<p class='small'>{h(text)}</p>"


def matplotlib_bar_chart(series, title, xlabel="", ylabel="", horizontal=True, limit=12):
    require_visual_libraries()
    import matplotlib.pyplot as plt

    if series is None or len(series) == 0:
        return empty_chart_message()

    series = series.dropna().head(limit)
    if len(series) == 0:
        return empty_chart_message()

    fig_height = max(3.2, min(7.5, 0.42 * len(series) + 1.4)) if horizontal else 4.6
    fig, ax = plt.subplots(figsize=(8.8, fig_height))

    labels = [str(x) for x in series.index]
    values = [float(x) for x in series.values]

    if horizontal:
        ax.barh(labels[::-1], values[::-1])
        ax.set_xlabel(xlabel)
    else:
        ax.bar(labels, values)
        ax.set_ylabel(ylabel)
        ax.tick_params(axis="x", rotation=35)

    ax.set_title(title)
    ax.grid(axis="x" if horizontal else "y", alpha=0.25)
    return render_plot_image(plot_to_base64(fig), title)


def matplotlib_histogram(values, title, xlabel="", bins=12):
    require_visual_libraries()
    import matplotlib.pyplot as plt
    import pandas as pd

    values = pd.Series(values).dropna()
    values = values[values > 0]
    if len(values) < 2:
        return empty_chart_message()

    fig, ax = plt.subplots(figsize=(8.8, 4.6))
    ax.hist(values, bins=min(bins, max(3, len(values))))
    ax.set_title(title)
    ax.set_xlabel(xlabel)
    ax.set_ylabel("Количество объявлений")
    ax.grid(axis="y", alpha=0.25)
    return render_plot_image(plot_to_base64(fig), title)


def matplotlib_line_chart(series, title, xlabel="", ylabel=""):
    require_visual_libraries()
    import matplotlib.pyplot as plt

    if series is None or len(series) == 0:
        return empty_chart_message()

    series = series.dropna()
    if len(series) == 0:
        return empty_chart_message()

    fig, ax = plt.subplots(figsize=(8.8, 4.6))
    ax.plot([str(x) for x in series.index], [float(x) for x in series.values], marker="o")
    ax.set_title(title)
    ax.set_xlabel(xlabel)
    ax.set_ylabel(ylabel)
    ax.tick_params(axis="x", rotation=35)
    ax.grid(alpha=0.25)
    return render_plot_image(plot_to_base64(fig), title)


def matplotlib_scatter(df, x_field, y_field, title, xlabel="", ylabel=""):
    require_visual_libraries()
    import matplotlib.pyplot as plt

    if df is None or len(df) == 0 or x_field not in df.columns or y_field not in df.columns:
        return empty_chart_message()

    plot_df = df[[x_field, y_field]].dropna()
    plot_df = plot_df[(plot_df[x_field] > 0) & (plot_df[y_field] > 0)]
    if len(plot_df) < 2:
        return empty_chart_message()

    fig, ax = plt.subplots(figsize=(8.8, 4.8))
    ax.scatter(plot_df[x_field], plot_df[y_field], alpha=0.65)
    ax.set_title(title)
    ax.set_xlabel(xlabel or x_field)
    ax.set_ylabel(ylabel or y_field)
    ax.grid(alpha=0.25)
    return render_plot_image(plot_to_base64(fig), title)


def pandas_metrics(df):
    if df is None or len(df) == 0:
        return {
            "records": 0,
            "uniqueUrls": 0,
            "medianPrice": None,
            "meanPrice": None,
            "minPrice": None,
            "maxPrice": None,
            "medianYear": None,
            "medianMileage": None,
        }

    prices = df["price"].dropna()
    prices = prices[prices > 0]
    years = df["year"].dropna()
    mileage = df["mileage"].dropna()
    mileage = mileage[mileage > 0]

    return {
        "records": int(len(df)),
        "uniqueUrls": int(df["url"].replace("", None).dropna().nunique()) if "url" in df.columns else 0,
        "medianPrice": float(prices.median()) if len(prices) else None,
        "meanPrice": float(prices.mean()) if len(prices) else None,
        "minPrice": float(prices.min()) if len(prices) else None,
        "maxPrice": float(prices.max()) if len(prices) else None,
        "medianYear": float(years.median()) if len(years) else None,
        "medianMileage": float(mileage.median()) if len(mileage) else None,
    }


def records_table_from_df(df, limit=50, include_score=False):
    if df is None or len(df) == 0:
        return []
    records = dataframe_to_records(df, limit=limit)
    return car_table_rows(records, include_score=include_score)
