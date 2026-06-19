# -*- coding: utf-8 -*-
import os
import re
import sys

sys.path.append(os.path.join(os.path.dirname(__file__), "Lib"))
from ReportUtils import *


KEY_FIELDS = [
    "url", "brand", "model", "price", "year", "sale_region", "mileage",
    "transmission", "drive_type", "engine_power", "engine_volume", "fuel_type",
]

HIGH_RISK_KEYWORDS = [
    "дтп", "авар", "бит", "перевер", "утоп", "сгор", "пожар", "не на ходу",
    "не завод", "без документов", "документов нет", "распил", "конструктор",
    "запрет", "арест", "залог", "кредит", "проблемы с документ",
]

MEDIUM_RISK_KEYWORDS = [
    "требует влож", "вложения", "корроз", "ржав", "жучк", "сварк", "под замену",
    "не работает", "ошибка", "горит чек", "чек горит", "ремонт", "под восстановление",
    "крашен", "окрас", "шпакл", "скол", "трещин", "неисправ",
]

POSITIVE_MASKS = [
    "без дтп", "дтп не", "не бит", "не бита", "не битая", "не крашен", "не крашена",
    "без авар", "аварий не", "без залога", "без ареста", "без запретов",
]


def year_bucket(year, size=5):
    year = to_int(year)
    if year is None:
        return "unknown"
    start = year - (year % size)
    return f"{start}-{start + size - 1}"


def build_reference_prices(df):
    references = {}
    priced = df.dropna(subset=["price"]).copy()
    priced = priced[priced["price"] > 0]
    if len(priced) == 0:
        references["global"] = None
        references["groups"] = []
        return references

    priced["year_bucket"] = priced["year"].apply(year_bucket)

    group_specs = [
        (["brand", "model", "body_type", "year_bucket"], 4, "медиана модели, кузова и поколения"),
        (["brand", "model", "year_bucket"], 4, "медиана модели и поколения"),
        (["brand", "model", "body_type"], 4, "медиана модели и кузова"),
        (["brand", "model"], 3, "медиана модели"),
        (["brand"], 5, "медиана бренда"),
    ]

    groups = []
    for fields, min_count, label in group_specs:
        grouped = priced.groupby(fields, dropna=False)["price"].agg(["count", "median"]).reset_index()
        groups.append({
            "fields": fields,
            "min_count": min_count,
            "label": label,
            "rows": grouped,
        })

    references["global"] = float(priced["price"].median())
    references["groups"] = groups
    return references


def reference_price_for_row(row, references):
    if references.get("global") is None:
        return None, "нет ориентира", 0

    lookup_values = dict(row)
    lookup_values["year_bucket"] = year_bucket(row.get("year"))

    for group in references.get("groups", []):
        fields = group["fields"]
        rows = group["rows"]
        mask = None
        for field in fields:
            value = clean_string(lookup_values.get(field))
            current_mask = rows[field].fillna("").astype(str).str.strip().eq(value)
            mask = current_mask if mask is None else (mask & current_mask)
        matched = rows[mask] if mask is not None else rows.iloc[0:0]
        if len(matched):
            item = matched.iloc[0]
            if int(item["count"]) >= group["min_count"]:
                return float(item["median"]), group["label"], int(item["count"])

    return references.get("global"), "медиана всей выборки", len(row)


def price_position_score(price, reference):
    price = to_float(price)
    reference = to_float(reference)
    if price is None or reference is None or reference <= 0:
        return 50, None, "нет ориентира"

    ratio = price / reference

    if ratio < 0.55:
        return 52, ratio, "сильно ниже рынка, нужен ручной осмотр"
    if ratio < 0.75:
        return 70, ratio, "ниже рынка"
    if ratio < 0.95:
        return 92, ratio, "умеренно выгоднее рынка"
    if ratio <= 1.10:
        return 86, ratio, "около рынка"
    if ratio <= 1.25:
        return 66, ratio, "выше рынка"
    if ratio <= 1.45:
        return 43, ratio, "заметно выше рынка"
    return 25, ratio, "сильно выше рынка"


def mileage_score(mileage, year):
    mileage = to_float(mileage)
    year = to_int(year)
    if mileage is None or year is None:
        return 55
    current_year = 2026
    age = max(1, current_year - year)
    expected = age * 15000
    ratio = mileage / expected if expected > 0 else 1
    if ratio <= 0.55:
        return 95
    if ratio <= 0.80:
        return 84
    if ratio <= 1.10:
        return 72
    if ratio <= 1.45:
        return 54
    if ratio <= 1.90:
        return 34
    return 18


def year_score(year, min_year, max_year):
    year = to_float(year)
    min_year = to_float(min_year)
    max_year = to_float(max_year)
    if year is None or min_year is None or max_year is None or max_year == min_year:
        return 55
    value = (year - min_year) / (max_year - min_year) * 100
    return max(15, min(95, value))


def completeness_score(row):
    filled = 0
    for field in KEY_FIELDS:
        if not is_missing(row.get(field)):
            filled += 1
    return filled / len(KEY_FIELDS) * 100


def description_text(row):
    parts = [
        row.get("description"), row.get("brand"), row.get("model"), row.get("body_type"),
        row.get("color"), row.get("transmission"), row.get("drive_type"),
    ]
    text = " ".join(clean_string(part).lower() for part in parts if not is_missing(part))
    for phrase in POSITIVE_MASKS:
        text = text.replace(phrase, " ")
    return text


def condition_risk(row):
    text = description_text(row)
    high = []
    medium = []

    for keyword in HIGH_RISK_KEYWORDS:
        if keyword in text:
            high.append(keyword)
    for keyword in MEDIUM_RISK_KEYWORDS:
        if keyword in text:
            medium.append(keyword)

    if high:
        return 2, sorted(set(high + medium))[:8], 35
    if medium:
        return 1, sorted(set(medium))[:8], 68
    return 0, [], 88


def risk_badge(risk_level):
    if risk_level >= 2:
        return "<span class='badge badge-bad'>высокий риск</span>"
    if risk_level == 1:
        return "<span class='badge badge-warn'>есть признаки риска</span>"
    return "<span class='badge badge-good'>явных рисков нет</span>"


def score_badge(score):
    css = "badge"
    if score >= 75:
        css += " badge-good"
    elif score < 45:
        css += " badge-bad"
    else:
        css += " badge-warn"
    return f"<span class='{css}'>{h(format_float(score, 1))}</span>"


def position_badge(label):
    css = "badge"
    lower = clean_string(label).lower()
    if "выгод" in lower or "около" in lower:
        css += " badge-good"
    elif "ниже" in lower:
        css += " badge-warn"
    elif "выше" in lower:
        css += " badge-bad"
    return f"<span class='{css}'>{h(label)}</span>"


def deal_rows(df, limit=40):
    rows = []
    for item in dataframe_to_records(df, limit=limit):
        flags = item.get("risk_flags") or []
        if isinstance(flags, str):
            risk_text = flags
        else:
            risk_text = ", ".join(flags) if flags else "—"
        rows.append([
            score_badge(item.get("score", 0)),
            position_badge(item.get("price_position", "")),
            h(car_label(item)),
            f"<span class='price'>{h(format_price(item.get('price')))}</span>",
            h(format_price(item.get("reference_price"))),
            h(format_percent((to_float(item.get("price_ratio")) or 0) * 100, 0) if item.get("price_ratio") is not None else "—"),
            risk_badge(to_int(item.get("risk_level")) or 0) + f"<div class='small'>{h(risk_text)}</div>",
            h(format_year(item.get("year"))),
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
        html_page = render_page("Рыночная позиция объявлений", "Нет данных для анализа.", [section("Нет данных", "<p>Выборка пуста.</p>")], meta=meta)
        finish_report(file_path, html_page, {"status": "ok", "file": file_path, "recordsCount": 0})
        return

    references = build_reference_prices(df)
    min_year = df["year"].dropna().min() if len(df["year"].dropna()) else None
    max_year = df["year"].dropna().max() if len(df["year"].dropna()) else None

    rows = []
    for item in dataframe_to_records(df):
        ref_price, ref_label, peer_count = reference_price_for_row(item, references)
        p_score, ratio, price_position = price_position_score(item.get("price"), ref_price)
        m_score = mileage_score(item.get("mileage"), item.get("year"))
        y_score = year_score(item.get("year"), min_year, max_year)
        c_score = completeness_score(item)
        risk_level, risk_flags, cond_score = condition_risk(item)

        # Слишком дешёвые объявления без проверки состояния не должны автоматически становиться лучшими.
        if ratio is not None and ratio < 0.55:
            cond_score = min(cond_score, 55)
            if "сильно низкая цена" not in risk_flags:
                risk_flags.append("сильно низкая цена")
                risk_level = max(risk_level, 1)

        total = p_score * 0.35 + m_score * 0.20 + y_score * 0.15 + c_score * 0.10 + cond_score * 0.20
        item.update({
            "score": round(total, 2),
            "price_score": round(p_score, 2),
            "mileage_score": round(m_score, 2),
            "year_score": round(y_score, 2),
            "completeness_score": round(c_score, 2),
            "condition_score": round(cond_score, 2),
            "reference_price": ref_price,
            "reference_label": ref_label,
            "peer_count": peer_count,
            "price_ratio": ratio,
            "price_position": price_position,
            "risk_level": risk_level,
            "risk_flags": risk_flags,
        })
        rows.append(item)

    import pandas as pd
    scored = pd.DataFrame(rows)

    promising = scored[
        (scored["risk_level"] <= 1) &
        ((scored["price_ratio"].isna()) | (scored["price_ratio"] >= 0.55))
    ].sort_values("score", ascending=False)

    watchlist = scored.sort_values(["risk_level", "score"], ascending=[False, True])
    overpriced = scored[scored["price_ratio"].fillna(1) > 1.25].sort_values("price_ratio", ascending=False)

    metrics = metrics_grid([
        metric_card("Оценённых объявлений", format_number(len(scored))),
        metric_card("Средняя оценка", format_float(scored["score"].mean(), 1)),
        metric_card("Медианная оценка", format_float(scored["score"].median(), 1)),
        metric_card("С признаками риска", format_number(int((scored["risk_level"] > 0).sum()))),
        metric_card("Сильно ниже рынка", format_number(int((scored["price_ratio"].fillna(1) < 0.55).sum()))),
        metric_card("Сильно выше рынка", format_number(int((scored["price_ratio"].fillna(1) > 1.45).sum()))),
    ])

    notes = [
        "Этот отчёт больше не пытается назвать объявление однозначно хорошим или плохим. Он показывает рыночную позицию: цена относительно похожих машин, пробег, год, полнота данных и явные текстовые признаки риска.",
        "Слишком низкая цена теперь не считается автоматическим плюсом. Если в описании есть признаки ДТП, ремонта, ограничений, документов или цена сильно ниже рынка, объявление получает предупреждение и не попадает в список перспективных вариантов без штрафа.",
        "Дорогая машина в хорошем состоянии не обязательно является плохой. Она может попасть в блок переоценённых только как предложение выше рынка, а не как технически слабый автомобиль.",
        "Для точной оценки нужны цели пользователя: бюджет, поколение, кузов, регион, допустимый пробег и отношение к ремонту. Этот анализатор даёт предварительную сортировку, а не экспертную диагностику машины.",
    ]

    top_for_bar = promising.head(12).copy() if len(promising) else scored.sort_values("score", ascending=False).head(12).copy()
    top_for_bar["label"] = top_for_bar.apply(lambda row: car_label(row.to_dict())[:44], axis=1)
    top_series = top_for_bar.set_index("label")["score"] if len(top_for_bar) else None

    risk_counts = scored["risk_level"].map({0: "Явных рисков нет", 1: "Есть признаки риска", 2: "Высокий риск"}).value_counts()

    sections = [
        metrics,
        section("Как читать отчёт", "".join([f"<p class='note'>{h(note)}</p>" for note in notes])),
        section("Графики рыночной позиции",
            matplotlib_histogram(scored["score"], "Распределение итоговой оценки", "Оценка 0–100", bins=24) +
            matplotlib_bar_chart(top_series, "Перспективные объявления по рейтингу", "Оценка", horizontal=True) +
            matplotlib_bar_chart(risk_counts, "Текстовые признаки риска", "Количество", horizontal=True) +
            matplotlib_scatter(scored, "price_ratio", "score", "Оценка и цена относительно ориентира", "Цена / ориентир", "Оценка", show_trend=True)
        ),
        section("Перспективные варианты", simple_table(["Оценка", "Позиция", "Автомобиль", "Цена", "Ориентир", "Цена от ориентира", "Риски", "Год", "Пробег", "Регион", "Ссылка"], deal_rows(promising, 45))),
        section("Рискованные или спорные варианты", simple_table(["Оценка", "Позиция", "Автомобиль", "Цена", "Ориентир", "Цена от ориентира", "Риски", "Год", "Пробег", "Регион", "Ссылка"], deal_rows(watchlist, 35))),
        section("Предложения заметно выше рынка", simple_table(["Оценка", "Позиция", "Автомобиль", "Цена", "Ориентир", "Цена от ориентира", "Риски", "Год", "Пробег", "Регион", "Ссылка"], deal_rows(overpriced, 30))),
    ]

    html_page = render_page(
        "Рыночная позиция объявлений",
        "Предварительная оценка цены, пробега, года, полноты данных и текстовых признаков риска.",
        sections,
        meta=meta,
    )

    finish_report(file_path, html_page, {
        "status": "ok",
        "file": file_path,
        "recordsCount": int(len(scored)),
        "bestScore": float(scored["score"].max()) if len(scored) else None,
        "medianScore": float(scored["score"].median()) if len(scored) else None,
        "riskRecordsCount": int((scored["risk_level"] > 0).sum()) if len(scored) else 0,
    })


if __name__ == "__main__":
    main()
