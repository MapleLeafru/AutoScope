# -*- coding: utf-8 -*-
import os
import sys

sys.path.append(os.path.join(os.path.dirname(__file__), "Lib"))
from ReportUtils import *


KEY_FIELDS = ["url", "brand", "model", "price", "year", "sale_region", "mileage", "transmission", "drive_type", "engine_power", "engine_volume", "fuel_type"]


def build_reference_prices(df):
    references = {}
    priced = df.dropna(subset=["price"])
    priced = priced[priced["price"] > 0]

    global_median = float(priced["price"].median()) if len(priced) else None
    brand_medians = priced.groupby("brand")["price"].median().to_dict() if len(priced) else {}
    model_medians = priced.groupby(["brand", "model"])["price"].median().to_dict() if len(priced) else {}
    model_counts = priced.groupby(["brand", "model"])["price"].count().to_dict() if len(priced) else {}
    brand_counts = priced.groupby("brand")["price"].count().to_dict() if len(priced) else {}

    references["global"] = global_median
    references["brandMedians"] = brand_medians
    references["modelMedians"] = model_medians
    references["modelCounts"] = model_counts
    references["brandCounts"] = brand_counts
    return references


def reference_price_for_row(row, references):
    brand = row.get("brand", "")
    model = row.get("model", "")
    model_key = (brand, model)

    if references["modelCounts"].get(model_key, 0) >= 3:
        return references["modelMedians"].get(model_key), "медиана похожей модели"
    if references["brandCounts"].get(brand, 0) >= 3:
        return references["brandMedians"].get(brand), "медиана бренда"
    return references["global"], "медиана выборки"


def price_score(price, reference):
    price = to_float(price)
    reference = to_float(reference)
    if price is None or reference is None or reference <= 0:
        return 45
    ratio = price / reference
    if ratio <= 0.75:
        return 100
    if ratio <= 0.90:
        return 88
    if ratio <= 1.05:
        return 75
    if ratio <= 1.20:
        return 58
    if ratio <= 1.40:
        return 38
    return 22


def mileage_score(mileage, year):
    mileage = to_float(mileage)
    year = to_int(year)
    if mileage is None or year is None:
        return 50
    # Стабильная приблизительная оценка, чтобы не зависеть от текущей даты слишком точно.
    current_year = 2026
    age = max(1, current_year - year)
    expected = age * 15000
    ratio = mileage / expected if expected > 0 else 1
    if ratio <= 0.55:
        return 100
    if ratio <= 0.80:
        return 85
    if ratio <= 1.10:
        return 70
    if ratio <= 1.45:
        return 50
    if ratio <= 1.90:
        return 30
    return 15


def year_score(year, min_year, max_year):
    year = to_float(year)
    min_year = to_float(min_year)
    max_year = to_float(max_year)
    if year is None or min_year is None or max_year is None or max_year == min_year:
        return 50
    value = (year - min_year) / (max_year - min_year) * 100
    return max(10, min(100, value))


def completeness_score(row):
    filled = 0
    for field in KEY_FIELDS:
        value = row.get(field)
        if not is_missing(value):
            filled += 1
    return filled / len(KEY_FIELDS) * 100


def score_badge(score):
    css = "badge"
    if score >= 75:
        css += " badge-good"
    elif score < 45:
        css += " badge-bad"
    else:
        css += " badge-warn"
    return f"<span class='{css}'>{h(format_float(score, 1))}</span>"


def deal_rows(df, limit=40):
    rows = []
    for item in dataframe_to_records(df, limit=limit):
        rows.append([
            score_badge(item.get("score", 0)),
            h(car_label(item)),
            f"<span class='price'>{h(format_price(item.get('price')))}</span>",
            h(format_price(item.get("reference_price"))),
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

    if len(df) == 0:
        html_page = render_page("Оценка выгодности объявлений", "Нет данных для анализа.", [section("Нет данных", "<p>Выборка пуста.</p>")], meta=meta)
        finish_report(file_path, html_page, {"status": "ok", "file": file_path, "recordsCount": 0})
        return

    references = build_reference_prices(df)
    min_year = df["year"].dropna().min() if len(df["year"].dropna()) else None
    max_year = df["year"].dropna().max() if len(df["year"].dropna()) else None

    rows = []
    for item in dataframe_to_records(df):
        ref_price, ref_label = reference_price_for_row(item, references)
        p_score = price_score(item.get("price"), ref_price)
        m_score = mileage_score(item.get("mileage"), item.get("year"))
        y_score = year_score(item.get("year"), min_year, max_year)
        c_score = completeness_score(item)
        total = p_score * 0.45 + m_score * 0.25 + y_score * 0.20 + c_score * 0.10
        item.update({
            "score": round(total, 2),
            "price_score": round(p_score, 2),
            "mileage_score": round(m_score, 2),
            "year_score": round(y_score, 2),
            "completeness_score": round(c_score, 2),
            "reference_price": ref_price,
            "reference_label": ref_label,
        })
        rows.append(item)

    scored = dataframe_from_data(rows)
    # dataframe_from_data нормализует только стандартные поля, поэтому добавляем аналитические поля заново.
    import pandas as pd
    scored = pd.DataFrame(rows)

    best = scored.sort_values("score", ascending=False)
    weak = scored.sort_values("score", ascending=True)

    metrics = metrics_grid([
        metric_card("Оценённых объявлений", format_number(len(scored))),
        metric_card("Средняя оценка", format_float(scored["score"].mean(), 1)),
        metric_card("Медианная оценка", format_float(scored["score"].median(), 1)),
        metric_card("Лучший результат", format_float(scored["score"].max(), 1)),
        metric_card("Низкий результат", format_float(scored["score"].min(), 1)),
    ])

    notes = [
        "Оценка выгодности — это не экспертная проверка автомобиля, а простой аналитический рейтинг по данным объявления.",
        "Сильнее всего учитывается цена относительно похожих объявлений. Также учитываются пробег, год выпуска и полнота данных.",
        "Низкая цена может означать выгодное предложение, но также может быть признаком проблем, скрытых условий или неполного объявления.",
    ]

    top_for_bar = best.head(12).copy()
    top_for_bar["label"] = top_for_bar.apply(lambda row: car_label(row.to_dict())[:40], axis=1)
    top_series = top_for_bar.set_index("label")["score"]

    sections = [
        metrics,
        section("Как читать рейтинг", "".join([f"<p class='note'>{h(note)}</p>" for note in notes])),
        section("Графики оценки", 
            matplotlib_histogram(scored["score"], "Распределение итоговой оценки", "Оценка 0–100", bins=10) +
            matplotlib_bar_chart(top_series, "Лучшие объявления по рейтингу", "Оценка", horizontal=True) +
            matplotlib_scatter(scored, "mileage", "score", "Оценка и пробег", "Пробег, км", "Оценка")
        ),
        section("Лучшие варианты", simple_table(["Оценка", "Автомобиль", "Цена", "Ориентир", "Год", "Пробег", "Регион", "Ссылка"], deal_rows(best, 45))),
        section("Слабые варианты", simple_table(["Оценка", "Автомобиль", "Цена", "Ориентир", "Год", "Пробег", "Регион", "Ссылка"], deal_rows(weak, 30))),
    ]

    html_page = render_page(
        "Оценка выгодности объявлений",
        "Рейтинг объявлений на основе цены, пробега, года выпуска и полноты данных.",
        sections,
        meta=meta,
    )

    finish_report(file_path, html_page, {
        "status": "ok",
        "file": file_path,
        "recordsCount": int(len(scored)),
        "bestScore": float(scored["score"].max()) if len(scored) else None,
        "medianScore": float(scored["score"].median()) if len(scored) else None,
    })


if __name__ == "__main__":
    main()
