# -*- coding: utf-8 -*-
import os
import sys

sys.path.append(os.path.join(os.path.dirname(__file__), "Lib"))
from ReportUtils import *


FIELDS_TO_CHECK = [
    "url", "source", "brand", "model", "price", "year", "sale_region", "mileage",
    "transmission", "drive_type", "color", "body_type", "steering_wheel",
    "engine_power", "engine_volume", "fuel_type", "description",
]

KEY_FIELDS = ["url", "brand", "model", "price", "year", "sale_region", "mileage"]


def field_completeness(df):
    rows = []
    total = len(df)
    for field in FIELDS_TO_CHECK:
        if field not in df.columns:
            filled = 0
        else:
            series = df[field]
            if field in ["price", "year", "mileage", "engine_power", "engine_volume"]:
                filled = int(series.dropna().shape[0])
            else:
                filled = int(series.fillna("").astype(str).str.strip().ne("").sum())
        percent = filled / total * 100 if total else 0
        rows.append({"field": field, "filled": filled, "missing": total - filled, "percent": percent})
    return rows


def completeness_rows(rows):
    result = []
    for row in rows:
        status = "Хорошо" if row["percent"] >= 80 else "Средне" if row["percent"] >= 50 else "Плохо"
        css = "badge-good" if status == "Хорошо" else "badge-warn" if status == "Средне" else "badge-bad"
        result.append([
            h(row["field"]),
            h(format_number(row["filled"])),
            h(format_number(row["missing"])),
            h(format_percent(row["percent"])),
            f"<span class='badge {css}'>{h(status)}</span>",
        ])
    return result


def problematic_rows(df, limit=40):
    items = []
    for _, row in df.iterrows():
        item = row.to_dict()
        filled = 0
        for field in KEY_FIELDS:
            value = item.get(field)
            if value is not None and str(value).strip() != "" and str(value).strip().lower() != "nan":
                filled += 1
        item["key_completeness"] = filled / len(KEY_FIELDS) * 100
        items.append(item)

    items = sorted(items, key=lambda item: item.get("key_completeness", 0))[:limit]
    rows = []
    for item in items:
        rows.append([
            h(format_percent(item.get("key_completeness"))),
            h(car_label(item)),
            h(format_price(item.get("price"))),
            h(format_number(item.get("year"))),
            h(format_number(item.get("mileage"))),
            h(display_region(item.get("sale_region"))),
            link_cell(item),
        ])
    return rows


def main():
    data_raw, meta = read_input_payload()
    df = dataframe_from_data(data_raw)
    file_path = build_report_path(__file__)

    rows = field_completeness(df)
    total = len(df)
    duplicates = int(df["url"].replace("", None).dropna().duplicated().sum()) if total and "url" in df.columns else 0
    key_fill = []
    for _, row in df.iterrows():
        item = row.to_dict()
        key_fill.append(completeness(item, KEY_FIELDS))
    avg_key = sum(key_fill) / len(key_fill) if key_fill else 0

    overall = sum([row["percent"] for row in rows]) / len(rows) if rows else 0

    metric_html = metrics_grid([
        metric_card("Записей", format_number(total)),
        metric_card("Дубликатов URL", format_number(duplicates)),
        metric_card("Средняя заполненность", format_percent(overall)),
        metric_card("Ключевые поля", format_percent(avg_key), "url, бренд, модель, цена, год, регион, пробег"),
    ])

    notes = []
    if duplicates > 0:
        notes.append(f"В выборке найдено {duplicates} повторов URL. Это может быть нормально для истории проверок, но для рыночной статистики лучше использовать последние снимки.")
    if avg_key < 70:
        notes.append("Заполненность ключевых полей низкая. Часть аналитики может быть неточной, особенно оценка выгодности и сравнение по цене.")
    else:
        notes.append("Ключевые поля заполнены достаточно хорошо для базового анализа рынка.")
    notes.append("Разные площадки могут заполнять характеристики по-разному. Поэтому качество данных полезно смотреть перед любыми выводами о рынке.")

    import pandas as pd
    completeness_df = pd.DataFrame(rows).sort_values("percent", ascending=True)
    completeness_series = completeness_df.set_index("field")["percent"]

    missing_series = completeness_df.set_index("field")["missing"].sort_values(ascending=False)
    source_counts = df["source"].replace("", None).dropna().value_counts() if total else pd.Series(dtype=float)

    sections = [
        metric_html,
        section("Вывод по качеству данных", "".join([f"<p class='note'>{h(note)}</p>" for note in notes])),
        section("Графики качества", 
            matplotlib_bar_chart(completeness_series, "Заполненность полей, %", "Заполненность, %", horizontal=True, limit=24) +
            matplotlib_bar_chart(missing_series, "Количество пропусков по полям", "Пропусков", horizontal=True, limit=20) +
            matplotlib_bar_chart(source_counts, "Записи по источникам", "Количество", horizontal=True, limit=10)
        ),
        section("Таблица заполненности", simple_table(["Поле", "Заполнено", "Пусто", "Заполненность", "Оценка"], completeness_rows(rows))),
        section("Записи с низкой заполненностью ключевых полей", simple_table(["Заполненность", "Автомобиль", "Цена", "Год", "Пробег", "Регион", "Ссылка"], problematic_rows(df))),
    ]

    html_page = render_page(
        "Качество данных",
        "Проверка полноты, дублей и пригодности выборки для последующего анализа.",
        sections,
        meta=meta,
    )

    finish_report(file_path, html_page, {
        "status": "ok",
        "file": file_path,
        "recordsCount": total,
        "duplicatesCount": duplicates,
        "averageCompleteness": overall,
        "keyFieldsCompleteness": avg_key,
    })


if __name__ == "__main__":
    main()
