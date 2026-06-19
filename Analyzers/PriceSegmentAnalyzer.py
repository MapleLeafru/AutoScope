# -*- coding: utf-8 -*-
import os
import sys

sys.path.append(os.path.join(os.path.dirname(__file__), "Lib"))
from ReportUtils import *


def assign_segment(price, q1, q3, low_limit, high_limit):
    if price is None:
        return "Без цены"
    if low_limit is not None and price < low_limit:
        return "Сильно ниже рынка"
    if high_limit is not None and price > high_limit:
        return "Сильно выше рынка"
    if q1 is not None and price <= q1:
        return "Нижний ценовой сегмент"
    if q3 is not None and price >= q3:
        return "Верхний ценовой сегмент"
    return "Средний ценовой сегмент"


def segment_badge(segment):
    css = "badge"
    lower = str(segment).lower()
    if "ниже" in lower or "нижний" in lower:
        css += " badge-good"
    elif "выше" in lower or "верхний" in lower:
        css += " badge-warn"
    elif "без" in lower:
        css += " badge-bad"
    return f"<span class='{css}'>{h(segment)}</span>"


def rows_from_df(df, limit=50):
    rows = []
    for item in dataframe_to_records(df, limit=limit):
        rows.append([
            segment_badge(item.get("segment", "")),
            h(car_label(item)),
            f"<span class='price'>{h(format_price(item.get('price')))}</span>",
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

    prices = df["price"].dropna()
    prices = prices[prices > 0]

    q1 = float(prices.quantile(0.25)) if len(prices) else None
    median = float(prices.quantile(0.50)) if len(prices) else None
    q3 = float(prices.quantile(0.75)) if len(prices) else None
    iqr = q3 - q1 if q1 is not None and q3 is not None else None
    low_limit = q1 - 1.5 * iqr if iqr is not None else None
    high_limit = q3 + 1.5 * iqr if iqr is not None else None

    df = df.copy()
    df["segment"] = df["price"].apply(lambda value: assign_segment(value if value == value else None, q1, q3, low_limit, high_limit))

    segment_order = [
        "Сильно ниже рынка",
        "Нижний ценовой сегмент",
        "Средний ценовой сегмент",
        "Верхний ценовой сегмент",
        "Сильно выше рынка",
        "Без цены",
    ]
    segment_counts = df["segment"].value_counts().reindex(segment_order).dropna()

    metric_html = metrics_grid([
        metric_card("Объявлений с ценой", format_number(len(prices))),
        metric_card("Q1", format_price(q1), "25% объявлений дешевле этого значения"),
        metric_card("Медиана", format_price(median), "Центр ценового распределения"),
        metric_card("Q3", format_price(q3), "75% объявлений дешевле этого значения"),
        metric_card("Нижняя граница выбросов", format_price(low_limit)),
        metric_card("Верхняя граница выбросов", format_price(high_limit)),
    ])

    notes = []
    if len(prices) < 5:
        notes.append("В выборке мало объявлений с ценой, поэтому сегментация может быть нестабильной.")
    else:
        notes.append(
            f"Ценовые сегменты рассчитаны по квартилям: нижний сегмент до {format_price(q1)}, "
            f"средний между {format_price(q1)} и {format_price(q3)}, верхний от {format_price(q3)}."
        )
        notes.append("Сильно дешёвые и сильно дорогие объявления считаются по правилу межквартильного размаха. Это не доказательство ошибки, а повод посмотреть объявление вручную.")

    cheap = df[df["segment"].isin(["Сильно ниже рынка", "Нижний ценовой сегмент"])].sort_values("price", ascending=True)
    expensive = df[df["segment"].isin(["Сильно выше рынка", "Верхний ценовой сегмент"])].sort_values("price", ascending=False)

    sections = [
        metric_html,
        section("Пояснение", "".join([f"<p class='note'>{h(note)}</p>" for note in notes])),
        section("Распределение по ценовым сегментам", matplotlib_bar_chart(segment_counts, "Количество объявлений по ценовым сегментам", "Количество", horizontal=True)),
        section("Распределение цен", matplotlib_histogram(df["price"], "Гистограмма цен", "Цена, ₽")),
        section("Цена по годам", matplotlib_scatter(df, "year", "price", "Цена и год выпуска", "Год", "Цена, ₽")),
        section("Самые доступные предложения", simple_table(["Сегмент", "Автомобиль", "Цена", "Год", "Пробег", "Регион", "Ссылка"], rows_from_df(cheap, 35))),
        section("Самые дорогие предложения", simple_table(["Сегмент", "Автомобиль", "Цена", "Год", "Пробег", "Регион", "Ссылка"], rows_from_df(expensive, 35))),
    ]

    html_page = render_page(
        "Ценовые сегменты рынка",
        "Статический отчёт с расчётом квартилей, выбросов и графиками распределения цены.",
        sections,
        meta=meta,
    )

    finish_report(file_path, html_page, {
        "status": "ok",
        "file": file_path,
        "recordsCount": int(len(df)),
        "pricedRecordsCount": int(len(prices)),
        "q1": q1,
        "median": median,
        "q3": q3,
        "lowOutlierLimit": low_limit,
        "highOutlierLimit": high_limit,
    })


if __name__ == "__main__":
    main()
