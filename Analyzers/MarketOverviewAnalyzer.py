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




def interactive_price_mileage_chart(df, limit=2000):
    # Небольшой интерактивный график прямо внутри статического отчёта.
    # Он нужен именно для включения/выключения линии тренда без перегенерации отчёта.
    import json

    if df is None or len(df) == 0:
        return empty_chart_message()

    chart_df = df[["price", "mileage", "brand", "model", "year", "url"]].dropna(subset=["price", "mileage"]).copy()
    chart_df = chart_df[(chart_df["price"] > 0) & (chart_df["mileage"] > 0)].head(limit)
    if len(chart_df) < 2:
        return empty_chart_message()

    points = []
    for item in dataframe_to_records(chart_df):
        points.append({
            "price": to_float(item.get("price")),
            "mileage": to_float(item.get("mileage")),
            "label": car_label(item),
            "url": clean_string(item.get("url")),
        })

    json_text = json.dumps(points, ensure_ascii=False).replace("</", "<\\/").replace("\u2028", " ").replace("\u2029", " ")
    element_id = "priceMileageInteractive"

    return f"""
<div class="interactive-chart-box">
    <label class="small" style="display:flex; gap:8px; align-items:center; margin-bottom:8px;">
        <input type="checkbox" id="{element_id}_trend" checked style="width:auto;">
        Показывать линию тренда
    </label>
    <canvas id="{element_id}" style="width:100%; height:420px; display:block; border:1px solid var(--line); border-radius:14px; background:white;"></canvas>
    <p class="small">Линия тренда показывает общее направление зависимости цены от пробега. Это упрощённая линейная аппроксимация, а не точная модель стоимости.</p>
</div>
<script>
(function() {{
    const points = {json_text};
    const canvas = document.getElementById('{element_id}');
    const checkbox = document.getElementById('{element_id}_trend');
    const fmt = new Intl.NumberFormat('ru-RU');
    function draw() {{
        const ratio = window.devicePixelRatio || 1;
        const rect = canvas.getBoundingClientRect();
        const width = rect.width || 900;
        const height = 420;
        canvas.width = width * ratio;
        canvas.height = height * ratio;
        const ctx = canvas.getContext('2d');
        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        ctx.clearRect(0, 0, width, height);
        if (!points.length) {{ ctx.fillText('Нет данных', 20, 24); return; }}
        const padL = 86, padR = 24, padT = 22, padB = 56;
        const xs = points.map(p => Number(p.mileage)).filter(Number.isFinite);
        const ys = points.map(p => Number(p.price)).filter(Number.isFinite);
        const minX = Math.min(...xs), maxX = Math.max(...xs);
        const minY = Math.min(...ys), maxY = Math.max(...ys);
        const sx = x => padL + (x - minX) / (maxX - minX || 1) * (width - padL - padR);
        const sy = y => height - padB - (y - minY) / (maxY - minY || 1) * (height - padT - padB);

        ctx.strokeStyle = '#d9e0e7';
        ctx.lineWidth = 1;
        ctx.beginPath(); ctx.moveTo(padL, padT); ctx.lineTo(padL, height - padB); ctx.lineTo(width - padR, height - padB); ctx.stroke();

        ctx.fillStyle = '#667085';
        ctx.font = '12px Arial';
        ctx.fillText('Цена, ₽', 12, padT + 4);
        ctx.fillText('Пробег, км', width - 92, height - 16);
        ctx.fillText(fmt.format(Math.round(maxY)), 8, padT + 18);
        ctx.fillText(fmt.format(Math.round(minY)), 8, height - padB);
        ctx.fillText(fmt.format(Math.round(minX)), padL, height - 30);
        ctx.fillText(fmt.format(Math.round(maxX)), width - padR - 80, height - 30);

        ctx.fillStyle = 'rgba(31,111,235,.62)';
        for (const p of points) {{
            const x = sx(Number(p.mileage));
            const y = sy(Number(p.price));
            ctx.beginPath(); ctx.arc(x, y, 3, 0, Math.PI * 2); ctx.fill();
        }}

        if (checkbox.checked && points.length >= 3) {{
            const n = points.length;
            const sumX = points.reduce((a,p)=>a+Number(p.mileage),0);
            const sumY = points.reduce((a,p)=>a+Number(p.price),0);
            const sumXY = points.reduce((a,p)=>a+Number(p.mileage)*Number(p.price),0);
            const sumXX = points.reduce((a,p)=>a+Number(p.mileage)*Number(p.mileage),0);
            const denom = n * sumXX - sumX * sumX;
            if (denom !== 0) {{
                const a = (n * sumXY - sumX * sumY) / denom;
                const b = (sumY - a * sumX) / n;
                ctx.strokeStyle = '#17212b';
                ctx.lineWidth = 2.2;
                ctx.beginPath();
                ctx.moveTo(sx(minX), sy(a * minX + b));
                ctx.lineTo(sx(maxX), sy(a * maxX + b));
                ctx.stroke();
            }}
        }}
    }}
    checkbox.addEventListener('change', draw);
    window.addEventListener('resize', draw);
    draw();
}})();
</script>
"""

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
        metric_card("Медианный год", format_year(metrics["medianYear"])),
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
            interactive_price_mileage_chart(df)
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
