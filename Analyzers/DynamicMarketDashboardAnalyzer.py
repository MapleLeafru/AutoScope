# -*- coding: utf-8 -*-
import datetime
import json
import os
import sys

sys.path.append(os.path.join(os.path.dirname(__file__), "Lib"))
from ReportUtils import *


DASHBOARD_RECORD_LIMIT = 5000


def safe_json_for_script(value):
    text = json.dumps(value, ensure_ascii=False)
    return text.replace("</", "<\\/")


def prepare_dashboard_records(df):
    fields = [
        "source", "url", "brand", "model", "price", "year", "sale_region", "mileage",
        "transmission", "drive_type", "color", "body_type", "steering_wheel",
        "engine_power", "engine_volume", "fuel_type", "description",
    ]
    for field in fields:
        if field not in df.columns:
            df[field] = None
    return dataframe_to_records(df[fields].copy(), limit=DASHBOARD_RECORD_LIMIT)


def build_dashboard_html(records, total_records):
    created_at = h(datetime.datetime.now().strftime("%d.%m.%Y %H:%M:%S"))
    truncated_note = ""
    if total_records > len(records):
        truncated_note = (
            f"<div class='warning'>В интерактивный отчёт встроено {len(records)} записей из {total_records}, "
            "чтобы HTML-файл не стал слишком тяжёлым.</div>"
        )

    template = r'''<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="UTF-8">
<title>AutoScope Dynamic Dashboard</title>
<style>
:root { --bg:#f4f6f8; --card:#fff; --text:#17212b; --muted:#667085; --line:#d9e0e7; --accent:#1f6feb; --warn:#b7791f; }
* { box-sizing:border-box; }
body { margin:0; padding:26px; font-family:Arial, Helvetica, sans-serif; background:var(--bg); color:var(--text); }
a { color:var(--accent); text-decoration:none; }
a:hover { text-decoration:underline; }
.header { background:linear-gradient(135deg,#17212b,#31445a); color:white; border-radius:18px; padding:26px 30px; margin-bottom:20px; }
.header h1 { margin:0 0 10px 0; font-size:30px; }
.header p { margin:0; color:#dbe5ef; line-height:1.45; }
.meta { margin-top:14px; font-size:13px; color:#dbe5ef; }
.card { background:var(--card); border:1px solid var(--line); border-radius:14px; padding:18px; margin-bottom:18px; box-shadow:0 1px 3px rgba(16,24,40,.06); }
.grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(210px,1fr)); gap:14px; }
.controls { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:12px; }
label { display:block; font-size:12px; color:var(--muted); margin-bottom:5px; }
select,input { width:100%; padding:9px 10px; border:1px solid var(--line); border-radius:10px; background:#fff; color:var(--text); }
button { padding:10px 14px; border:0; border-radius:10px; background:var(--accent); color:white; cursor:pointer; }
.metric { background:white; border:1px solid var(--line); border-radius:14px; padding:16px; }
.metric .label { color:var(--muted); font-size:13px; margin-bottom:8px; }
.metric .value { font-size:24px; font-weight:700; }
.chart-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(430px,1fr)); gap:16px; }
.chart-card { background:white; border:1px solid var(--line); border-radius:14px; padding:14px; min-height:330px; }
.chart-card h3 { margin:0 0 10px 0; font-size:16px; }
canvas { width:100%; height:270px; display:block; }
table { width:100%; border-collapse:collapse; }
th,td { padding:9px; border-bottom:1px solid var(--line); text-align:left; vertical-align:top; font-size:13px; }
th { background:#f7f9fb; color:#344054; }
.small { color:var(--muted); font-size:13px; }
.warning { border-left:4px solid var(--warn); background:#fff9ed; padding:10px 12px; border-radius:10px; margin-bottom:16px; }
.price { font-weight:700; white-space:nowrap; }
@media (max-width:720px) { body { padding:14px; } .chart-grid { grid-template-columns:1fr; } }
</style>
</head>
<body>
<div class="header">
  <h1>Интерактивный отчёт AutoScope</h1>
  <p>Фильтры и графики работают прямо в HTML-файле без сервера. Можно быстро смотреть срезы по источнику, бренду, модели, региону, году, цене и пробегу.</p>
  <div class="meta">Сформировано: __CREATED_AT__</div>
</div>
__TRUNCATED_NOTE__
<div class="card">
  <h2>Фильтры</h2>
  <div class="controls">
    <div><label>Источник</label><select id="sourceFilter"></select></div>
    <div><label>Бренд</label><select id="brandFilter"></select></div>
    <div><label>Модель</label><select id="modelFilter"></select></div>
    <div><label>Регион</label><select id="regionFilter"></select></div>
    <div><label>Цена от</label><input id="priceMin" type="number" placeholder="например 800000"></div>
    <div><label>Цена до</label><input id="priceMax" type="number" placeholder="например 2500000"></div>
    <div><label>Год от</label><input id="yearMin" type="number" placeholder="например 2012"></div>
    <div><label>Год до</label><input id="yearMax" type="number" placeholder="например 2021"></div>
    <div><label>Пробег до</label><input id="mileageMax" type="number" placeholder="например 150000"></div>
    <div><label>Поиск</label><input id="textSearch" type="text" placeholder="бренд, модель, регион"></div>
    <div style="display:flex;align-items:end"><button id="resetButton">Сбросить фильтры</button></div>
  </div>
</div>
<div class="grid" id="metrics"></div>
<div class="chart-grid">
  <div class="chart-card"><h3>Распределение цен</h3><canvas id="priceHistogram"></canvas></div>
  <div class="chart-card"><h3>Топ брендов</h3><canvas id="brandChart"></canvas></div>
  <div class="chart-card"><h3>Топ регионов</h3><canvas id="regionChart"></canvas></div>
  <div class="chart-card"><h3>Медианная цена по годам</h3><canvas id="priceByYearChart"></canvas></div>
  <div class="chart-card"><h3>Цена и пробег</h3><canvas id="scatterChart"></canvas></div>
  <div class="chart-card"><h3>Коробка передач</h3><canvas id="transmissionChart"></canvas></div>
</div>
<div class="card">
  <h2>Таблица объявлений</h2>
  <p class="small" id="tableInfo"></p>
  <div style="overflow-x:auto"><table id="recordsTable"></table></div>
</div>
<script>
const rawData = __RECORDS_JSON__;
const RUB = new Intl.NumberFormat('ru-RU');
function clean(value) { return value === null || value === undefined ? '' : String(value).trim(); }
function num(value) { const n = Number(value); return Number.isFinite(n) ? n : null; }
function price(value) { const n = num(value); return n === null ? '—' : RUB.format(Math.round(n)) + ' ₽'; }
function number(value) { const n = num(value); return n === null ? '—' : RUB.format(Math.round(n)); }
function median(values) { const arr = values.map(num).filter(v => v !== null).sort((a,b)=>a-b); if (!arr.length) return null; const mid = Math.floor(arr.length / 2); return arr.length % 2 ? arr[mid] : (arr[mid-1] + arr[mid]) / 2; }
function mean(values) { const arr = values.map(num).filter(v => v !== null); if (!arr.length) return null; return arr.reduce((a,b)=>a+b,0) / arr.length; }
function escapeHtml(text) { return clean(text).replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch])); }
function uniqueSorted(field) { const set = new Set(rawData.map(x => clean(x[field])).filter(Boolean)); return Array.from(set).sort((a,b)=>a.localeCompare(b, 'ru')); }
function fillSelect(id, values) { const el = document.getElementById(id); el.innerHTML = '<option value="">Все</option>' + values.map(v => `<option value="${escapeHtml(v)}">${escapeHtml(v)}</option>`).join(''); }
function countBy(data, field, limit=10) { const map = new Map(); for (const item of data) { const key = clean(item[field]) || 'Не указано'; map.set(key, (map.get(key) || 0) + 1); } return Array.from(map.entries()).sort((a,b)=>b[1]-a[1]).slice(0, limit); }
function medianByYear(data) { const map = new Map(); for (const item of data) { const y = num(item.year), p = num(item.price); if (y !== null && p !== null && p > 0) { const key = String(Math.round(y)); if (!map.has(key)) map.set(key, []); map.get(key).push(p); } } return Array.from(map.entries()).map(([y, prices]) => [y, median(prices)]).sort((a,b)=>Number(a[0])-Number(b[0])); }
function priceHistogram(data, bins=10) { const prices = data.map(x => num(x.price)).filter(v => v !== null && v > 0).sort((a,b)=>a-b); if (prices.length < 2) return []; const min = prices[0], max = prices[prices.length - 1]; if (min === max) return [[price(min), prices.length]]; const step = (max - min) / bins; const result = Array.from({length: bins}, (_, i) => [RUB.format(Math.round(min + i * step)) + '–' + RUB.format(Math.round(min + (i + 1) * step)), 0]); for (const p of prices) { const idx = Math.min(bins - 1, Math.floor((p - min) / step)); result[idx][1] += 1; } return result; }
function filteredData() {
  const source = document.getElementById('sourceFilter').value;
  const brand = document.getElementById('brandFilter').value;
  const model = document.getElementById('modelFilter').value;
  const region = document.getElementById('regionFilter').value;
  const pMin = num(document.getElementById('priceMin').value), pMax = num(document.getElementById('priceMax').value);
  const yMin = num(document.getElementById('yearMin').value), yMax = num(document.getElementById('yearMax').value);
  const mMax = num(document.getElementById('mileageMax').value);
  const search = clean(document.getElementById('textSearch').value).toLowerCase();
  return rawData.filter(item => {
    const p = num(item.price), y = num(item.year), m = num(item.mileage);
    if (source && clean(item.source) !== source) return false;
    if (brand && clean(item.brand) !== brand) return false;
    if (model && clean(item.model) !== model) return false;
    if (region && clean(item.sale_region) !== region) return false;
    if (pMin !== null && (p === null || p < pMin)) return false;
    if (pMax !== null && (p === null || p > pMax)) return false;
    if (yMin !== null && (y === null || y < yMin)) return false;
    if (yMax !== null && (y === null || y > yMax)) return false;
    if (mMax !== null && (m === null || m > mMax)) return false;
    if (search) { const hay = [item.brand, item.model, item.sale_region, item.source, item.transmission, item.drive_type, item.fuel_type].map(clean).join(' ').toLowerCase(); if (!hay.includes(search)) return false; }
    return true;
  });
}
function metric(label, value, hint='') { return `<div class="metric"><div class="label">${escapeHtml(label)}</div><div class="value">${escapeHtml(value)}</div><div class="small">${escapeHtml(hint)}</div></div>`; }
function prepareCanvas(canvasId) { const canvas = document.getElementById(canvasId), ctx = canvas.getContext('2d'); const rect = canvas.getBoundingClientRect(); canvas.width = rect.width * devicePixelRatio; canvas.height = rect.height * devicePixelRatio; ctx.setTransform(devicePixelRatio, 0, 0, devicePixelRatio, 0, 0); ctx.clearRect(0,0,rect.width,rect.height); return [ctx, rect.width, rect.height]; }
function drawBar(canvasId, pairs) { const [ctx,w,h] = prepareCanvas(canvasId); if (!pairs.length) { ctx.fillText('Нет данных',14,24); return; } const left=135,right=18,top=14,rowH=Math.min(28,(h-28)/pairs.length); const max=Math.max(...pairs.map(x=>x[1]),1); ctx.font='12px Arial'; ctx.textBaseline='middle'; pairs.forEach((p,i)=>{ const y=top+i*rowH+rowH/2; const label=p[0].length>20?p[0].slice(0,19)+'…':p[0]; const bw=(w-left-right)*p[1]/max; ctx.fillStyle='#344054'; ctx.fillText(label,6,y); ctx.fillStyle='#1f6feb'; ctx.fillRect(left,y-rowH*.28,bw,rowH*.56); ctx.fillStyle='#667085'; ctx.fillText(RUB.format(Math.round(p[1])),left+bw+6,y); }); }
function drawLine(canvasId, pairs) { const [ctx,w,h] = prepareCanvas(canvasId); if (pairs.length < 2) { ctx.fillText('Недостаточно данных',14,24); return; } const pad=42, vals=pairs.map(x=>x[1]); const min=Math.min(...vals), max=Math.max(...vals), span=max-min||1; ctx.strokeStyle='#d9e0e7'; ctx.beginPath(); ctx.moveTo(pad,pad); ctx.lineTo(pad,h-pad); ctx.lineTo(w-pad,h-pad); ctx.stroke(); ctx.strokeStyle='#1f6feb'; ctx.lineWidth=2; ctx.beginPath(); pairs.forEach((p,i)=>{ const x=pad+i*(w-pad*2)/(pairs.length-1); const y=h-pad-(p[1]-min)/span*(h-pad*2); if(i===0)ctx.moveTo(x,y); else ctx.lineTo(x,y); }); ctx.stroke(); ctx.fillStyle='#1f6feb'; pairs.forEach((p,i)=>{ const x=pad+i*(w-pad*2)/(pairs.length-1); const y=h-pad-(p[1]-min)/span*(h-pad*2); ctx.beginPath(); ctx.arc(x,y,3,0,Math.PI*2); ctx.fill(); }); ctx.fillStyle='#667085'; ctx.font='11px Arial'; ctx.fillText(pairs[0][0],pad,h-12); ctx.fillText(pairs[pairs.length-1][0],w-pad-38,h-12); ctx.fillText(price(max),pad+4,pad-12); ctx.fillText(price(min),pad+4,h-pad+14); }
function drawScatter(canvasId, data) { const [ctx,w,h] = prepareCanvas(canvasId); const pts=data.map(x=>[num(x.mileage),num(x.price)]).filter(p=>p[0]!==null&&p[1]!==null&&p[0]>0&&p[1]>0).slice(0,1000); if(pts.length<2){ctx.fillText('Недостаточно данных',14,24);return;} const pad=42,xs=pts.map(p=>p[0]),ys=pts.map(p=>p[1]); const minX=Math.min(...xs),maxX=Math.max(...xs),minY=Math.min(...ys),maxY=Math.max(...ys); ctx.strokeStyle='#d9e0e7'; ctx.beginPath(); ctx.moveTo(pad,pad); ctx.lineTo(pad,h-pad); ctx.lineTo(w-pad,h-pad); ctx.stroke(); ctx.fillStyle='rgba(31,111,235,.65)'; pts.forEach(p=>{ const x=pad+(p[0]-minX)/(maxX-minX||1)*(w-pad*2); const y=h-pad-(p[1]-minY)/(maxY-minY||1)*(h-pad*2); ctx.beginPath(); ctx.arc(x,y,3,0,Math.PI*2); ctx.fill(); }); ctx.fillStyle='#667085'; ctx.font='11px Arial'; ctx.fillText('Пробег',w-82,h-12); ctx.fillText('Цена',8,22); }
function renderTable(data) { const table=document.getElementById('recordsTable'); const rows=data.slice(0,120); document.getElementById('tableInfo').textContent=`Показано ${rows.length} из ${data.length} объявлений после фильтрации.`; table.innerHTML='<thead><tr><th>Автомобиль</th><th>Цена</th><th>Год</th><th>Пробег</th><th>Регион</th><th>Источник</th><th>Ссылка</th></tr></thead><tbody>'+rows.map(x=>`<tr><td>${escapeHtml([x.brand,x.model].filter(Boolean).join(' ')||'—')}</td><td class="price">${price(x.price)}</td><td>${number(x.year)}</td><td>${number(x.mileage)}</td><td>${escapeHtml(x.sale_region||'—')}</td><td>${escapeHtml(x.source||'—')}</td><td>${x.url?`<a target="_blank" href="${escapeHtml(x.url)}">Открыть</a>`:'—'}</td></tr>`).join('')+'</tbody>'; }
function update() { const data=filteredData(); const prices=data.map(x=>num(x.price)).filter(v=>v!==null&&v>0); const years=data.map(x=>num(x.year)).filter(v=>v!==null); const mileages=data.map(x=>num(x.mileage)).filter(v=>v!==null&&v>0); document.getElementById('metrics').innerHTML=[metric('Объявлений',number(data.length),'После фильтров'),metric('Медианная цена',price(median(prices)),'По отфильтрованной выборке'),metric('Средняя цена',price(mean(prices)),'Чувствительна к выбросам'),metric('Медианный год',number(median(years)),''),metric('Медианный пробег',number(median(mileages)),'км'),metric('Источников',number(new Set(data.map(x=>clean(x.source)).filter(Boolean)).size),'')].join(''); drawBar('priceHistogram',priceHistogram(data,10)); drawBar('brandChart',countBy(data,'brand',10)); drawBar('regionChart',countBy(data,'sale_region',10)); drawLine('priceByYearChart',medianByYear(data)); drawScatter('scatterChart',data); drawBar('transmissionChart',countBy(data,'transmission',10)); renderTable(data); }
function init() { fillSelect('sourceFilter',uniqueSorted('source')); fillSelect('brandFilter',uniqueSorted('brand')); fillSelect('modelFilter',uniqueSorted('model')); fillSelect('regionFilter',uniqueSorted('sale_region')); for (const id of ['sourceFilter','brandFilter','modelFilter','regionFilter','priceMin','priceMax','yearMin','yearMax','mileageMax','textSearch']) { document.getElementById(id).addEventListener('input',update); document.getElementById(id).addEventListener('change',update); } document.getElementById('resetButton').addEventListener('click',()=>{ for (const id of ['sourceFilter','brandFilter','modelFilter','regionFilter','priceMin','priceMax','yearMin','yearMax','mileageMax','textSearch']) document.getElementById(id).value=''; update(); }); update(); }
window.addEventListener('resize', update);
init();
</script>
</body>
</html>'''

    return (
        template
        .replace("__CREATED_AT__", created_at)
        .replace("__TRUNCATED_NOTE__", truncated_note)
        .replace("__RECORDS_JSON__", safe_json_for_script(records))
    )


def main():
    data_raw, meta = read_input_payload()
    df = dataframe_from_data(data_raw)
    file_path = build_report_path(__file__)
    records = prepare_dashboard_records(df)
    html_page = build_dashboard_html(records, len(df))
    finish_report(file_path, html_page, {
        "status": "ok",
        "file": file_path,
        "recordsCount": int(len(df)),
        "embeddedRecordsCount": int(len(records)),
        "reportType": "dynamic_dashboard",
    })


if __name__ == "__main__":
    main()
