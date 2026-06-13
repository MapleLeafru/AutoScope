# -*- coding: utf-8 -*-
import os
import sys

sys.path.append(os.path.join(os.path.dirname(__file__), "Lib"))
from ReportUtils import *


def build_observations(df, metrics):
    if df is None or len(df) == 0:
        return ["В выборке нет записей. Сначала запустите парсер или измените фильтры OutputPipeline."]

    notes = []
    notes.append(
        f"В выборке {format_number(metrics['records'])} записей, из них уникальных ссылок: {format_number(metrics['uniqueUrls'])}."
    )

    if metrics.get("medianPrice"):
        notes.append(
            f"Медианная цена составляет {format_price(metrics['medianPrice'])}, средняя цена — {format_price(metrics['meanPrice'])}. "
            "Если средняя заметно выше медианной, в выборке есть дорогие объявления, которые тянут показатель вверх."
        )
    else:
        notes.append("В выборке мало корректных цен, поэтому ценовой анализ ограничен.")

    top_brand = df["brand"].replace("", None).dropna().value_counts().head(1)
    if len(top_brand):
        notes.append(f"Самый частый бренд в данных: {top_brand.index[0]} ({format_number(top_brand.iloc[0])} объявл.).")

    top_region = df["sale_region"].replace("", None).dropna().value_counts().head(3)
    if len(top_region):
        regions = ", ".join([f"{name} ({format_number(count)})" for name, count in top_region.items()])
        notes.append(f"Наиболее заметные регионы продажи: {regions}.")

    source_count = df["source"].replace("", None).dropna().nunique()
    if source_count > 1:
        notes.append("Выборка собрана из нескольких источников. При сравнении цен стоит учитывать, что площадки могут по-разному заполнять поля и по-разному ранжировать объявления.")

    return notes


def group_price_table(df, field, limit=12):
    grouped = (
        df.dropna(subset=["price"])
        .query("price > 0")
        .groupby(field, dropna=False)["price"]
        .agg(["count", "median", "mean"])
        .sort_values("count", ascending=False)
        .head(limit)
    )

    rows = []
    for key, row in grouped.iterrows():
        if not str(key).strip():
            continue
        rows.append([
            h(key),
            h(format_number(row["count"])),
            h(format_price(row["median"])),
            h(format_price(row["mean"])),
        ])
    return rows


def main():
    data_raw, meta = read_input_payload()
    df = dataframe_from_data(data_raw)
    file_path = build_report_path(__file__)
    metrics = pandas_metrics(df)

    metric_html = metrics_grid([
        metric_card("Записей", format_number(metrics["records"]), "После фильтров OutputPipeline"),
        metric_card("Уникальных URL", format_number(metrics["uniqueUrls"]), "Полезно для контроля дублей"),
        metric_card("Медианная цена", format_price(metrics["medianPrice"]), "Главная ориентировочная цена"),
        metric_card("Средняя цена", format_price(metrics["meanPrice"]), "Чувствительна к дорогим объявлениям"),
        metric_card("Минимальная цена", format_price(metrics["minPrice"])),
        metric_card("Максимальная цена", format_price(metrics["maxPrice"])),
        metric_card("Медианный год", format_number(metrics["medianYear"])),
        metric_card("Медианный пробег", format_number(metrics["medianMileage"]), "км"),
    ])

    notes_html = "".join([f"<p class='note'>{h(note)}</p>" for note in build_observations(df, metrics)])

    top_brands = df["brand"].replace("", None).dropna().value_counts().head(12)
    top_models = df["model"].replace("", None).dropna().value_counts().head(12)
    top_regions = df["sale_region"].replace("", None).dropna().value_counts().head(12)
    top_sources = df["source"].replace("", None).dropna().value_counts().head(8)

    year_counts = df["year"].dropna().astype(int).value_counts().sort_index()
    price_year_df = df.dropna(subset=["price", "year"]).query("price > 0").copy()
    price_year_df["year_int"] = price_year_df["year"].astype(int)
    price_by_year = price_year_df.groupby("year_int")["price"].median().sort_index()

    chart_sections = [
        section("Графики распределения", 
            matplotlib_bar_chart(top_brands, "Топ брендов по количеству объявлений", "Количество", horizontal=True) +
            matplotlib_bar_chart(top_models, "Топ моделей по количеству объявлений", "Количество", horizontal=True) +
            matplotlib_bar_chart(top_regions, "Топ регионов по количеству объявлений", "Количество", horizontal=True)
        ),
        section("Цены и годы", 
            matplotlib_histogram(df["price"], "Распределение цен", "Цена, ₽") +
            matplotlib_line_chart(year_counts, "Распределение объявлений по годам", "Год", "Количество") +
            matplotlib_line_chart(price_by_year, "Медианная цена по годам выпуска", "Год", "Медианная цена, ₽")
        ),
        section("Связь цены с пробегом", 
            matplotlib_scatter(df, "mileage", "price", "Цена и пробег", "Пробег, км", "Цена, ₽")
        ),
    ]

    brand_price_rows = group_price_table(df, "brand")
    region_price_rows = group_price_table(df, "sale_region")

    rows = records_table_from_df(df.sort_values("price", ascending=True, na_position="last"), limit=40)

    sections = [
        metric_html,
        section("Краткие выводы", notes_html),
        *chart_sections,
        section("Цена по брендам", simple_table(["Бренд", "Объявлений", "Медианная цена", "Средняя цена"], brand_price_rows)),
        section("Цена по регионам", simple_table(["Регион", "Объявлений", "Медианная цена", "Средняя цена"], region_price_rows)),
        section("Примеры объявлений", simple_table(["Автомобиль", "Цена", "Пробег", "Регион", "Источник", "Ссылка"], rows)),
    ]

    html_page = render_page(
        "Обзор автомобильного рынка",
        "Статический HTML-отчёт с расчётами pandas и графиками matplotlib.",
        sections,
        meta=meta,
    )

    finish_report(file_path, html_page, {
        "status": "ok",
        "file": file_path,
        "recordsCount": metrics["records"],
        "medianPrice": metrics["medianPrice"],
        "averagePrice": metrics["meanPrice"],
        "uniqueUrls": metrics["uniqueUrls"],
    })


if __name__ == "__main__":
    main()
